using ModelContextProtocol;
namespace Lifter.ModelContextProtocol.WatsonWebserver;

/// <summary>
/// The MCP endpoint mounted on a Watson server. Disposing it ends every live session; the routes
/// stay registered, so dispose it when the server itself is going away.
/// </summary>
public sealed class McpWatsonEndpoint : IAsyncDisposable
{
    private readonly McpWatsonHandler _handler;

    internal McpWatsonEndpoint(string path, McpWatsonHandler handler)
    {
        Path = path;
        _handler = handler;
    }

    /// <summary>The route the endpoint answers on.</summary>
    public string Path { get; }

    /// <summary>How many sessions are currently alive. Always zero in stateless mode.</summary>
    public int ActiveSessionCount => _handler.ActiveSessionCount;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _handler.DisposeAsync();
}
