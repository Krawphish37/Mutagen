using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Naming decisions taken on 2026-11-10 (Wwise GUID form, NPC DNAM Unknown, XHLT integer percent, terminal art themes).
public class DecisionNameTests
{
    // The 16 bytes of a WEAP sound start event. .NET shows them as A97E454F-4307-41EE-45D0-7BD23FC3DE8D; xEdit and
    // Wwise show {41EE4307-A97E-454F-8DDE-C33FD27BD045} (ITM_Generic_Weapon_Rifle_Light_Up).
    private const string WeaponSoundGuid = "4F457EA9 0743 EE41 45D0 7BD23FC3DE8D";

    [Fact]
    public void SoundGuid_WwiseForm()
    {
        var guid = new Guid(Hex(WeaponSoundGuid));
        guid.ToString().ToUpperInvariant().ShouldBe("A97E454F-4307-41EE-45D0-7BD23FC3DE8D");
        guid.ToWwiseString().ShouldBe("{41EE4307-A97E-454F-8DDE-C33FD27BD045}");

        var sound = new SoundReference { Start = guid, Stop = Guid.Empty };
        sound.StartWwiseForm().ShouldBe("{41EE4307-A97E-454F-8DDE-C33FD27BD045}");
        sound.StopWwiseForm().ShouldBe("{00000000-0000-0000-0000-000000000000}");
        // A view only: the Guid itself is unchanged
        sound.Start.ShouldBe(guid);
    }

    // LC107_Crew_Liang_Corpse (001E3D:Starfield.esm) DNAM: the last byte (0x2B, as on most HumanRace NPCs) is data.
    [Fact]
    public void NpcDnam_LastByteIsUnknown()
    {
        const string dnam = "900132000000012B";
        var bytes = Record("NPC_", Subrecord("EDID", System.Text.Encoding.ASCII.GetBytes("LC107_Crew_Liang_Corpse\0")), Subrecord("DNAM", dnam));
        foreach (var npc in ReadBoth<INpcGetter>(bytes, f => Npc.CreateFromBinary(f), (s, p) => NpcBinaryOverlay.NpcFactory(s, p)))
        {
            npc.GearedUpWeapons.ShouldBe((byte)1);
            npc.Unknown.ShouldBe((byte)0x2B);
            var written = GameConstants.Starfield.MajorRecord(Write(w => npc.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("DNAM")).Content.ToArray().ShouldBe(Hex(dnam));
        }
    }

    // XHLT Health %: an integer percent (xEdit itU32). All 16 in Starfield.esm store 0; 50 is synthetic.
    [Theory]
    [InlineData("00000000", 0u)]
    [InlineData("32000000", 50u)]
    public void Xhlt_IntegerPercent(string xhlt, uint expected)
    {
        var refr = Record("REFR", Subrecord("XHLT", xhlt));
        foreach (var placed in ReadBoth<IPlacedObjectGetter>(refr, f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            placed.HealthPercent.ShouldBe(expected);
            var written = GameConstants.Starfield.MajorRecord(Write(w => placed.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("XHLT")).Content.ToArray().ShouldBe(Hex(xhlt));
        }
        var achr = Record("ACHR", Subrecord("XHLT", xhlt));
        foreach (var placed in ReadBoth<IPlacedNpcGetter>(achr, f => PlacedNpc.CreateFromBinary(f), (s, p) => PlacedNpcBinaryOverlay.PlacedNpcFactory(s, p)))
        {
            placed.Health.ShouldBe(expected);
            var written = GameConstants.Starfield.MajorRecord(Write(w => placed.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("XHLT")).Content.ToArray().ShouldBe(Hex(xhlt));
        }
    }

    // TERM NAM1 art themes 9-11 (xEdit: House Varuun, Ship Upgrade, Terran Armada). No plugin we have uses them yet.
    [Theory]
    [InlineData("09", Terminal.BackgroundType.HouseVaruun)]
    [InlineData("0A", Terminal.BackgroundType.ShipUpgrade)]
    [InlineData("0B", Terminal.BackgroundType.TerranArmada)]
    [InlineData("07", Terminal.BackgroundType.CrimsonFleet)]
    public void Terminal_ArtThemes(string nam1, Terminal.BackgroundType expected)
    {
        var bytes = Record("TERM", Subrecord("NAM1", nam1));
        foreach (var term in ReadBoth<ITerminalGetter>(bytes, f => Terminal.CreateFromBinary(f), (s, p) => TerminalBinaryOverlay.TerminalFactory(s, p)))
        {
            term.Background.ShouldBe(expected);
            var written = GameConstants.Starfield.MajorRecord(Write(w => term.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("NAM1")).Content.ToArray().ShouldBe(Hex(nam1));
        }
    }
}
