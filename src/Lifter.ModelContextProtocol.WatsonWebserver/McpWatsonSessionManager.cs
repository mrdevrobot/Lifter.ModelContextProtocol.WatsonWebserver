using ModelContextProtocol;
using System.Collections.Concurrent;

namespace Lifter.ModelContextProtocol.WatsonWebserver;

/// <summary>Keeps the live sessions and disposes the ones that went quiet.</summary>
internal sealed class McpWatsonSessionManager : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, McpWatsonSession> _sessions = new(StringComparer.Ordinal);
    private readonly TimeSpan _idleTimeout;
    private readonly Timer _sweeper;
    private int _sweeping;

    internal McpWatsonSessionManager(TimeSpan idleTimeout, TimeSpan sweepInterval)
    {
        _idleTimeout = idleTimeout;
        _sweeper = new Timer(_ => _ = SweepAsync(), null, sweepInterval, sweepInterval);
    }

    internal int Count => _sessions.Count;

    internal void Add(McpWatsonSession session) => _sessions[session.Id] = session;

    internal bool TryGet(string id, out McpWatsonSession session) => _sessions.TryGetValue(id, out session!);

    internal bool TryRemove(string id, out McpWatsonSession session) => _sessions.TryRemove(id, out session!);

    private async Task SweepAsync()
    {
        if (Interlocked.Exchange(ref _sweeping, 1) == 1)
        {
            return;
        }

        try
        {
            foreach (var entry in _sessions)
            {
                if (entry.Value.IdleFor < _idleTimeout)
                {
                    continue;
                }

                if (_sessions.TryRemove(entry.Key, out var session))
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _sweeping, 0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _sweeper.DisposeAsync().ConfigureAwait(false);

        foreach (var key in _sessions.Keys)
        {
            if (_sessions.TryRemove(key, out var session))
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
