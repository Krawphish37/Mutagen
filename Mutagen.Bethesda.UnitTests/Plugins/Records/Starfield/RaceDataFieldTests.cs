using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Race DAT2: the three raw blocks DAT2Unknown1-3 become xEdit's fields (Head/Hair biped objects, Injured Health Pct,
// Aim Angle Tolerance, Flight Radius, Angular Acceleration Rate/Tolerance, Pipboy biped object, XP Value, OnCripple
// debris, Bleedout, Max Size, Intelligence Level, Electromagnetic Shocked). xEdit's own unknowns (28 zero bytes, a float
// and 4 bytes) stay Unknown1-3. Race.Flag gets xEdit's Starfield names where Mutagen had Fallout 4's (Jumps,
// Not Pushable, Simulate Cloth On Down) and the bits it lacked (46, 57-59).
// Biped objects are checked by number: their names are Fallout 4's until C47 (cov/starfield-biped-objects) is merged.
public class RaceDataFieldTests
{
    // A race writes its 64 biped object names (NAME) and their conditions (RBPC); the binary overlay needs them present.
    private static byte[][] BipedObjectNames()
        => Enumerable.Range(0, 64).Select(_ => Subrecord("NAME", "00")).Append(Subrecord("RBPC", new byte[256])).ToArray();

    // DAT2 of HumanRace (00347D:Starfield.esm)
    private const string Human = "0000803F0000803F4CA6AA3E4CA6AA3E4CA6AA3E4CA6AA3E4CA6AA3E4CA6AA3E6389A010214084000000803F0000803F0100000002000000000000000AD7233C1D00000011000000030000000000F041000000000000803E0000344200000000000000000000000000000000000000000000000000000000000096C300000000FFFFFFFF00000000000000000000000000000000000000000000B4420000B4420000803E0000803E0000803E00000041000040410AD7233C0100000002000000010000803F00001644000016440000803F000000000000803F0000803F";

    // DAT2 of LarvaARace (29A905:Starfield.esm)
    private const string Larva = "0000803F0000803F4CA6AA3E4CA6AA3E4CA6AA3EABAAAA3EABAAAA3EABAAAA3E19011000004322000000403F0000403F00000000FFFFFFFFFFFFFFFF00000000FFFFFFFFFFFFFFFF030000000000A041000000000000803E0000A04000000000000000000000000000000000000000000000000000000000000096C300000000FFFFFFFF00000000000000000000000000000000000000000000B4420000B44200000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000";

    private static IEnumerable<IRaceGetter> Read(string dat2)
        => ReadBoth<IRaceGetter>(Record("RACE", [Subrecord("DAT2", dat2), .. BipedObjectNames()]), f => Race.CreateFromBinary(f), (s, p) => RaceBinaryOverlay.RaceFactory(s, p));

    private static void WritesBack(IRaceGetter race, string dat2)
    {
        var written = GameConstants.Starfield.MajorRecord(Write(w => race.WriteToBinary(w)));
        written.FindSubrecord(new RecordType("DAT2")).Content.ToArray().ShouldBe(Hex(dat2));
    }

    [Fact]
    public void HumanRace_Dat2Fields()
    {
        foreach (var race in Read(Human))
        {
            race.Size.ShouldBe(Size.Medium);
            ((int)race.HeadBipedObject).ShouldBe(2);
            ((int)race.HairBipedObject).ShouldBe(0);
            race.InjuredHealthPercent.ShouldBe(0.01f);
            ((int)race.ShieldBipedObject).ShouldBe(29);
            ((int)race.BeardBipedObject).ShouldBe(17);
            ((int)race.BodyBipedObject).ShouldBe(3);
            race.AimAngleTolerance.ShouldBe(30f);
            race.FlightRadius.ShouldBe(0f);
            race.AngularAccelerationRate.ShouldBe(0.25f);
            race.AngularTolerance.ShouldBe(45f);
            race.Unknown2.ShouldBe(-300f);
            ((int)race.PipboyBipedObject).ShouldBe(-1);
            race.XpValue.ShouldBe((short)0);
            race.OrientationLimitsPitch.ShouldBe(90f);
            race.BleedoutHealthPercent.ShouldBe(0.25f);
            race.BleedoutChance.ShouldBe(0.25f);
            race.BleedoutRecoverChance.ShouldBe(0.25f);
            race.BleedoutTimeMin.ShouldBe(8f);
            race.BleedoutTimeMax.ShouldBe(12f);
            race.BleedoutHealthDrainRate.ShouldBe(0.01f);
            race.MaxSize.ShouldBe(Size.Medium);
            race.IntelligenceLevel.ShouldBe(Race.IntelligenceLevelType.Normal);
            race.SupportsElectromagneticShock.ShouldBeTrue();
            race.ElectromagneticShockRecoverChance.ShouldBe(1f);
            race.ElectromagneticShockTimeMin.ShouldBe(600f);
            race.ElectromagneticShockTimeMax.ShouldBe(600f);
            race.ElectromagneticShockRecoverChanceOnDamageReceived.ShouldBe(1f);
            race.ElectromagneticShockHealthDrainRate.ShouldBe(0f);
            race.ElectromagneticShockHealthAfterRecovery.ShouldBe(1f);
            race.ElectromagneticShockImmunityDurationAfterRecovery.ShouldBe(1f);
            race.Flags.ShouldBe(race.Flags | Race.Flag.Jumps | Race.Flag.CalcCharacterControllerRotationCenterOff | Race.Flag.HasFacialRig);
            WritesBack(race, Human);
        }
    }

    [Fact]
    public void LarvaARace_FlagsAndDefaults()
    {
        foreach (var race in Read(Larva))
        {
            race.Size.ShouldBe(Size.Small);
            ((int)race.HeadBipedObject).ShouldBe(-1);
            race.AimAngleTolerance.ShouldBe(20f);
            race.AngularTolerance.ShouldBe(5f);
            race.IntelligenceLevel.ShouldBe(Race.IntelligenceLevelType.None);
            race.SupportsElectromagneticShock.ShouldBeFalse();
            race.Flags.HasFlag(Race.Flag.SimulateClothOnDown).ShouldBeTrue();
            race.Flags.HasFlag(Race.Flag.LowPriorityPushable).ShouldBeTrue();
            race.Flags.HasFlag(Race.Flag.CalcCharacterControllerRotationCenterOff).ShouldBeTrue();
            race.Flags.HasFlag(Race.Flag.Jumps).ShouldBeFalse();
            WritesBack(race, Larva);
        }
    }

    [Fact]
    public void Flag_TunnelsHopsTeleport()
    {
        ((ulong)Race.Flag.CalcCharacterControllerRotationCenterOff).ShouldBe(1ul << 46);
        ((ulong)Race.Flag.Tunnels).ShouldBe(1ul << 57);
        ((ulong)Race.Flag.Hops).ShouldBe(1ul << 58);
        ((ulong)Race.Flag.Teleport).ShouldBe(1ul << 59);
    }
}
