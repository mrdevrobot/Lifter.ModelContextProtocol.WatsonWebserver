namespace ModelContextProtocol.WatsonWebserver;

/// <summary>
/// How the MCP endpoint is mounted on a Watson server.
/// </summary>
public sealed class McpWatsonOptions
{
    /// <summary>The route the endpoint answers on. Default <c>/mcp</c>.</summary>
    public string Path { get; set; } = "/mcp";

    /// <summary>
    /// When true, every request is served by a fresh server instance and no session is kept: the
    /// client's <c>Mcp-Session-Id</c> is ignored and the GET stream is not offered.
    /// </summary>
    public bool Stateless { get; set; }

    /// <summary>How long an idle session lives before it is disposed. Default two hours.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromHours(2);
}
