namespace BulkSharp.Processing.Storage;

/// <summary>
/// A read-only stream wrapper that throws when cumulative bytes read exceeds a configured limit.
/// Used to enforce file size limits on non-seekable streams where Length is unavailable.
/// </summary>
internal sealed class MaxLengthStream(Stream inner, long maxBytes) : Stream
{
    private long _totalRead;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => _totalRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        _totalRead += read;
        ThrowIfExceeded();
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        _totalRead += read;
        ThrowIfExceeded();
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        _totalRead += read;
        ThrowIfExceeded();
        return read;
    }

    public override void Flush() => inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }

    private void ThrowIfExceeded()
    {
        if (_totalRead > maxBytes)
            throw new ArgumentException(
                $"Stream size ({_totalRead} bytes) exceeds maximum allowed size ({maxBytes} bytes).");
    }
}
