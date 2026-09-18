using ModelContextProtocol;
using System.Runtime.InteropServices;
using global::WatsonWebserver.Core;

namespace Lifter.ModelContextProtocol.WatsonWebserver;

/// <summary>
/// A write-only stream over a Watson response body. The first write starts the response, so a
/// handler that ends up writing nothing can still choose a status code and send an empty body.
/// Writing after the stream has been disposed throws <see cref="ObjectDisposedException"/>.
/// </summary>
internal sealed class WatsonChunkStream : Stream
{
    private readonly HttpContextBase _context;
    private readonly Action _onFirstWrite;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _started;
    private bool _finished;
    private volatile bool _disposed;

    internal WatsonChunkStream(HttpContextBase context, Action onFirstWrite)
    {
        _context = context;
        _onFirstWrite = onFirstWrite;
    }

    internal bool Started => _started;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (buffer.IsEmpty)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_finished)
            {
                return;
            }

            if (!_started)
            {
                _started = true;
                _onFirstWrite();
            }

            await _context.Response.SendChunk(AsChunk(buffer), false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Write(byte[] buffer, int offset, int count)
        => WriteAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>
    /// Watson only sends a chunk from a <see cref="byte"/> array, so a buffer that is not already
    /// one whole array is copied into one.
    /// </summary>
    private static byte[] AsChunk(ReadOnlyMemory<byte> buffer) =>
        MemoryMarshal.TryGetArray(buffer, out var segment) &&
        segment.Array is { } array &&
        segment.Offset == 0 &&
        segment.Count == array.Length
            ? array
            : buffer.ToArray();

    /// <summary>Closes the chunked body when anything was written to it. A no-op once disposed.</summary>
    internal async Task CompleteAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_started || _finished)
            {
                return;
            }

            _finished = true;
            await _context.Response.SendChunk(Array.Empty<byte>(), true, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _gate.Dispose();
        }

        base.Dispose(disposing);
    }
}
