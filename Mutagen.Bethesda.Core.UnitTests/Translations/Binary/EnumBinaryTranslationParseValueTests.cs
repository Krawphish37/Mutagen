using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Translations.Binary;
using Noggog;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Translations.Binary;

public class EnumBinaryTranslationParseValueTests
{
    public enum ByteEnum : byte
    {
        None = 0,
        Value = 3,
    }

    public enum UShortEnum : ushort
    {
        None = 0,
        Value = 3,
    }

    public enum IntEnum
    {
        None = 0,
        Value = 3,
    }

    public enum LongEnum : long
    {
        None = 0,
        Value = 3,
    }

    private static MutagenFrame GetFrame(byte[] bytes)
    {
        return new MutagenFrame(
            new MutagenInterfaceReadStream(
                new BinaryReadStream(new MemoryStream(bytes)),
                new ParsingMeta(GameConstants.SkyrimSE, ModKey.Null, SeparatedMasterPackage.EmptyNull)));
    }

    private static TEnum ParseValue<TEnum>(byte[] bytes)
        where TEnum : struct, Enum, IConvertible
    {
        var frame = GetFrame(bytes);
        var ret = EnumBinaryTranslation<TEnum, MutagenFrame, MutagenWriter>.Instance.ParseValue(frame);
        frame.Complete.ShouldBeTrue();
        return ret;
    }

    [Fact]
    public void ParseValueOneByte()
    {
        ParseValue<ByteEnum>(new byte[] { 3 }).ShouldBe(ByteEnum.Value);
    }

    [Fact]
    public void ParseValueTwoBytes()
    {
        ParseValue<UShortEnum>(new byte[] { 3, 0 }).ShouldBe(UShortEnum.Value);
    }

    [Fact]
    public void ParseValueFourBytes()
    {
        ParseValue<IntEnum>(new byte[] { 3, 0, 0, 0 }).ShouldBe(IntEnum.Value);
    }

    [Fact]
    public void ParseValueEightBytes()
    {
        ParseValue<LongEnum>(new byte[] { 3, 0, 0, 0, 0, 0, 0, 0 }).ShouldBe(LongEnum.Value);
    }

    [Fact]
    public void ParseValueUndefinedValue()
    {
        ((int)ParseValue<IntEnum>(new byte[] { 7, 0, 0, 0 })).ShouldBe(7);
    }

    [Fact]
    public void ParseValueMatchesParse()
    {
        var bytes = new byte[] { 3, 0, 0, 0 };
        var parsed = EnumBinaryTranslation<IntEnum, MutagenFrame, MutagenWriter>.Instance.Parse(GetFrame(bytes), 4);
        ParseValue<IntEnum>(bytes).ShouldBe(parsed);
    }

    [Fact]
    public void ParseValueUnsupportedLengthThrows()
    {
        Assert.Throws<NotImplementedException>(() => ParseValue<IntEnum>(new byte[] { 3, 0, 0 }));
    }
}
