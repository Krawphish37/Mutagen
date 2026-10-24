using System.Buffers.Binary;
using System.IO.Compression;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Meta;
using Shouldly;
using Xunit;
using MemoryStream = System.IO.MemoryStream;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary.Translations;

/// <summary>
/// CompressionExport and ExtraLengthHeaderExport buffer their content before writing it out.
/// One is made per compressed record, and per subrecord written with an overflow record type.
/// </summary>
public class ScratchBufferExportTests
{
    private static readonly RecordType Main = new("XTV2");
    private static readonly RecordType Overflow = new("XXXX");

    // Small enough that a 64 KB scratch block per use fails it
    private const long MaxBytesAllocatedPerUse = 16 * 1024;

    private class NonClosingMemoryStream : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
        }
    }

    private static byte[] Payload(int length)
    {
        var ret = new byte[length];
        for (int i = 0; i < ret.Length; i++)
        {
            ret[i] = (byte)(i * 7);
        }
        return ret;
    }

    private static long BytesAllocatedPerUse(Action use)
    {
        const int Count = 100;
        for (int i = 0; i < 10; i++)
        {
            use();
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Count; i++)
        {
            use();
        }
        return (GC.GetAllocatedBytesForCurrentThread() - before) / Count;
    }

    private static byte[] Compress(byte[] content)
    {
        var stream = new NonClosingMemoryStream();
        var writer = new MutagenWriter(stream, GameConstants.SkyrimSE);
        using (CompressionExport.Compression(true, writer, out var writerToUse))
        {
            writerToUse.Write(content);
        }
        return stream.ToArray();
    }

    private static byte[] Decompress(byte[] compressed)
    {
        using var zlib = new ZLibStream(new MemoryStream(compressed, 4, compressed.Length - 4), CompressionMode.Decompress);
        using var ret = new MemoryStream();
        zlib.CopyTo(ret);
        return ret.ToArray();
    }

    private static byte[] WriteWithOverflow(byte[] content)
    {
        var stream = new MemoryStream();
        using var writer = new MutagenWriter(stream, GameConstants.SkyrimSE);
        using (HeaderExport.Subrecord(writer, Main, Overflow, out var writerToUse))
        {
            writerToUse.Write(content);
        }
        return stream.ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(200_000)]
    public void CompressionRoundTrips(int length)
    {
        var content = Payload(length);
        var compressed = Compress(content);
        BinaryPrimitives.ReadUInt32LittleEndian(compressed).ShouldBe((uint)length);
        Decompress(compressed).ShouldBe(content);
    }

    [Fact]
    public void CompressionSmallRecordDoesNotAllocateLargeScratchBuffer()
    {
        var content = Payload(100);
        var stream = new NonClosingMemoryStream();
        var writer = new MutagenWriter(stream, GameConstants.SkyrimSE);
        BytesAllocatedPerUse(() =>
            {
                stream.Position = 0;
                using (CompressionExport.Compression(true, writer, out var writerToUse))
                {
                    writerToUse.Write(content);
                }
            })
            .ShouldBeLessThan(MaxBytesAllocatedPerUse);
    }

    [Fact]
    public void OverflowCapableSubrecordWithinLimit()
    {
        var content = Payload(16);
        var bytes = WriteWithOverflow(content);
        bytes.Length.ShouldBe(6 + 16);
        BinaryPrimitives.ReadInt32LittleEndian(bytes).ShouldBe(Main.TypeInt);
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)).ShouldBe((ushort)16);
        bytes.AsSpan(6).ToArray().ShouldBe(content);
    }

    [Fact]
    public void OverflowCapableSubrecordAtLimit()
    {
        var content = Payload(ushort.MaxValue);
        var bytes = WriteWithOverflow(content);
        bytes.Length.ShouldBe(6 + ushort.MaxValue);
        BinaryPrimitives.ReadInt32LittleEndian(bytes).ShouldBe(Main.TypeInt);
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)).ShouldBe(ushort.MaxValue);
        bytes.AsSpan(6).ToArray().ShouldBe(content);
    }

    [Fact]
    public void OverflowCapableSubrecordOverLimit()
    {
        var content = Payload(ushort.MaxValue + 1000);
        var bytes = WriteWithOverflow(content);
        bytes.Length.ShouldBe(10 + 6 + content.Length);
        BinaryPrimitives.ReadInt32LittleEndian(bytes).ShouldBe(Overflow.TypeInt);
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)).ShouldBe((ushort)4);
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(6)).ShouldBe((uint)content.Length);
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10)).ShouldBe(Main.TypeInt);
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14)).ShouldBe((ushort)0);
        bytes.AsSpan(16).ToArray().ShouldBe(content);
    }

    [Fact]
    public void OverflowCapableSubrecordDoesNotAllocateLargeScratchBuffer()
    {
        var content = Payload(16);
        var stream = new MemoryStream();
        using var writer = new MutagenWriter(stream, GameConstants.SkyrimSE);
        BytesAllocatedPerUse(() =>
            {
                stream.Position = 0;
                using (HeaderExport.Subrecord(writer, Main, Overflow, out var writerToUse))
                {
                    writerToUse.Write(content);
                }
            })
            .ShouldBeLessThan(MaxBytesAllocatedPerUse);
    }
}
