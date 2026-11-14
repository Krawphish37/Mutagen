namespace Mutagen.Bethesda.Archives;

/// <summary>
/// The unpacked content of a compressed archive entry, read through a decompressing stream: exactly the entry's
/// unpacked size. Its position counts the bytes it has given out.<br/>
/// A decompressing stream's own position is the position of the archive file it reads from (SharpZipLib's
/// InflaterInputStream), and it reads ahead in that file. Bounding it with FramedStream, which takes the inner
/// position as the entry's, asked it for a negative count once the read-ahead passed the unpacked size: "count cannot
/// be negative" on any read but one of exactly the entry's size (C193; 54,906 of 129,599 Starfield meshes).
/// </summary>
internal sealed class DecompressedEntryStream : Stream
{
    private readonly Stream _decompressing;
    private readonly long _length;
    private long _position;

    /// <param name="decompressing">The decompressing stream, already at the entry's start; it is disposed with this</param>
    /// <param name="length">The entry's unpacked size</param>
    public DecompressedEntryStream(Stream decompressing, long length)
    {
        _decompressing = decompressing;
        _length = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException("An archive entry's unpacked content can only be read in order");
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var want = (int)Math.Min(buffer.Length, _length - _position);
        if (want <= 0) return 0;
        var read = _decompressing.Read(buffer[..want]);
        _position += read;
        return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("An archive entry's unpacked content can only be read in order");

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _decompressing.Dispose();
        }
        base.Dispose(disposing);
    }
}
