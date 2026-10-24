using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Masters;

/// <summary>
/// With separated masters, a Full FormID's top byte is the Full index, and 0xFD / 0xFE in that byte
/// mark Medium / Small FormIDs.  Full indices must stay at or below 0xFC.
/// </summary>
public class SeparatedMasterIndexLimitTests
{
    private static readonly ModKey Current = ModKey.FromFileName("Patch.esm");

    private static ModKey Key(MasterStyle style, int i) => ModKey.FromFileName($"{style}{i}.esm");

    private static IReadOnlySeparatedMasterPackage Package(int full, int medium = 0, int small = 0, MasterStyle currentStyle = MasterStyle.Full)
    {
        var keys = new List<ModKey>();
        var loadOrder = new LoadOrder<IModFlagsGetter>();
        foreach (var (style, count) in new[] { (MasterStyle.Full, full), (MasterStyle.Medium, medium), (MasterStyle.Small, small) })
        {
            for (int i = 0; i < count; i++)
            {
                var key = Key(style, i);
                keys.Add(key);
                loadOrder.Add(MastersTestUtil.GetFlags(key, style));
            }
        }
        loadOrder.Add(MastersTestUtil.GetFlags(Current, currentStyle));

        // CreateUnsafe, as MasterReferenceCollection caps the total master count by itself
        var masters = MasterReferenceCollection.CreateUnsafe(
            Current,
            keys.Select(k => new MasterReference { Master = k }));

        return SeparatedMasterPackage.Separate(Current, currentStyle, masters, loadOrder);
    }

    [Fact]
    public void LastFullIndexBelowMediumMarker()
    {
        var package = Package(full: 252);

        package.GetFormID(new FormKey(Key(MasterStyle.Full, 251), 0x800)).ShouldBe(new FormID(0xFB000800));
        package.GetFormID(new FormKey(Current, 0x012345)).ShouldBe(new FormID(0xFC012345));
        package.GetFormKey(new FormID(0xFC012345), reference: true).ShouldBe(new FormKey(Current, 0x012345));
    }

    [Theory]
    [InlineData(MasterStyle.Full)]
    [InlineData(MasterStyle.Medium)]
    [InlineData(MasterStyle.Small)]
    public void OwnIndexReachingMediumMarkerThrowsOnlyForOwnFormKeys(MasterStyle currentStyle)
    {
        // 253 Full masters take Full indices 0x00-0xFC, which leaves no Full index for the mod's own records
        var package = Package(full: 253, currentStyle: currentStyle);

        package.GetFormID(new FormKey(Key(MasterStyle.Full, 252), 0x800)).ShouldBe(new FormID(0xFC000800));
        package.GetFormKey(new FormID(0xFC000800), reference: true).ShouldBe(new FormKey(Key(MasterStyle.Full, 252), 0x800));
        var ex = Should.Throw<TooManyMastersException>(() => package.GetFormID(new FormKey(Current, 0x012345)));
        ex.CurrentMod.ShouldBe(Current);
        ex.Masters.Count.ShouldBe(253);
        Should.Throw<TooManyMastersException>(() => ((SeparatedMasterPackage)package).CheckOwnIndexFits());
    }

    [Fact]
    public void OwnIndexBelowMediumMarkerPassesCheck()
    {
        ((SeparatedMasterPackage)Package(full: 252)).CheckOwnIndexFits();
    }

    [Theory]
    [InlineData(254)]
    [InlineData(255)]
    public void FullMastersReachingMediumMarkerThrow(int full)
    {
        var ex = Should.Throw<TooManyMastersException>(() => Package(full: full));
        ex.CurrentMod.ShouldBe(Current);
        ex.Masters.Count.ShouldBe(full);
    }

    [Fact]
    public void MediumAndSmallDoNotTakeFullIndices()
    {
        var package = Package(full: 30, medium: 25, small: 199);

        package.GetFormID(new FormKey(Current, 0x012345)).ShouldBe(new FormID(0x1E012345));
        package.GetFormID(new FormKey(Key(MasterStyle.Medium, 24), 0x0800)).ShouldBe(new FormID(0xFD180800));
        package.GetFormID(new FormKey(Key(MasterStyle.Small, 198), 0x800)).ShouldBe(new FormID(0xFE0C6800));
    }

    [Fact]
    public void MediumIndexPastByteThrows()
    {
        Package(full: 0, medium: 256)
            .GetFormID(new FormKey(Key(MasterStyle.Medium, 255), 0x0800))
            .ShouldBe(new FormID(0xFDFF0800));
        Should.Throw<TooManyMastersException>(() => Package(full: 0, medium: 257));
    }

    [Fact]
    public void SmallIndexPastTwelveBitsThrows()
    {
        Package(full: 0, small: 4096)
            .GetFormID(new FormKey(Key(MasterStyle.Small, 4095), 0x800))
            .ShouldBe(new FormID(0xFEFFF800));
        Should.Throw<TooManyMastersException>(() => Package(full: 0, small: 4097));
    }
}
