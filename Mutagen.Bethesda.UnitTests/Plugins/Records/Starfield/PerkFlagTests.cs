using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield perk flags (DATA byte 3) include Hidden 0x04 and Hidden on Data Menu 0x20 (xEdit's Starfield
// definitions); Starfield.esm sets them on 107 and 60 perks.
public class PerkFlagTests
{
    private static IEnumerable<IPerkGetter> ReadPerk(string data) =>
        ReadBoth<IPerkGetter>(Record("PERK", Subrecord("DATA", data)),
            f => Perk.CreateFromBinary(f),
            (s, p) => PerkBinaryOverlay.PerkFactory(s, p));

    [Fact]
    public void Hidden_Reads()
    {
        // ModIncomingDamageFakeCombatShip (PERK 3F14E1): DATA 00 00 00 04
        foreach (var perk in ReadPerk("00-00-00-04"))
        {
            perk.Flags.ShouldBe(Perk.Flag.Hidden);
        }
    }

    [Fact]
    public void HiddenOnDataMenu_Reads()
    {
        // Perk_LegendaryWeapon_Corrosive (PERK 388574): DATA 00 00 00 22
        foreach (var perk in ReadPerk("00-00-00-22"))
        {
            perk.Flags.ShouldBe(Perk.Flag.PcPlayable | Perk.Flag.HiddenOnDataMenu);
        }
    }

    [Fact]
    public void HiddenFlags_Write()
    {
        var perk = new Perk(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            Flags = Perk.Flag.Hidden | Perk.Flag.HiddenOnDataMenu,
        };
        var written = GameConstants.Starfield.MajorRecord(Write(w => perk.WriteToBinary(w)));
        written.FindSubrecord(new RecordType("DATA")).Content[3].ShouldBe((byte)0x24);
    }
}
