using System.Text;
using ICSharpCode.SharpZipLib.Zip.Compression;
using K4os.Compression.LZ4;
using Mutagen.Bethesda.Archives.Ba2;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Archives;

/// <summary>
/// C11: Starfield archives (BA2 versions 2 and 3).  Their header has two more uints after the name table offset
/// (1 and 0 in every archive in the Data folder) and, in version 3, the compression method (3 = LZ4 blocks, used by
/// Starfield's own texture archives).  The file and texture records are Fallout 4's: 36 bytes per file record, and
/// 24 bytes per texture record plus 24 per chunk ending in 0xBAADF00D.  The reader skipped only one header uint and
/// read texture records with guessed fields, so a texture's size and contents were garbage (ar99_reflex_color.dds in
/// aa50_longwatch - textures.ba2 reported 1.5 GB), and LZ4 chunks were inflated as zlib.
/// </summary>
public class Ba2StarfieldTests
{
    private static readonly byte[] Mip0 = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly byte[] Mip1 = [9, 10, 11, 12, 13, 14, 15, 16];

    private static byte[] Zlib(byte[] data)
    {
        var deflater = new Deflater();
        deflater.SetInput(data);
        deflater.Finish();
        var buffer = new byte[256];
        var length = deflater.Deflate(buffer);
        return buffer[..length];
    }

    private static byte[] Lz4(byte[] data)
    {
        var buffer = new byte[LZ4Codec.MaximumOutputSize(data.Length)];
        var length = LZ4Codec.Encode(data, buffer);
        return buffer[..length];
    }

    private static void WriteHeader(BinaryWriter writer, uint version, string type, uint files, long nameTable, uint compression)
    {
        writer.Write("BTDX"u8);
        writer.Write(version);
        writer.Write(Encoding.ASCII.GetBytes(type));
        writer.Write(files);
        writer.Write((ulong)nameTable);
        writer.Write(1u);
        writer.Write(0u);
        if (version == 3)
        {
            writer.Write(compression);
        }
    }

    private static int HeaderLength(uint version) => version == 3 ? 36 : 32;

    /// <summary>
    /// A BC1 4x4 texture with two mips: the first stored as is, the second compressed (zlib, or LZ4 when version 3
    /// names compression 3).
    /// </summary>
    private static byte[] BuildTextureBa2(uint version, uint compression)
    {
        var packed = version == 3 && compression == 3 ? Lz4(Mip1) : Zlib(Mip1);
        var chunksStart = HeaderLength(version) + 24 + 2 * 24;
        var nameTable = chunksStart + Mip0.Length + packed.Length;

        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        WriteHeader(writer, version, "DX10", 1, nameTable, compression);
        writer.Write(0x1234u); // name hash
        writer.Write("dds\0"u8);
        writer.Write(0x5678u); // directory hash
        writer.Write((byte)0);
        writer.Write((byte)2); // chunks
        writer.Write((ushort)24); // chunk header length
        writer.Write((ushort)4); // height
        writer.Write((ushort)4); // width
        writer.Write((byte)2); // mips
        writer.Write((byte)71); // DXGI_FORMAT_BC1_UNORM
        writer.Write((byte)0); // cube map
        writer.Write((byte)8); // tile mode
        writer.Write((ulong)chunksStart);
        writer.Write(0u); // packed size: 0 means stored as is
        writer.Write((uint)Mip0.Length);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(0xBAADF00Du);
        writer.Write((ulong)(chunksStart + Mip0.Length));
        writer.Write((uint)packed.Length);
        writer.Write((uint)Mip1.Length);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(0xBAADF00Du);
        writer.Write(Mip0);
        writer.Write(packed);
        var name = "textures/test.dds"u8.ToArray();
        writer.Write((short)name.Length);
        writer.Write(name);
        writer.Flush();
        return stream.ToArray();
    }

    [Theory]
    [InlineData(2u, 0u)] // Starfield DLC and mod texture archives
    [InlineData(3u, 0u)]
    [InlineData(3u, 3u)] // Starfield - Textures01.ba2 and the other base game texture archives: LZ4
    public void Texture_SizeIsWhatGetBytesReturns(uint version, uint compression)
    {
        var archive = new Ba2Reader(() => new MemoryStream(BuildTextureBa2(version, compression)));
        archive.Version.ShouldBe(version);
        var file = archive.Files.ShouldHaveSingleItem();
        file.Path.ShouldBe("textures/test.dds");

        var bytes = file.GetBytes();

        // DDS magic and header (124 bytes, BC1 needs no DX10 header), then both mips unpacked
        file.Size.ShouldBe(4u + 124u + 16u);
        bytes.Length.ShouldBe((int)file.Size);
        bytes[..4].ShouldBe("DDS "u8.ToArray());
        bytes[^16..].ShouldBe(Mip0.Concat(Mip1).ToArray());
        using var stream = file.AsStream();
        stream.Length.ShouldBe(file.Size);
    }

    [Theory]
    [InlineData(2u, false)]
    [InlineData(2u, true)]
    public void General_ReadsStarfieldRecords(uint version, bool compressed)
    {
        byte[] content = [1, 2, 3, 4, 5, 6, 7, 8, 9];
        var stored = compressed ? Zlib(content) : content;
        var dataStart = HeaderLength(version) + 36;
        var nameTable = dataStart + stored.Length;

        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        WriteHeader(writer, version, "GNRL", 1, nameTable, 0);
        writer.Write(0x1234u); // name hash
        writer.Write("bin\0"u8);
        writer.Write(0x5678u); // directory hash
        writer.Write(0u); // flags
        writer.Write((ulong)dataStart);
        writer.Write(compressed ? (uint)stored.Length : 0u);
        writer.Write((uint)content.Length);
        writer.Write(0xBAADF00Du);
        writer.Write(stored);
        var name = "misc/test.bin"u8.ToArray();
        writer.Write((short)name.Length);
        writer.Write(name);
        writer.Flush();

        var archive = new Ba2Reader(() => new MemoryStream(stream.ToArray()));
        var file = archive.Files.ShouldHaveSingleItem();
        file.Path.ShouldBe("misc/test.bin");
        file.Size.ShouldBe((uint)content.Length);
        file.GetBytes().ShouldBe(content);
    }
}
