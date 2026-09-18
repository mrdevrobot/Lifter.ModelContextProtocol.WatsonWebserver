using ModelContextProtocol.Server;

namespace ModelContextProtocol.WatsonWebserver;

/// <summary>
/// One MCP session: the transport the HTTP requests are fed to, the server running on it, and the
/// task that keeps that server pumping messages until the session ends.
/// </summary>
internal sealed class McpWatsonSession : IAsyncDisposable
{
    private readonly CancellationTokenSource _sessionClosed = new();
    private int _getStreamTaken;
    private long _lastActivityMilliseconds;

    internal McpWatsonSession(string id, StreamableHttpServerTransport transport, McpServer server)
    {
        Id = id;
        Transport = transport;
        Server = server;
        Touch();
    }

    internal string Id { get; }

    internal StreamableHttpServerTransport Transport { get; }

    internal McpServer Server { get; }

    internal Task ServerRunTask { get; set; } = Task.CompletedTask;

    internal CancellationToken SessionClosed => _sessionClosed.Token;

    internal TimeSpan IdleFor =>
        TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref _lastActivityMilliseconds));

    internal void Touch() => Interlocked.Exchange(ref _lastActivityMilliseconds, Environment.TickCount64);

    /// <summary>
    /// Reserves the single standalone GET stream a session may have open. The specification lets a
    /// client keep at most one.
    /// </summary>
    internal bool TryTakeGetStream() => Interlocked.Exchange(ref _getStreamTaken, 1) == 0;

    internal void ReleaseGetStream() => Interlocked.Exchange(ref _getStreamTaken, 0);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _sessionClosed.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            await ServerRunTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // A session torn down while its server was mid-flight must not take the sweeper with it.
        }

        await Server.DisposeAsync().ConfigureAwait(false);
        await Transport.DisposeAsync().ConfigureAwait(false);
        _sessionClosed.Dispose();
    }
}
