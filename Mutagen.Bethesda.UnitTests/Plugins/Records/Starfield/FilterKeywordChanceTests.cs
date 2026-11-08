using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A leveled list's filter keyword chance (LLKC) is a plain UInt32 percentage (xEdit's Starfield definitions: Chance,
// itU32). All 675 in Starfield.esm store 100. As a Percent divided by 4,294,967,295 it read as 0.000000023, and
// setting 0.5 would have written 2,147,483,647.
public class FilterKeywordChanceTests
{
    [Fact]
    public void Chance_Reads100()
    {
        // LL_Spacesuit_Navigator_Body_Quality_04 (LVLI 3D2642): keyword 11E2BD, chance 100
        var bytes = Record("LVLI", Subrecord("LLKC", "BD-E2-11-00-64-00-00-00-00-00-00-00"));
        foreach (var list in ReadBoth<ILeveledItemGetter>(bytes, f => LeveledItem.CreateFromBinary(f), (s, p) => LeveledItemBinaryOverlay.LeveledItemFactory(s, p)))
        {
            var chance = list.FilterKeywordChances!.Single();
            chance.Keyword.FormKey.ID.ShouldBe(0x11E2BDu);
            chance.Chance.ShouldBe(100u);
        }
    }

    [Fact]
    public void Chance_Writes()
    {
        var list = new LeveledItem(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield);
        list.FilterKeywordChances = new() { new FilterKeywordChance { Chance = 50 } };
        var written = GameConstants.Starfield.MajorRecord(Write(w => list.WriteToBinary(w)));
        BitConverter.ToUInt32(written.FindSubrecord(new RecordType("LLKC")).Content.Slice(4, 4)).ShouldBe(50u);
    }
}
