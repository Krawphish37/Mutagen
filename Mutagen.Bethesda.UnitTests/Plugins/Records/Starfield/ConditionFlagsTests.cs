using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

/// <summary>
/// The CTDA flag bits ParametersUseAliases (0x02) and ParametersUsePackData (0x08) are surfaced on
/// <see cref="IConditionDataGetter"/> rather than on <see cref="Condition.Flags"/>.  The direct import
/// strips both from Flags; the overlay must do the same, or a copy of an overlay condition keeps the
/// raw bit and can no longer have UsePackageData turned off.
/// </summary>
public class ConditionFlagsTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Starfield, TestModKey, masters);
    }

    private static Condition MakeCondition(bool useAliases, bool usePackageData)
    {
        return new ConditionFloat
        {
            Flags = Condition.Flag.OR,
            ComparisonValue = 1,
            Data = new GetIsIDConditionData
            {
                UseAliases = useAliases,
                UsePackageData = usePackageData,
            },
        };
    }

    private static byte[] Write(IConditionGetter condition)
    {
        var masters = new MasterReferenceCollection(TestModKey);
        var bundle = new WritingBundle(GameConstants.Starfield)
        {
            MasterReferences = masters,
            SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(masters),
        };
        var memStream = new MemoryStream();
        using (var writer = new MutagenWriter(memStream, bundle, dispose: false))
        {
            condition.WriteToBinary(writer);
        }
        return memStream.ToArray();
    }

    private static IConditionGetter ReadDirect(byte[] bytes)
    {
        return Condition.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())),
            default);
    }

    private static IConditionGetter ReadOverlay(byte[] bytes)
    {
        var meta = Meta();
        return ConditionBinaryOverlay.ConditionFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    private static byte FlagBits(byte[] bytes) => (byte)(bytes[6] & ConditionBinaryCreateTranslation.FlagMask);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ParameterBits_StrippedFromFlags_DirectAndOverlay(bool useAliases, bool usePackageData)
    {
        var bytes = Write(MakeCondition(useAliases, usePackageData));

        var direct = ReadDirect(bytes);
        var overlay = ReadOverlay(bytes);

        direct.Flags.ShouldBe(Condition.Flag.OR);
        overlay.Flags.ShouldBe(Condition.Flag.OR);
        overlay.Data.UseAliases.ShouldBe(useAliases);
        overlay.Data.UsePackageData.ShouldBe(usePackageData);
        direct.Equals(overlay).ShouldBeTrue();
        Write(overlay).ShouldBe(bytes);
    }

    [Fact]
    public void OverlayCopy_CanClearUsePackageData()
    {
        var bytes = Write(MakeCondition(useAliases: false, usePackageData: true));
        FlagBits(bytes).ShouldBe((byte)0x09);

        var copy = ReadOverlay(bytes).DeepCopy();
        copy.Data.UsePackageData = false;

        var written = Write(copy);
        FlagBits(written).ShouldBe((byte)Condition.Flag.OR);
        ReadDirect(written).Data.UsePackageData.ShouldBeFalse();
    }
}
