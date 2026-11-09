using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// NPC ONA2 (race overrides, on 184 NPCs in Starfield.esm) was raw bytes. It is xEdit's layout: a u32 of section flags,
// then each flagged section in order (Size as a signed byte, Movement, Flight, Unarmed Weapon, Flag Overrides,
// Bleedout, General, Electromagnetic Shocked). Also CS3F, the NPC sound block's Stop Conscious Loop When Unconscious bool.
// Each test reads the real subrecord with both readers and writes it back byte for byte.
public class NpcRaceOverrideTests
{
    // CCT_Template_RaceOverride_Tunneling_NoAppearanceData (181E2E:Starfield.esm): all 8 sections
    private const string Tunneling = "FF000000FF0000803F0000803F0000803E00003442000000000AD7233CF40100004089A0100040000200000000000000020000803E6666663F0000803E00000041000040410AD7233CD5230200000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000010000803F0000A040000020410000003F000000000000803F0000803F";

    // OutpostRobotMiniBotA_Garden (04297C:Starfield.esm): Size Small, flag overrides, corpse sounds
    private const string MiniBot = "DF00000000CDCCCC3DCDCCCC3D0000803E0000A04000000000000000008F791E0028C92042004000000000100000000000552902006F4243A8EA1DA128DDB620C58F35919B000000000000000000000000000000000000000000000000774354E0FB79B43A2D2C41B503D5DC83000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000";

    // Traits_OctopedeA_BlisterCrab_Small (2BF30F:Starfield.esm): movement floats xEdit marks "sometimes broken"
    private const string BlisterCrab = "C3000000FFFF7F7FFFFF7F7FFFFF7F7FFFFF7F7F000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000803F0000000000000000000000000000000000000000000000000000000000000000000000";

    // UC_NA_CL_TolimanScene_Friend (00697A:Starfield.esm): Size, Flight, Unarmed Weapon, General, EM
    private const string Friend = "CD000000FF0000344200003442000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000803F0000000000000000000000000000000000000000000000000000000000000000000000";

    // ACBS of CCT_Template_RaceOverride_Tunneling_NoAppearanceData; an NPC needs one to be written
    private const string Acbs = "00000000000001000000000023000000";

    private static IEnumerable<INpcGetter> Read(byte[] bytes)
        => ReadBoth<INpcGetter>(bytes, f => Npc.CreateFromBinary(f), (s, p) => NpcBinaryOverlay.NpcFactory(s, p));

    private static void WritesBack(INpcGetter npc, string type, string hex)
    {
        var written = GameConstants.Starfield.MajorRecord(Write(w => npc.WriteToBinary(w)));
        written.FindSubrecord(new RecordType(type)).Content.ToArray().ShouldBe(Hex(hex));
    }

    [Fact]
    public void AllSections()
    {
        foreach (var npc in Read(Record("NPC_", Subrecord("ACBS", Acbs), Subrecord("ONA2", Tunneling))))
        {
            var o = npc.RaceOverrides!;
            o.Size.ShouldBe(Size.Unknown);
            o.Movement!.AccelerationRate.ShouldBe(1f);
            o.Movement.AngularAccelerationRate.ShouldBe(0.25f);
            o.Movement.AngularTolerance.ShouldBe(45f);
            o.Flight!.FlightRadius.ShouldBe(0f);
            o.UnarmedWeapon!.InjuredHealthPercent.ShouldBe(0.01f);
            o.UnarmedWeapon.UnarmedWeapon.FormKey.ID.ShouldBe(0x1F4u);
            // bit 57 is xEdit's Tunnels (named on cov/starfield-race-dat2)
            o.FlagOverrides!.OverrideActive.ShouldBe((Race.Flag)(1ul << 57));
            o.FlagOverrides.RaceDataFlags.HasFlag((Race.Flag)(1ul << 57)).ShouldBeTrue();
            o.Bleedout!.HealthPercent.ShouldBe(0.25f);
            o.Bleedout.Chance.ShouldBe(0.9f);
            o.Bleedout.TimeMin.ShouldBe(8f);
            o.Bleedout.TimeMax.ShouldBe(12f);
            o.Bleedout.HealthDrainRate.ShouldBe(0.01f);
            o.General!.ImpactMaterialType.FormKey.ID.ShouldBe(0x0223D5u);
            o.General.OnCrippleDebrisScale.ShouldBe(0f);
            o.General.ActiveOverrides.ShouldBe((NpcRaceOverrideGeneral.ActiveOverride)0);
            o.ElectromagneticShock!.Supported.ShouldBeTrue();
            o.ElectromagneticShock.RecoverChance.ShouldBe(1f);
            o.ElectromagneticShock.TimeMin.ShouldBe(5f);
            o.ElectromagneticShock.TimeMax.ShouldBe(10f);
            o.ElectromagneticShock.RecoverChanceOnDamageReceived.ShouldBe(0.5f);
            o.ElectromagneticShock.HealthAfterRecovery.ShouldBe(1f);
            o.ElectromagneticShock.ImmunityDurationAfterRecovery.ShouldBe(1f);
            WritesBack(npc, "ONA2", Tunneling);
        }
    }

