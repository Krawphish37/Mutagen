using System.Buffers.Binary;
using System.Drawing;
using Shouldly;
using Mutagen.Bethesda.Binary;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Meta;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Translations.Binary;

public class ColorBinaryTranslationTests
{
    private static byte[] GetFloatBytes(params float[] floats)
    {
        var bytes = new byte[floats.Length * 4];
        for (int i = 0; i < floats.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), floats[i]);
        }
        return bytes;
    }

    private static float[] WriteFloats(Color color, ColorBinaryType binaryType)
    {
        var memStream = new MemoryStream();
        using (var writer = new MutagenWriter(memStream, GameConstants.Starfield, dispose: false))
        {
            writer.Write(color, binaryType);
        }
        var bytes = memStream.ToArray();
        var ret = new float[bytes.Length / 4];
        for (int i = 0; i < ret.Length; i++)
        {
            ret[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4));
        }
        return ret;
    }

    [Theory]
    [InlineData(-0.5f, 0)]
    [InlineData(0f, 0)]
    [InlineData(0.5f, 128)]
    [InlineData(0.9f, 230)]
    [InlineData(1f, 255)]
    public void GetColorByteInRange(float f, byte expected)
    {
        IBinaryStreamExt.GetColorByte(f).ShouldBe(expected);
    }

    [Theory]
    [InlineData(1.0001f)]
    [InlineData(1.2f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    [InlineData(10f)]
    [InlineData(200f)]
    [InlineData(254.9f)]
    [InlineData(255f)]
    [InlineData(300f)]
    public void GetColorByteAboveOneClamps(float f)
    {
        IBinaryStreamExt.GetColorByte(f).ShouldBe(byte.MaxValue);
    }

    [Fact]
    public void ReadNoAlphaFloatAboveOneClamps()
    {
        ReadOnlySpan<byte> bytes = GetFloatBytes(1.5f, 2f, 0.2f);
        var color = bytes.ReadColor(ColorBinaryType.NoAlphaFloat);
        color.R.ShouldBe(byte.MaxValue);
        color.G.ShouldBe(byte.MaxValue);
        color.B.ShouldBe((byte)51);
    }

    [Fact]
    public void ReadAlphaFloatAboveOneClamps()
    {
        ReadOnlySpan<byte> bytes = GetFloatBytes(0.2f, 1.2f, 10f, 1.5f);
        var color = bytes.ReadColor(ColorBinaryType.AlphaFloat);
        color.R.ShouldBe((byte)51);
        color.G.ShouldBe(byte.MaxValue);
        color.B.ShouldBe(byte.MaxValue);
        color.A.ShouldBe(byte.MaxValue);
    }

    [Fact]
    public void NoAlphaFloatAboveOneWritesBackAsOne()
    {
        ReadOnlySpan<byte> bytes = GetFloatBytes(1.5f, 2f, 1.2f);
        var color = bytes.ReadColor(ColorBinaryType.NoAlphaFloat);
        WriteFloats(color, ColorBinaryType.NoAlphaFloat).ShouldBe(new[] { 1f, 1f, 1f });
    }
}
