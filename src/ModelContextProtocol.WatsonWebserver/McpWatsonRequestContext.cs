using global::WatsonWebserver.Core;

namespace ModelContextProtocol.WatsonWebserver;

/// <summary>
/// The Watson request a tool, prompt or resource handler is currently running for.
/// </summary>
/// <remarks>
/// The endpoint flows the execution context of the HTTP request into the server's handling of the
/// JSON-RPC message it carried, so a handler reached from a POST reads the very request that
/// carried it. A handler reached any other way — a notification the server raises on its own, a
/// message replayed on the GET stream — sees <see langword="null"/>.
/// </remarks>
public sealed class McpWatsonRequestContext
{
    private static readonly AsyncLocal<McpWatsonRequestContext?> Ambient = new();

    internal McpWatsonRequestContext(HttpContextBase httpContext, string? sessionId)
    {
        HttpContext = httpContext;
        SessionId = sessionId;
    }

    /// <summary>The Watson context of the request being served.</summary>
    public HttpContextBase HttpContext { get; }

    /// <summary>The MCP session the request belongs to, or <see langword="null"/> when stateless.</summary>
    public string? SessionId { get; }

    /// <summary>The context of the request being served, or <see langword="null"/> outside one.</summary>
    public static McpWatsonRequestContext? Current => Ambient.Value;

    /// <summary>Reads a request header, or <see langword="null"/> when it is absent.</summary>
    public string? Header(string name) => HttpContext.Request.Headers[name];

    internal static void SetCurrent(McpWatsonRequestContext? context) => Ambient.Value = context;
}
