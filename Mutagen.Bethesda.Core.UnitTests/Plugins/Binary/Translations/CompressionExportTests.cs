using System.Buffers.Binary;
using System.IO.Compression;
using Shouldly;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Meta;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary.Translations;

public class CompressionExportTests
{
    private static readonly byte[] Payload = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

    [Fact]
    public void NotCompressedPassesWriterThrough()
    {
        using var stream = new MemoryStream();
        using var writer = new MutagenWriter(stream, GameConstants.SkyrimSE, dispose: false);
        using (CompressionExport.Compression(false, writer, out var writerToUse))
        {
            writerToUse.ShouldBeSameAs(writer);
        }
        stream.CanWrite.ShouldBeTrue();
    }

    [Fact]
    public void CompressedLeavesOriginalStreamOpen()
    {
        using var stream = new MemoryStream();
        using var writer = new MutagenWriter(stream, GameConstants.SkyrimSE, dispose: false);
        using (CompressionExport.Compression(true, writer, out var writerToUse))
        {
            writerToUse.Write(Payload);
        }
        stream.CanWrite.ShouldBeTrue();
        stream.CanSeek.ShouldBeTrue();
        writer.Write(0xABCD);
        stream.Position.ShouldBe(stream.Length);
    }

    [Fact]
    public void CompressedWritesLengthAndZlibData()
    {
        using var stream = new MemoryStream();
        using (var writer = new MutagenWriter(stream, GameConstants.SkyrimSE, dispose: false))
        {
            using (CompressionExport.Compression(true, writer, out var writerToUse))
            {
                writerToUse.Write(Payload);
            }
        }
        var bytes = stream.ToArray();
        BinaryPrimitives.ReadUInt32LittleEndian(bytes).ShouldBe((uint)Payload.Length);
        using var zlib = new ZLibStream(new MemoryStream(bytes, 4, bytes.Length - 4), CompressionMode.Decompress);
        using var decompressed = new MemoryStream();
        zlib.CopyTo(decompressed);
        decompressed.ToArray().ShouldBe(Payload);
    }

    [Fact]
    public void CompressedTwiceToSameWriter()
    {
        using var stream = new MemoryStream();
        using var writer = new MutagenWriter(stream, GameConstants.SkyrimSE, dispose: false);
        using (CompressionExport.Compression(true, writer, out var writerToUse))
        {
            writerToUse.Write(Payload);
        }
        var firstEnd = stream.Position;
        using (CompressionExport.Compression(true, writer, out var writerToUse))
        {
            writerToUse.Write(Payload);
        }
        stream.Position.ShouldBeGreaterThan(firstEnd);
        stream.CanWrite.ShouldBeTrue();
    }
}
