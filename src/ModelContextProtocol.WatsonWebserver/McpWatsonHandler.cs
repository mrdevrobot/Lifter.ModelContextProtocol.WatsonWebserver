using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using global::WatsonWebserver.Core;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.WatsonWebserver;

/// <summary>
/// Turns Watson requests into calls on the SDK's Streamable HTTP transport: the whole package, once
/// the routes are registered.
/// </summary>
internal sealed class McpWatsonHandler : IAsyncDisposable
{
    internal const string SessionIdHeader = "Mcp-Session-Id";
    internal const string ProtocolVersionHeader = "MCP-Protocol-Version";

    private static readonly JsonTypeInfo<JsonRpcMessage> MessageTypeInfo =
        (JsonTypeInfo<JsonRpcMessage>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcMessage));

    private static readonly JsonTypeInfo<JsonRpcError> ErrorTypeInfo =
        (JsonTypeInfo<JsonRpcError>)McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcError));

    private static readonly JsonTypeInfo<UnsupportedProtocolVersionErrorData> UnsupportedVersionTypeInfo =
        (JsonTypeInfo<UnsupportedProtocolVersionErrorData>)McpJsonUtilities.DefaultOptions
            .GetTypeInfo(typeof(UnsupportedProtocolVersionErrorData));

    private readonly McpServerOptions _serverOptions;
    private readonly McpWatsonOptions _options;
    private readonly McpWatsonSessionManager _sessions;
    private readonly ILoggerFactory? _loggerFactory;

    private static ReadOnlyMemory<byte> SseComment => ": mcp\n\n"u8.ToArray();

    internal McpWatsonHandler(McpServerOptions serverOptions, McpWatsonOptions options)
    {
        _serverOptions = serverOptions;
        _options = options;
        _loggerFactory = options.LoggerFactory;
        _sessions = new McpWatsonSessionManager(options.IdleTimeout, options.IdleSweepInterval);
    }

    internal int ActiveSessionCount => _sessions.Count;

    internal async Task HandlePostAsync(HttpContextBase context)
    {
        if (!CheckOrigin(context))
        {
            await WriteErrorAsync(context, "Forbidden: Origin not allowed.", 403).ConfigureAwait(false);
            return;
        }

        var accept = context.Request.Headers["Accept"] ?? string.Empty;
        if (!AcceptsJson(accept) || !AcceptsEventStream(accept))
        {
            await WriteErrorAsync(context,
                "Not Acceptable: Client must accept both application/json and text/event-stream.", 406)
                .ConfigureAwait(false);
            return;
        }

        JsonRpcMessage? message;
        try
        {
            message = ParseMessage(context);
        }
        catch (JsonException)
        {
            message = null;
        }

        if (message is null)
        {
            await WriteErrorAsync(context,
                "Bad Request: The POST body did not contain a valid JSON-RPC message.", 400,
                (int)McpErrorCode.InvalidRequest).ConfigureAwait(false);
            return;
        }

        var requestId = message is JsonRpcRequest request ? request.Id : default;

        if (!ValidateProtocolVersion(context, out var versionError))
        {
            await WriteErrorDetailAsync(context, versionError, 400, requestId).ConfigureAwait(false);
            return;
        }

        var session = await GetOrCreateSessionAsync(context, message, requestId).ConfigureAwait(false);
        if (session is null)
        {
            return;
        }

        var stateless = _options.Stateless;
        var stream = new WatsonChunkStream(context, () => StartEventStream(context));
        var previous = McpWatsonRequestContext.Current;
        McpWatsonRequestContext.SetCurrent(new McpWatsonRequestContext(context, stateless ? null : session.Id));

        try
        {
            var wroteResponse = await session.Transport
                .HandlePostRequestAsync(message, stream, context.Token)
                .ConfigureAwait(false);

            if (wroteResponse)
            {
                await stream.CompleteAsync().ConfigureAwait(false);
            }
            else
            {
                context.Response.StatusCode = 202;
                await context.Response.Send(context.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            McpWatsonRequestContext.SetCurrent(previous);
            await stream.DisposeAsync().ConfigureAwait(false);

            if (stateless)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                session.Touch();
            }
        }
    }

    internal async Task HandleGetAsync(HttpContextBase context)
    {
        if (!CheckOrigin(context))
        {
            await WriteErrorAsync(context, "Forbidden: Origin not allowed.", 403).ConfigureAwait(false);
            return;
        }

        if (_options.Stateless)
        {
            context.Response.Headers["Allow"] = "POST";
            await WriteErrorAsync(context,
                "Method Not Allowed: The server-to-client stream is not available in stateless mode.", 405)
                .ConfigureAwait(false);
            return;
        }

        if (!AcceptsEventStream(context.Request.Headers["Accept"] ?? string.Empty))
        {
            await WriteErrorAsync(context, "Not Acceptable: Client must accept text/event-stream.", 406)
                .ConfigureAwait(false);
            return;
        }

        if (!ValidateProtocolVersion(context, out var versionError))
        {
            await WriteErrorDetailAsync(context, versionError, 400).ConfigureAwait(false);
            return;
        }

        var session = await GetSessionAsync(context).ConfigureAwait(false);
        if (session is null)
        {
            return;
        }

        if (!session.TryTakeGetStream())
        {
            await WriteErrorAsync(context,
                "Bad Request: This server does not support multiple GET requests for the same session.", 400)
                .ConfigureAwait(false);
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.Token, session.SessionClosed);
        var stream = new WatsonChunkStream(context, () => StartEventStream(context));

        try
        {
            // Writing a comment first flushes the response headers, so the client sees an open
            // stream before the server has anything unsolicited to say.
            await stream.WriteAsync(SseComment, cts.Token).ConfigureAwait(false);
            await session.Transport.HandleGetRequestAsync(stream, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
            // The client went away mid-stream; that is how an SSE connection normally ends.
        }
        finally
        {
            session.ReleaseGetStream();
            session.Touch();
            await stream.CompleteAsync().ConfigureAwait(false);
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal async Task HandleDeleteAsync(HttpContextBase context)
    {
        if (!CheckOrigin(context))
        {
            await WriteErrorAsync(context, "Forbidden: Origin not allowed.", 403).ConfigureAwait(false);
            return;
        }

        if (_options.Stateless)
        {
            context.Response.Headers["Allow"] = "POST";
            await WriteErrorAsync(context,
                "Method Not Allowed: There is no session to end in stateless mode.", 405).ConfigureAwait(false);
            return;
        }

        if (!ValidateProtocolVersion(context, out var versionError))
        {
            await WriteErrorDetailAsync(context, versionError, 400).ConfigureAwait(false);
            return;
        }

        var sessionId = context.Request.Headers[SessionIdHeader];
        if (string.IsNullOrEmpty(sessionId))
        {
            await WriteErrorAsync(context,
                $"Bad Request: The {SessionIdHeader} header is required to end a session.", 400).ConfigureAwait(false);
            return;
        }

        if (!_sessions.TryRemove(sessionId!, out var session))
        {
            await WriteErrorAsync(context, "Session not found", 404, -32001).ConfigureAwait(false);
            return;
        }

        await session.DisposeAsync().ConfigureAwait(false);
        context.Response.StatusCode = 204;
        await context.Response.Send(context.Token).ConfigureAwait(false);
    }

    internal async Task HandleUnsupportedMethodAsync(HttpContextBase context)
    {
        context.Response.Headers["Allow"] = _options.Stateless ? "POST" : "GET, POST, DELETE";
        await WriteErrorAsync(context,
            $"Method Not Allowed: {context.Request.Method} is not supported by the MCP endpoint.", 405)
            .ConfigureAwait(false);
    }

    private async ValueTask<McpWatsonSession?> GetOrCreateSessionAsync(
        HttpContextBase context,
        JsonRpcMessage message,
        RequestId requestId)
    {
        if (_options.Stateless)
        {
            return await StartSessionAsync(context, stateless: true).ConfigureAwait(false);
        }

        var sessionId = context.Request.Headers[SessionIdHeader];
        if (!string.IsNullOrEmpty(sessionId))
        {
            return await GetSessionAsync(context, requestId).ConfigureAwait(false);
        }

        if (message is not JsonRpcRequest { Method: RequestMethods.Initialize })
        {
            await WriteErrorAsync(context,
                $"Bad Request: A new session can only be created by an initialize request. Send the {SessionIdHeader} " +
                "header returned by initialize, or mount the endpoint with Stateless = true.", 400,
                requestId: requestId).ConfigureAwait(false);
            return null;
        }

        return await StartSessionAsync(context, stateless: false).ConfigureAwait(false);
    }

    private async ValueTask<McpWatsonSession?> GetSessionAsync(HttpContextBase context, RequestId requestId = default)
    {
        var sessionId = context.Request.Headers[SessionIdHeader];
        if (string.IsNullOrEmpty(sessionId))
        {
            await WriteErrorAsync(context,
                $"Bad Request: The {SessionIdHeader} header is required when the server is using sessions. " +
                "Mount the endpoint with Stateless = true if your server does not need them.", 400,
                requestId: requestId).ConfigureAwait(false);
            return null;
        }

        if (!_sessions.TryGet(sessionId!, out var session))
        {
            // -32001 is what the TypeScript SDK answers for an unknown session, and what clients look for.
            await WriteErrorAsync(context, "Session not found", 404, -32001, requestId).ConfigureAwait(false);
            return null;
        }

        session.Touch();
        context.Response.Headers[SessionIdHeader] = session.Id;
        return session;
    }

    private async ValueTask<McpWatsonSession> StartSessionAsync(HttpContextBase context, bool stateless)
    {
        StreamableHttpServerTransport transport;
        string sessionId;

        if (stateless)
        {
            sessionId = string.Empty;
            transport = new StreamableHttpServerTransport(_loggerFactory)
            {
                Stateless = true,
                FlowExecutionContextFromRequests = true,
            };
        }
        else
        {
            sessionId = NewSessionId();
            transport = new StreamableHttpServerTransport(_loggerFactory)
            {
                SessionId = sessionId,
                FlowExecutionContextFromRequests = true,
            };

            context.Response.Headers[SessionIdHeader] = sessionId;
        }

        var serverOptions = _serverOptions;
        if (_options.ConfigureSession is { } configure)
        {
            serverOptions = CloneServerOptions(_serverOptions);
            await configure(context, serverOptions, context.Token).ConfigureAwait(false);
        }

        var server = McpServer.Create(transport, serverOptions, _loggerFactory, _options.Services);
        var session = new McpWatsonSession(sessionId, transport, server);
        session.ServerRunTask = RunServerAsync(server, session.SessionClosed);

        if (!stateless)
        {
            _sessions.Add(session);
        }

        return session;
    }

    private static async Task RunServerAsync(McpServer server, CancellationToken sessionClosed)
    {
        try
        {
            await server.RunAsync(sessionClosed).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static McpServerOptions CloneServerOptions(McpServerOptions source) => new()
    {
        ServerInfo = source.ServerInfo,
        Capabilities = source.Capabilities,
        ProtocolVersion = source.ProtocolVersion,
        InitializationTimeout = source.InitializationTimeout,
        ServerInstructions = source.ServerInstructions,
        ScopeRequests = source.ScopeRequests,
        KnownClientInfo = source.KnownClientInfo,
        KnownClientCapabilities = source.KnownClientCapabilities,
        Handlers = source.Handlers,
        Filters = source.Filters,
        ToolCollection = source.ToolCollection,
        ResourceCollection = source.ResourceCollection,
        PromptCollection = source.PromptCollection,
    };

    private static JsonRpcMessage? ParseMessage(HttpContextBase context)
    {
        var body = context.Request.DataAsBytes;
        if (body is null || body.Length == 0)
        {
            return null;
        }

        var message = JsonSerializer.Deserialize(body, MessageTypeInfo);
        if (message is null)
        {
            return null;
        }

        var protocolVersion = context.Request.Headers[ProtocolVersionHeader];
        if (!string.IsNullOrEmpty(protocolVersion))
        {
            message.Context ??= new JsonRpcMessageContext();
            message.Context.ProtocolVersion = protocolVersion;
        }

        return message;
    }

    private bool ValidateProtocolVersion(HttpContextBase context, out JsonRpcErrorDetail? errorDetail)
    {
        var requested = context.Request.Headers[ProtocolVersionHeader];
        if (string.IsNullOrEmpty(requested) || _options.SupportedProtocolVersions.Contains(requested!))
        {
            errorDetail = null;
            return true;
        }

        errorDetail = new JsonRpcErrorDetail
        {
            Code = (int)McpErrorCode.UnsupportedProtocolVersion,
            Message = $"Bad Request: The {ProtocolVersionHeader} header value '{requested}' is not supported.",
            Data = JsonSerializer.SerializeToNode(
                new UnsupportedProtocolVersionErrorData
                {
                    Supported = new List<string>(_options.SupportedProtocolVersions),
                    Requested = requested!,
                },
                UnsupportedVersionTypeInfo),
        };

        return false;
    }

    private bool CheckOrigin(HttpContextBase context)
    {
        if (_options.AllowedOrigins.Count == 0)
        {
            return true;
        }

        var origin = context.Request.Headers["Origin"];
        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }

        foreach (var allowed in _options.AllowedOrigins)
        {
            if (string.Equals(allowed, origin, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void StartEventStream(HttpContextBase context)
    {
        context.Response.ServerSentEvents = true;
        context.Response.ChunkedTransfer = true;
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers["Cache-Control"] = "no-cache,no-store";
        context.Response.Headers["X-Accel-Buffering"] = "no";
    }

    private static bool AcceptsJson(string accept) => Accepts(accept, "application/json");

    private static bool AcceptsEventStream(string accept) => Accepts(accept, "text/event-stream");

    private static bool Accepts(string accept, string mediaType)
    {
        foreach (var part in accept.Split(','))
        {
            var value = part.Split(';')[0].Trim();
            if (value.Equals(mediaType, StringComparison.OrdinalIgnoreCase) ||
                value.Equals("*/*", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static Task WriteErrorAsync(
        HttpContextBase context,
        string message,
        int statusCode,
        int errorCode = -32000,
        RequestId requestId = default)
        => WriteErrorDetailAsync(
            context,
            new JsonRpcErrorDetail { Code = errorCode, Message = message },
            statusCode,
            requestId);

    private static async Task WriteErrorDetailAsync(
        HttpContextBase context,
        JsonRpcErrorDetail? errorDetail,
        int statusCode,
        RequestId requestId = default)
    {
        var error = new JsonRpcError
        {
            Id = requestId,
            Error = errorDetail ?? new JsonRpcErrorDetail { Code = -32000, Message = "Bad Request" },
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.Send(JsonSerializer.Serialize(error, ErrorTypeInfo), context.Token).ConfigureAwait(false);
    }

    private static string NewSessionId()
    {
        Span<byte> buffer = stackalloc byte[16];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public ValueTask DisposeAsync() => _sessions.DisposeAsync();
}
