using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A Starfield leveled list entry's count (LVLO) is unsigned (xEdit's Starfield definitions): Starfield.esm's vendor
// credit lists store 65535, 40000 and 62500, and a quest reward 33000, which a signed Int16 read as -1, -25536 ...
public class LeveledEntryCountTests
{
    [Theory]
    [InlineData("01-00-00-00-0F-00-00-00-FF-FF-00-00", 65535)] // PEO_LLS_VendorCredits_5_XXLarge (LVLI 2FAA12)
    [InlineData("01-00-00-00-0F-00-00-00-40-9C-00-00", 40000)] // same list
    [InlineData("01-00-00-00-0F-00-00-00-20-4E-00-00", 20000)] // same list
    public void LeveledItem_Count_Reads(string lvlo, int expected)
    {
        var bytes = Record("LVLI", Subrecord("LLCT", "01"), Subrecord("LVLO", lvlo));
        foreach (var list in ReadBoth<ILeveledItemGetter>(bytes, f => LeveledItem.CreateFromBinary(f), (s, p) => LeveledItemBinaryOverlay.LeveledItemFactory(s, p)))
        {
            list.Entries!.Single().Count.ShouldBe((ushort)expected);
        }
    }

    [Fact]
    public void LeveledItem_Count_Writes()
    {
        var list = new LeveledItem(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield);
        list.Entries = new() { new LeveledItemEntry { Count = 65535 } };
        var written = GameConstants.Starfield.MajorRecord(Write(w => list.WriteToBinary(w)));
        BitConverter.ToUInt16(written.FindSubrecord(new RecordType("LVLO")).Content.Slice(8, 2)).ShouldBe((ushort)65535);
    }

    [Fact]
    public void AllFiveLeveledLists_HaveUnsignedCounts()
    {
        typeof(ILeveledItemEntryGetter).GetProperty("Count")!.PropertyType.ShouldBe(typeof(ushort));
        typeof(ILeveledNpcEntryGetter).GetProperty("Count")!.PropertyType.ShouldBe(typeof(ushort));
        typeof(ILeveledBaseFormEntryGetter).GetProperty("Count")!.PropertyType.ShouldBe(typeof(ushort));
        typeof(ILeveledPackInEntryGetter).GetProperty("Count")!.PropertyType.ShouldBe(typeof(ushort));
        typeof(ILeveledSpaceCellEntryGetter).GetProperty("Count")!.PropertyType.ShouldBe(typeof(ushort));
    }
}
