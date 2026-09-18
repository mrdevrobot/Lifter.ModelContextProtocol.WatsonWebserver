using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.WatsonWebserver;

/// <summary>
/// One MCP session: the transport the HTTP requests are fed to, the server running on it, and the
/// task that keeps that server pumping messages until the session ends.
/// </summary>
internal sealed class McpWatsonSession : IAsyncDisposable
{
    /// <summary>
    /// How long disposal waits for the requests still running on the session. A standalone GET
    /// stream ends as soon as the session token is cancelled, so this only bounds a handler that
    /// ignores cancellation.
    /// </summary>
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _sessionClosed = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ILogger? _logger;
    private int _activeRequests;
    private int _getStreamTaken;
    private long _lastActivityMilliseconds;
    private volatile bool _closing;

    internal McpWatsonSession(string id, StreamableHttpServerTransport transport, McpServer server, ILogger? logger)
    {
        Id = id;
        Transport = transport;
        Server = server;
        _logger = logger;
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
    /// Marks a request as running on the session. Disposal waits for every outstanding reference, so
    /// the transport is never disposed underneath a response that is still being written.
    /// </summary>
    internal IDisposable AcquireReference()
    {
        Interlocked.Increment(ref _activeRequests);
        return new Reference(this);
    }

    /// <summary>
    /// Reserves the single standalone GET stream a session may have open. The specification lets a
    /// client keep at most one.
    /// </summary>
    internal bool TryTakeGetStream() => Interlocked.Exchange(ref _getStreamTaken, 1) == 0;

    internal void ReleaseGetStream() => Interlocked.Exchange(ref _getStreamTaken, 0);

    public async ValueTask DisposeAsync()
    {
        _closing = true;

        try
        {
            await _sessionClosed.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }

        if (Volatile.Read(ref _activeRequests) == 0)
        {
            _drained.TrySetResult();
        }

        await AwaitBoundedAsync(_drained.Task, "requests in flight").ConfigureAwait(false);
        await AwaitBoundedAsync(ServerRunTask, "the server loop").ConfigureAwait(false);

        await Server.DisposeAsync().ConfigureAwait(false);
        await Transport.DisposeAsync().ConfigureAwait(false);
        _sessionClosed.Dispose();
    }

    private async Task AwaitBoundedAsync(Task task, string what)
    {
        try
        {
            await task.WaitAsync(DrainTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger?.LogWarning("MCP session {SessionId} gave up waiting for {What} after {Timeout}.", Id, what, DrainTimeout);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "MCP session {SessionId} failed while waiting for {What}.", Id, what);
        }
    }

    private void ReleaseReference()
    {
        if (Interlocked.Decrement(ref _activeRequests) == 0 && _closing)
        {
            _drained.TrySetResult();
        }
    }

    private sealed class Reference(McpWatsonSession session) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                session.ReleaseReference();
            }
        }
    }
}
