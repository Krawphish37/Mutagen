using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A leveled base form's flags (LVLB LVLF) are LeveledBaseForm.Flag, not the leveled item flags: 0x08/0x10 are
// Disable If Does Not Resolve, 0x40 Allow Shift Up, 0x200 Contains Only Spaceship Base Forms (xEdit's Starfield
// definitions). Starfield.esm: 20, 93 and 335 leveled base forms.
public class LeveledBaseFormFlagTests
{
    [Fact]
    public void LeveledBaseFormFlags_Read()
    {
        // LShip_Civilian_50 (LVLB 3BEFB1): LVLF 0x025B
        var bytes = Record("LVLB", Subrecord("LVLF", "5B-02"));
        foreach (var lvlb in ReadBoth<ILeveledBaseFormGetter>(bytes, f => LeveledBaseForm.CreateFromBinary(f), (s, p) => LeveledBaseFormBinaryOverlay.LeveledBaseFormFactory(s, p)))
        {
            lvlb.Flags.ShouldBe(LeveledBaseForm.Flag.CalculateFromAllLevelsLessThanOrEqualPlayer
                | LeveledBaseForm.Flag.CalculateForEachItemInCount
                | LeveledBaseForm.Flag.DisableIfDoesNotResolve1 | LeveledBaseForm.Flag.DisableIfDoesNotResolve2
                | LeveledBaseForm.Flag.AllowShiftUp | LeveledBaseForm.Flag.ContainsOnlySpaceshipBaseForms);
        }
    }

    [Fact]
    public void LeveledBaseFormFlags_Write()
    {
        var lvlb = new LeveledBaseForm(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            Flags = LeveledBaseForm.Flag.AllowShiftUp | LeveledBaseForm.Flag.ContainsOnlySpaceshipBaseForms,
        };
        var written = GameConstants.Starfield.MajorRecord(Write(w => lvlb.WriteToBinary(w)));
        BitConverter.ToUInt16(written.FindSubrecord(new RecordType("LVLF")).Content.Slice(0, 2)).ShouldBe((ushort)0x240);
    }
}