    [Fact]
    public void SizeFlagsAndCorpseSounds()
    {
        foreach (var npc in Read(Record("NPC_", Subrecord("ACBS", Acbs), Subrecord("ONA2", MiniBot))))
        {
            var o = npc.RaceOverrides!;
            o.Size.ShouldBe(Size.Small);
            o.Movement!.AccelerationRate.ShouldBe(0.1f);
            o.UnarmedWeapon!.UnarmedWeapon.FormKey.ID.ShouldBe(0x1E798Fu);
            o.FlagOverrides!.OverrideActive.ShouldBe(Race.Flag.CantOpenDoors);
            o.Bleedout.ShouldBeNull();
            o.General!.ImpactMaterialType.FormKey.ID.ShouldBe(0x022955u);
            o.ElectromagneticShock!.Supported.ShouldBeFalse();
            WritesBack(npc, "ONA2", MiniBot);
        }
    }

    [Fact]
    public void BrokenMovementFloats_RoundTrip()
    {
        foreach (var npc in Read(Record("NPC_", Subrecord("ACBS", Acbs), Subrecord("ONA2", BlisterCrab))))
        {
            var o = npc.RaceOverrides!;
            o.Size.ShouldBe(Size.Unknown);
            o.Movement.ShouldNotBeNull();
            o.Flight.ShouldBeNull();
            o.UnarmedWeapon.ShouldBeNull();
            o.General.ShouldNotBeNull();
            o.ElectromagneticShock.ShouldNotBeNull();
            WritesBack(npc, "ONA2", BlisterCrab);
        }
    }

    [Fact]
    public void FlightAndUnarmed()
    {
        foreach (var npc in Read(Record("NPC_", Subrecord("ACBS", Acbs), Subrecord("ONA2", Friend))))
        {
            var o = npc.RaceOverrides!;
            o.Movement.ShouldBeNull();
            o.Flight!.FlightRadius.ShouldBe(45f);
            o.UnarmedWeapon!.InjuredHealthPercent.ShouldBe(45f);
            o.UnarmedWeapon.UnarmedWeapon.IsNull.ShouldBeTrue();
            WritesBack(npc, "ONA2", Friend);
        }
    }

    [Fact]
    public void Sounds_StopConsciousLoopWhenUnconscious()
    {
        // LvlRobotModelA_Industrial_Spaceport (003A5A:Starfield.esm)
        const string sound = "DB4590A17E086A0172C27C7A0DA0908E3342C259E6401923EB9F7381F8AE7F860000000000000000";
        var bytes = Record("NPC_", Subrecord("ACBS", "080100000F0006000000000023003E6F"), Subrecord("CS3H", "01000000"), Subrecord("CS3S", sound), Subrecord("CS3F", "01"), Subrecord("CS3E", ""));
        foreach (var npc in Read(bytes))
        {
            npc.Sounds.Single().StopConsciousLoopWhenUnconscious.ShouldBe(true);
            WritesBack(npc, "CS3F", "01");
        }
    }
}
