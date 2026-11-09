using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// WEAP WTUR (Turret) was kept as 29 raw bytes. xEdit reads Enabled (one byte, 0 or 1 on all 406 weapons in
// Starfield.esm), then the floats Heading Min/Max, Pitch Min/Max, Heading Speed, Pitch Speed and Accuracy.
// Each subrecord is copied whole from a Starfield.esm weapon, read with both readers and written back byte for byte.
public class WeaponTurretTests
{
    private static IEnumerable<IWeaponGetter> Read(string hex)
        => ReadBoth<IWeaponGetter>(Record("WEAP", Subrecord("WTUR", hex)), f => Weapon.CreateFromBinary(f), (s, p) => WeaponBinaryOverlay.WeaponFactory(s, p));

    private static void WritesBack(IWeaponGetter weap, string hex)
    {
        var written = GameConstants.Starfield.MajorRecord(Write(w => weap.WriteToBinary(w)));
        written.FindSubrecord(new RecordType("WTUR")).Content.ToArray().ShouldBe(Hex(hex));
    }

    [Fact]
    public void ShipTurret_Enabled()
    {
        // SWC_Particle_Ballistic_PBO-300_Alpha_Auto_turret_lvl34 (025FB3:Starfield.esm)
        const string hex = "01-00-00-34-C2-00-00-34-42-00-00-70-C2-00-00-A0-40-00-00-B4-42-00-00-B4-42-00-00-80-3F";
        foreach (var weap in Read(hex))
        {
            weap.TurretEnabled.ShouldBeTrue();
            weap.TurretHeadingMin.ShouldBe(-45f);
            weap.TurretHeadingMax.ShouldBe(45f);
            weap.TurretPitchMin.ShouldBe(-60f);
            weap.TurretPitchMax.ShouldBe(5f);
            weap.TurretHeadingSpeed.ShouldBe(90f);
            weap.TurretPitchSpeed.ShouldBe(90f);
            weap.TurretAccuracy.ShouldBe(1f);
            WritesBack(weap, hex);
        }
    }

    [Fact]
    public void LaserTurret_Enabled()
    {
        // SWL_Laser_Shinigami_800_3L3W3H_Turret (01BC22:Starfield.esm)
        const string hex = "01-00-00-B4-C2-00-00-B4-42-00-00-34-C2-00-00-A0-40-00-00-20-43-00-00-F0-42-00-00-80-3F";
        foreach (var weap in Read(hex))
        {
            weap.TurretEnabled.ShouldBeTrue();
            weap.TurretHeadingMin.ShouldBe(-90f);
            weap.TurretHeadingMax.ShouldBe(90f);
            weap.TurretPitchMin.ShouldBe(-45f);
            weap.TurretPitchMax.ShouldBe(5f);
            weap.TurretHeadingSpeed.ShouldBe(160f);
            weap.TurretPitchSpeed.ShouldBe(120f);
            weap.TurretAccuracy.ShouldBe(1f);
            WritesBack(weap, hex);
        }
    }

    [Fact]
    public void HandWeapon_Disabled()
    {
        // Cutter (016758:Starfield.esm): the default values most weapons store
        const string hex = "00-00-00-34-C3-00-00-34-43-00-00-B4-C2-00-00-B4-42-00-00-34-43-00-00-34-43-00-00-80-3F";
        foreach (var weap in Read(hex))
        {
            weap.TurretEnabled.ShouldBeFalse();
            weap.TurretHeadingMin.ShouldBe(-180f);
            weap.TurretHeadingMax.ShouldBe(180f);
            weap.TurretPitchMin.ShouldBe(-90f);
            weap.TurretPitchMax.ShouldBe(90f);
            weap.TurretHeadingSpeed.ShouldBe(180f);
            weap.TurretPitchSpeed.ShouldBe(180f);
            weap.TurretAccuracy.ShouldBe(1f);
            WritesBack(weap, hex);
        }
    }

    [Fact]
    public void DummyFire_SmallFloats()
    {
        // DefensiveLaserBatteryWeapon_DummyFire (003C6D:Starfield.esm): +-0.1 ranges
        const string hex = "00-CD-CC-CC-BD-CD-CC-CC-3D-CD-CC-CC-BD-CD-CC-CC-3D-00-00-34-43-00-00-34-43-00-00-80-3F";
        foreach (var weap in Read(hex))
        {
            weap.TurretEnabled.ShouldBeFalse();
            weap.TurretHeadingMin.ShouldBe(-0.1f);
            weap.TurretHeadingMax.ShouldBe(0.1f);
            weap.TurretPitchMin.ShouldBe(-0.1f);
            weap.TurretPitchMax.ShouldBe(0.1f);
            WritesBack(weap, hex);
        }
    }
}
