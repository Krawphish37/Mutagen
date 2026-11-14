using System.Text;
using ICSharpCode.SharpZipLib.Zip.Compression;
using Mutagen.Bethesda.Archives.Ba2;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Archives;

/// <summary>
/// C193: a zlib-compressed BA2 entry read through AsStream() threw "count cannot be negative" on any read but one of
/// exactly its size (54,906 of 129,599 Starfield meshes). The stream was a FramedStream over SharpZipLib's
/// InflaterInputStream, whose position is the archive file's: once the inflater had read ahead past the entry's unpacked
/// size, the frame asked it for a negative count. An entry followed by enough of the file shows it.
/// </summary>
public class Ba2CompressedStreamTests
{
    private static byte[] Zlib(byte[] data)
    {
        var deflater = new Deflater();
        deflater.SetInput(data);
        deflater.Finish();
        var buffer = new byte[data.Length + 256];
        var length = deflater.Deflate(buffer);
        return buffer[..length];
    }

    /// <summary>One compressed GNRL entry, then 8 KB of other data (as the next entries of a real archive), then the
    /// name table</summary>
    private static byte[] BuildArchive(uint version, byte[] content)
    {
        var packed = Zlib(content);
        var headerLength = version == 1 ? 24 : 32;
        var dataStart = headerLength + 36;
        var filler = new byte[8192];
        new Random(1).NextBytes(filler);
        var nameTable = dataStart + packed.Length + filler.Length;

        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write("BTDX"u8);
        writer.Write(version);
        writer.Write("GNRL"u8);
        writer.Write(1u);
        writer.Write((ulong)nameTable);
        if (version != 1)
        {
            writer.Write(1u);
            writer.Write(0u);
        }
        writer.Write(0x1234u); // name hash
        writer.Write("nif\0"u8);
        writer.Write(0x5678u); // directory hash
        writer.Write(0u); // flags
        writer.Write((ulong)dataStart);
        writer.Write((uint)packed.Length);
        writer.Write((uint)content.Length);
        writer.Write(0xBAADF00Du);
        writer.Write(packed);
        writer.Write(filler);
        var name = "meshes/test.nif"u8.ToArray();
        writer.Write((short)name.Length);
        writer.Write(name);
        writer.Flush();
        return stream.ToArray();
    }

    [Theory]
    [InlineData(1u)] // Fallout 4
    [InlineData(2u)] // Starfield
    public void AsStream_ReadInPieces_EqualsTheContent(uint version)
    {
        var content = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("BSTriShape NiNode bhkNPCollisionObject ", 25)));
        var bytes = BuildArchive(version, content);
        var file = new Ba2Reader(() => new MemoryStream(bytes)).Files.ShouldHaveSingleItem();
        file.Size.ShouldBe((uint)content.Length);

        foreach (var piece in new[] { 1, 100, 4096, content.Length + 1 })
        {
            using var stream = file.AsStream();
            stream.Length.ShouldBe(content.Length);
            var read = new MemoryStream();
            var buffer = new byte[piece];
            int n;
            while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                read.Write(buffer, 0, n);
                stream.Position.ShouldBe(read.Length);
            }
            read.ToArray().ShouldBe(content, $"pieces of {piece}");
        }

        using (var buffered = new BufferedStream(file.AsStream(), 1 << 20))
        {
            var read = new MemoryStream();
            buffered.CopyTo(read);
            read.ToArray().ShouldBe(content);
        }
        file.GetBytes().ShouldBe(content);
    }
}
