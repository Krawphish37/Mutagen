using System.Buffers.Binary;
using System.IO.Compression;
using System.Reactive.Disposables;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Binary.Translations;

public readonly struct CompressionExport : IDisposable
{
    // One per compressed record written, and most records are small.
    // MemoryStream grows with the content, where MemoryTributary allocates a 64 KB block on first write
    private readonly MemoryStream _tributary = new();

    // The record's compressed content as read, written instead when it holds exactly the bytes written (CompressedOriginals)
    private readonly ReadOnlyMemorySlice<byte>? _original;

    /// <summary>
    /// Writer being tracked
    /// </summary>
    public readonly MutagenWriter OriginalWriter;

    /// <summary>
    /// Writer being used for compression
    /// </summary>
    public MutagenWriter CompressionWriter { get; }

    private CompressionExport(
        MutagenWriter writer,
        ReadOnlyMemorySlice<byte>? original)
    {
        OriginalWriter = writer;
        _original = original;
        CompressionWriter = new MutagenWriter(_tributary, writer.MetaData);
    }

    public static IDisposable Compression(
        bool isCompressed,
        MutagenWriter writer,
        out MutagenWriter writerToUse)
    {
        return Compression(isCompressed, writer, original: null, out writerToUse);
    }

    /// <param name="isCompressed">Whether the record is compressed</param>
    /// <param name="writer">The writer the record is written to</param>
    /// <param name="original">The record's compressed content as read (the uncompressed length, then the zlib stream).
    /// It is written instead of compressing anew when it decompresses to exactly the bytes written.</param>
    /// <param name="writerToUse">The writer to write the record's content to</param>
    internal static IDisposable Compression(
        bool isCompressed,
        MutagenWriter writer,
        ReadOnlyMemorySlice<byte>? original,
        out MutagenWriter writerToUse)
    {
        if (!isCompressed)
        {
            writerToUse = writer;
            return Disposable.Empty;
        }

        var ret = new CompressionExport(writer, original);
        writerToUse = ret.CompressionWriter;
        return ret;
    }

    public void Dispose()
    {
        if (_original is { } original
            && CompressedOriginals.Holds(original, _tributary.GetBuffer().AsSpan(0, checked((int)_tributary.Length))))
        {
            OriginalWriter.BaseStream.Write(original.Span);
            return;
        }
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)_tributary.Length);
        OriginalWriter.BaseStream.Write(b);
        // The Creation Kits write zlib's best compression (header 78 DA); matching it gives back the original bytes of untouched records
        using var stream = new ZLibStream(OriginalWriter.BaseStream, CompressionLevel.SmallestSize, leaveOpen: true);
        _tributary.Position = 0;
        _tributary.CopyTo(stream);
    }
}
