namespace IQOne.Zero.Web.Binding;

/// <summary>
/// Passes a stream through until it has produced more bytes than it is allowed to.
/// </summary>
/// <remarks>
/// The count is kept as the bytes arrive rather than checked afterwards, because a chunked
/// body declares no length: refusing it after the fact would mean having already held all of
/// it, which is the cost the limit exists to avoid.
/// </remarks>
/// <param name="inner">The body being read.</param>
/// <param name="limit">The largest number of bytes to pass through.</param>
/// <param name="requestType">The request being bound, for the exception.</param>
internal sealed class CappedStream(Stream inner, long limit, Type requestType) : Stream
{
    private long _read;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _read;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Counted(inner.Read(buffer, offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        => Counted(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

    public override async Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => Counted(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false));

    public override void Flush()
    {
        // Nothing is written through this stream, so there is nothing to flush.
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Counted(int read)
    {
        _read += read;

        if (_read > limit) throw new RequestBodyTooLargeException(requestType, limit);

        return read;
    }
}
