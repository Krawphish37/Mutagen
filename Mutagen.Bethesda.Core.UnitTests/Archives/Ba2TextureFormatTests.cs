using System.Buffers.Binary;
using Mutagen.Bethesda.Archives.Ba2;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Archives;

/// <summary>
/// C202: a texture whose DXGI format has no legacy DDS description threw "Unsupported DDS header format" on GetBytes and
/// AsStream: 78 of Starfield's 138,678 official textures (R8G8B8A8_SNORM 63, e.g. blemish_veinsmany_normal.dds;
/// R16G16B16A16_FLOAT 13, e.g. textures/arealight/ltc_mat.dds; R16G16B16A16_UNORM 2). They are now written with the DX10
/// extended header, which names the format; the formats Mutagen already wrote are unchanged.
/// </summary>
public class Ba2TextureFormatTests
{
    /// <summary>A Starfield (version 2) texture archive: one 4x4 texture of the given format, one stored mip</summary>
    private static byte[] BuildTexture(byte format, byte[] mip)
    {
        const int headerLength = 32;
        var chunkStart = headerLength + 24 + 24;
        var nameTable = chunkStart + mip.Length;
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write("BTDX"u8);
        writer.Write(2u);
        writer.Write("DX10"u8);
        writer.Write(1u);
        writer.Write((ulong)nameTable);
        writer.Write(1u);
        writer.Write(0u);
        writer.Write(0x1234u); // name hash
        writer.Write("dds\0"u8);
        writer.Write(0x5678u); // directory hash
        writer.Write((byte)0);
        writer.Write((byte)1); // chunks
        writer.Write((ushort)24); // chunk header length
        writer.Write((ushort)4); // height
        writer.Write((ushort)4); // width
        writer.Write((byte)1); // mips
        writer.Write(format);
        writer.Write((byte)0); // cube map
        writer.Write((byte)8); // tile mode
        writer.Write((ulong)chunkStart);
        writer.Write(0u); // stored as is
        writer.Write((uint)mip.Length);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(0xBAADF00Du);
        writer.Write(mip);
        var name = "textures/test.dds"u8.ToArray();
        writer.Write((short)name.Length);
        writer.Write(name);
        writer.Flush();
        return stream.ToArray();
    }

    [Theory]
    [InlineData((byte)31, 4)] // R8G8B8A8_SNORM
    [InlineData((byte)10, 8)] // R16G16B16A16_FLOAT
    [InlineData((byte)11, 8)] // R16G16B16A16_UNORM
    [InlineData((byte)2, 16)] // R32G32B32A32_FLOAT
    public void FormatWithoutLegacyHeader_GetsTheDx10Header(byte format, int bytesPerPixel)
    {
        var mip = Enumerable.Range(0, 16 * bytesPerPixel).Select(i => (byte)i).ToArray();
        var file = new Ba2Reader(() => new MemoryStream(BuildTexture(format, mip))).Files.ShouldHaveSingleItem();

        var bytes = file.GetBytes();
        // "DDS ", the 124-byte header, the 20-byte DX10 header, the texture
        file.Size.ShouldBe((uint)(4 + 124 + 20 + mip.Length));
        bytes.Length.ShouldBe((int)file.Size);
        bytes[..4].ShouldBe("DDS "u8.ToArray());
        bytes.AsSpan(84, 4).ToArray().ShouldBe("DX10"u8.ToArray()); // pixel format FourCC
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20)).ShouldBe((uint)(16 * bytesPerPixel)); // linear size
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(128)).ShouldBe((uint)format); // dxgiFormat
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(132)).ShouldBe(3u); // TEXTURE2D
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(140)).ShouldBe(1u); // array size
        bytes[148..].ShouldBe(mip);

        using var stream = file.AsStream();
        var read = new MemoryStream();
        stream.CopyTo(read);
        read.ToArray().ShouldBe(bytes);
    }

    [Theory]
    [InlineData((byte)71, 124, "DXT1")] // BC1_UNORM: legacy header, as before
    [InlineData((byte)28, 124, null)] // R8G8B8A8_UNORM: bit masks, as before
    [InlineData((byte)98, 144, "DX10")] // BC7_UNORM: DX10 header, as before
    public void KnownFormats_AreWrittenAsBefore(byte format, int headerBytes, string? fourCC)
    {
        var mip = new byte[format == 28 ? 64 : 16];
        var file = new Ba2Reader(() => new MemoryStream(BuildTexture(format, mip))).Files.ShouldHaveSingleItem();
        var bytes = file.GetBytes();
        bytes.Length.ShouldBe(4 + headerBytes + mip.Length);
        if (fourCC != null) bytes.AsSpan(84, 4).ToArray().ShouldBe(System.Text.Encoding.ASCII.GetBytes(fourCC));
    }
}
