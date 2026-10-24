using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Translations.Binary;
using Noggog;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Translations.Binary;

public class EnumBinaryTranslationTests
{
    [Flags]
    public enum IntFlag
    {
        Low = 0x0001,
        High = 0x8000,
    }

    public enum IntEnum
    {
        Low = 1,
        High = 0x8000,
    }

    public enum UShortEnum : ushort
    {
        Low = 1,
        High = 0x8000,
    }

    private static MutagenFrame GetFrame(byte[] bytes)
    {
        return new MutagenFrame(
            new MutagenInterfaceReadStream(
                new BinaryReadStream(new MemoryStream(bytes)),
                new ParsingMeta(GameConstants.SkyrimSE, ModKey.Null, SeparatedMasterPackage.EmptyNull)));
    }

    [Fact]
    public void ParseTwoBytesHighBitNotSignExtended()
    {
        var item = EnumBinaryTranslation<IntFlag, MutagenFrame, MutagenWriter>.Instance.Parse(
            GetFrame(new byte[] { 0xFC, 0xFE }),
            2);
        ((int)item).ShouldBe(0xFEFC);
        item.HasFlag(IntFlag.High).ShouldBeTrue();
    }

    [Fact]
    public void ParseTwoBytesNamedValueAboveInt16Max()
    {
        EnumBinaryTranslation<IntEnum, MutagenFrame, MutagenWriter>.Instance.Parse(
                GetFrame(new byte[] { 0x00, 0x80 }),
                2)
            .ShouldBe(IntEnum.High);
    }

    [Fact]
    public void ParseTwoBytesUShortBacked()
    {
        EnumBinaryTranslation<UShortEnum, MutagenFrame, MutagenWriter>.Instance.Parse(
                GetFrame(new byte[] { 0x00, 0x80 }),
                2)
            .ShouldBe(UShortEnum.High);
    }

    [Fact]
    public void ParseNullableTwoBytesHighBitNotSignExtended()
    {
        EnumBinaryTranslation<IntFlag, MutagenFrame, MutagenWriter>.Instance.Parse(
            GetFrame(new byte[] { 0xFC, 0xFE }),
            out IntFlag? item);
        item.ShouldBe((IntFlag)0xFEFC);
    }

    [Fact]
    public void WriteThenParseTwoBytesRoundTrips()
    {
        var memStream = new MemoryStream();
        using (var writer = new MutagenWriter(memStream, GameConstants.SkyrimSE, dispose: false))
        {
            EnumBinaryTranslation<IntFlag, MutagenFrame, MutagenWriter>.Instance.Write(
                writer,
                (IntFlag)0xFEFC,
                2);
        }
        var bytes = memStream.ToArray();
        bytes.ShouldBe(new byte[] { 0xFC, 0xFE });
        EnumBinaryTranslation<IntFlag, MutagenFrame, MutagenWriter>.Instance.Parse(GetFrame(bytes), 2)
            .ShouldBe((IntFlag)0xFEFC);
    }
}
