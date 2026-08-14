namespace WebApi.Infrastructure;

/// <summary>
/// Reads from inner once; fans bytes out to an optional secondary consumer (e.g. CRC).
/// </summary>
public sealed class TeeReadStream : Stream
{
    private readonly Stream _inner;
    private readonly Action<ReadOnlyMemory<byte>> _onRead;

    public TeeReadStream(Stream inner, Action<ReadOnlyMemory<byte>> onRead)
    {
        _inner = inner;
        _onRead = onRead;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        if (read > 0)
            _onRead(buffer[..read]);
        return read;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        if (read > 0)
            _onRead(buffer.AsMemory(offset, read));
        return read;
    }

    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
}
