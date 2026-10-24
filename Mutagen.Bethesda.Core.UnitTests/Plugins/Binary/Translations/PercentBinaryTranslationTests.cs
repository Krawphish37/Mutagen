using System.Buffers.Binary;
using Shouldly;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Translations.Binary;
using Noggog;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary.Translations;

// Percents stored as integers used to be written with a truncating cast, so a value
// such as 29 / 100.0 (which is 28.999999999999996 once scaled back up) came out as 28.
public class PercentBinaryTranslationTests
{
    private static byte[] GetWriteArray(Percent item, FloatIntegerType integerType)
    {
        var memStream = new MemoryStream();
        using (var writer = new MutagenWriter(memStream, GameConstants.SkyrimSE))
        {
            PercentBinaryTranslation.Write(writer, item, integerType);
        }
        return memStream.ToArray();
    }

    [Fact]
    public void ByteHundredRoundTripsEveryValue()
    {
        for (int raw = 0; raw <= 100; raw++)
        {
            var percent = PercentBinaryTranslation.GetPercent(new[] { (byte)raw }, FloatIntegerType.ByteHundred);
            GetWriteArray(percent, FloatIntegerType.ByteHundred).ShouldBe(new[] { (byte)raw }, $"raw {raw}");
        }
    }

    [Fact]
    public void ByteRoundTripsEveryValue()
    {
        for (int raw = 0; raw <= byte.MaxValue; raw++)
        {
            var percent = PercentBinaryTranslation.GetPercent(new[] { (byte)raw }, FloatIntegerType.Byte);
            GetWriteArray(percent, FloatIntegerType.Byte).ShouldBe(new[] { (byte)raw }, $"raw {raw}");
        }
    }

    [Fact]
    public void UShortRoundTripsEveryValue()
    {
        var bytes = new byte[2];
        for (int raw = 0; raw <= ushort.MaxValue; raw++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)raw);
            var percent = PercentBinaryTranslation.GetPercent(bytes, FloatIntegerType.UShort);
            var written = GetWriteArray(percent, FloatIntegerType.UShort);
            BinaryPrimitives.ReadUInt16LittleEndian(written).ShouldBe((ushort)raw);
        }
    }

    [Theory]
    [InlineData(0.29, 29)]
    [InlineData(0.57, 57)]
    [InlineData(0.58, 58)]
    [InlineData(0.5, 50)]
    [InlineData(1.0, 100)]
    public void ByteHundredWritesNearestValue(double value, byte expected)
    {
        GetWriteArray(new Percent(value), FloatIntegerType.ByteHundred).ShouldBe(new[] { expected });
    }

    [Theory]
    [InlineData(0.29, 74)]
    [InlineData(1.0, 255)]
    public void ByteWritesNearestValue(double value, byte expected)
    {
        GetWriteArray(new Percent(value), FloatIntegerType.Byte).ShouldBe(new[] { expected });
    }
}
