using ModelContextProtocol;
using System.Net;
using System.Net.Sockets;
using global::WatsonWebserver;
using global::WatsonWebserver.Core;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Lifter.ModelContextProtocol.WatsonWebserver.Tests;

/// <summary>A real Watson listener on a free loopback port with an MCP endpoint mounted on it.</summary>
internal sealed class McpWatsonTestServer : IAsyncDisposable
{
    private readonly Webserver _webserver;
    private readonly McpWatsonEndpoint _endpoint;

    private McpWatsonTestServer(Webserver webserver, McpWatsonEndpoint endpoint, McpServerOptions serverOptions, int port)
    {
        _webserver = webserver;
        _endpoint = endpoint;
        ServerOptions = serverOptions;
        Uri = new Uri($"http://127.0.0.1:{port}{endpoint.Path}");
    }

    internal Uri Uri { get; }

    internal McpServerOptions ServerOptions { get; }

    internal int ActiveSessionCount => _endpoint.ActiveSessionCount;

    /// <summary>Exceptions Watson caught out of a route handler; a healthy run leaves this empty.</summary>
    internal List<Exception> Failures { get; private init; } = new();

    internal Exception[] CapturedFailures()
    {
        lock (Failures)
        {
            return Failures.ToArray();
        }
    }

    internal static McpWatsonTestServer Start(Action<McpWatsonOptions>? configure = null)
    {
        var serverOptions = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "watson-test-server", Version = "1.0.0" },
            ServerInstructions = "A server hosted on WatsonWebserver.",
        };

        serverOptions.WithTools<TestTools>().WithPrompts<TestPrompts>();

        var port = FreePort();
        var webserver = new Webserver(new WebserverSettings("127.0.0.1", port), DefaultRouteAsync);
        var failures = new List<Exception>();
        webserver.Routes.Exception = (context, exception) =>
        {
            lock (failures)
            {
                failures.Add(exception);
            }

            context.Response.StatusCode = 500;
            return context.Response.Send(exception.ToString(), context.Token);
        };

        var endpoint = webserver.MapMcp(serverOptions, configure);
        webserver.Start();

        return new McpWatsonTestServer(webserver, endpoint, serverOptions, port) { Failures = failures };
    }

    internal Task<McpClient> ConnectAsync(IDictionary<string, string>? headers = null, bool standaloneGetStream = false)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = Uri,
            TransportMode = HttpTransportMode.StreamableHttp,
            EnableStandaloneGetStream = standaloneGetStream,
        };

        if (headers is not null)
        {
            options.AdditionalHeaders = headers;
        }

        return McpClient.CreateAsync(new HttpClientTransport(options));
    }

    internal HttpClient CreateHttpClient() => new() { BaseAddress = Uri };

    private static Task DefaultRouteAsync(HttpContextBase context)
    {
        context.Response.StatusCode = 404;
        return context.Response.Send(context.Token);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await _endpoint.DisposeAsync();
        _webserver.Stop();
        _webserver.Dispose();
    }
}
