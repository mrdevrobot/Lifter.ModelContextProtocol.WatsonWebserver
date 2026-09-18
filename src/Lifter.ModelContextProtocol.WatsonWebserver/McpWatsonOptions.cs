using ModelContextProtocol;
using global::WatsonWebserver.Core;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Lifter.ModelContextProtocol.WatsonWebserver;

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

    /// <summary>How often idle sessions are swept. Default five seconds.</summary>
    public TimeSpan IdleSweepInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Origins allowed to call the endpoint. Empty means every origin is accepted, which is what a
    /// server bound to loopback and reached by a local client needs; populate it whenever the
    /// listener is reachable by a browser, so a page on another origin cannot drive the server.
    /// </summary>
    public IList<string> AllowedOrigins { get; } = new List<string>();

    /// <summary>
    /// Protocol revisions the endpoint accepts in the <c>MCP-Protocol-Version</c> header. A request
    /// naming anything else is rejected with 400 and the supported list, which is how a client
    /// negotiates down to a revision this server speaks.
    /// </summary>
    public IList<string> SupportedProtocolVersions { get; } = new List<string>
    {
        "2024-11-05",
        "2025-03-26",
        "2025-06-18",
        "2025-11-25",
    };

    /// <summary>
    /// Whether the route is registered after Watson's authentication phase, so
    /// Watson's authentication route runs before the endpoint. Default true.
    /// </summary>
    public bool RequireAuthentication { get; set; } = true;

    /// <summary>Service provider handed to the <see cref="McpServer"/> of every session.</summary>
    public IServiceProvider? Services { get; set; }

    /// <summary>Logger factory handed to the transport and the server.</summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// Invoked before a session's <see cref="McpServer"/> is created, with the request that opened
    /// it and a per-session copy of the server options, so a session can be configured from the
    /// caller's headers or identity.
    /// </summary>
    public Func<HttpContextBase, McpServerOptions, CancellationToken, Task>? ConfigureSession { get; set; }
}
