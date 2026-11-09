using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Three misspelled field names, renamed to xEdit's spelling (same bytes, same types): Perk Categroy -> Category,
// CombatStyle DodgeThreadChance -> DodgeThreatChance, CellLighting ForColorHighNear/Far -> FogColorHighNear/Far.
public class SpellingFixTests
{
    [Fact]
    public void CombatStyle_DodgeThreatChance()
    {
        // CSGD of CombatStyle 2BFC0E: Avoid Threat 0.5, Dodge Threat 0.75, Evade Threat 0.6
        var bytes = Record("CSTY", Subrecord("CSGD",
            "00-00-80-3F-00-00-00-00-00-00-00-00-FF-FF-1F-41-00-00-80-3F-CC-CC-CC-3D-00-00-00-00-FF-FF-1F-41-00-00-00-00-00-00-00-3F-00-00-40-3F-99-99-19-3F-00-00-00-00-00-00-80-3F-00-00-80-3F"));
        // The value itself (0.75) needs the C32 fix (Percent -> Float), which is on its own branch; here both readers
        // must read the renamed field from the same bytes.
        var values = ReadBoth<ICombatStyleGetter>(bytes, f => CombatStyle.CreateFromBinary(f), (s, p) => CombatStyleBinaryOverlay.CombatStyleFactory(s, p))
            .Select(s => (double)s.DodgeThreatChance).ToArray();
        values[1].ShouldBe(values[0]);
    }

    [Fact]
    public void Perk_Category()
    {
        // DATA of ModIncomingDamageFakeCombatShip (PERK 3F14E1): category 0, flags 0x04
        foreach (var perk in ReadBoth<IPerkGetter>(Record("PERK", Subrecord("DATA", "00-00-00-04")), f => Perk.CreateFromBinary(f), (s, p) => PerkBinaryOverlay.PerkFactory(s, p)))
        {
            perk.Category.ShouldBe((PerkCategory)0);
        }
    }

    [Fact]
    public void CellLighting_FogColorHigh()
    {
        // XCLL of cell 3BC26F: both high fog colours are 0
        const string lighting =
            "00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-80-3F-00-00-00-00-00-00-80-3F-00-00-00-00-00-00-80-3F-00-00-00-00-00-00-00-00-9F-07-00-00-00-00-00-00-00-40-1C-46-00-00-00-00-00-00-00-00-00-00-80-3F-00-00-80-3F-00-00-80-3F-00-00-80-3F-00-00-80-3F-00-00-00-00-00-40-1C-46-03-AB-AB-AB";
        foreach (var cell in ReadBoth<ICellGetter>(Record("CELL", Subrecord("XCLL", lighting)), f => Cell.CreateFromBinary(f), (s, p) => CellBinaryOverlay.CellFactory(s, p)))
        {
            cell.Lighting!.FogColorHighNear.ToArgb().ShouldBe(0);
            cell.Lighting!.FogColorHighFar.ToArgb().ShouldBe(0);
        }
    }
}
