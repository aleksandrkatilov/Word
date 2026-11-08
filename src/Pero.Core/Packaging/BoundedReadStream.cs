namespace Pero.Core.Packaging;

/// <summary>
/// Обёртка чтения, которая не доверяет размерам из заголовков ZIP: считает фактически
/// распакованные байты и прерывает чтение при превышении бюджета.
/// </summary>
public sealed class BoundedReadStream(Stream inner, DecompressionBudget budget) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = inner.Read(buffer, offset, count);
        budget.Consume(n);
        return n;
    }

    public override int Read(Span<byte> buffer)
    {
        int n = inner.Read(buffer);
        budget.Consume(n);
        return n;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}

public sealed class DecompressionBudget(long maxBytes)
{
    public long Consumed { get; private set; }

    public void Consume(long n)
    {
        Consumed += n;
        if (Consumed > maxBytes)
            throw new PackageRejectedException(
                $"Распакованный объём превышает допустимые {maxBytes / (1024 * 1024)} МБ.");
    }
}

public sealed class PackageRejectedException(string message) : Exception(message);
