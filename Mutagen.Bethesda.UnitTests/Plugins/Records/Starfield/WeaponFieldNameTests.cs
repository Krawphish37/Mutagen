using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// WEAP fields Mutagen named after their subrecord (WAIMUnknown1, QNAMUnknown4 ...) get xEdit's names. The one-byte
// booleans become Bool (every one is 0 or 1 in Starfield.esm), and Reticle Type becomes an enum.
// Each subrecord is copied whole from a Starfield.esm weapon where the fields are set.
public class WeaponFieldNameTests
{
    private static IEnumerable<IWeaponGetter> Read(params (string Sig, string Hex)[] subs)
        => ReadBoth<IWeaponGetter>(Record("WEAP", subs.Select(s => Subrecord(s.Sig, s.Hex)).ToArray()), f => Weapon.CreateFromBinary(f), (s, p) => WeaponBinaryOverlay.WeaponFactory(s, p));

    private static void WritesBack(IWeaponGetter weap, params (string Sig, string Hex)[] subs)
    {
        var written = GameConstants.Starfield.MajorRecord(Write(w => weap.WriteToBinary(w)));
        foreach (var (sig, hex) in subs)
        {
            written.FindSubrecord(new RecordType(sig)).Content.ToArray().ShouldBe(Hex(hex), sig);
        }
    }

    [Fact]
    public void Cutter_AimAndPower()
    {
        // Cutter (016758:Starfield.esm)
        var subs = new[]
        {
            ("WAIM", "00-00-80-3E-46-B2-04-00-27-D7-1E-00-00-00-61-F1-2F-00-00-00-00-00-00-00-00-00-01-01-00-07-00-00-00-00-00"),
            ("QNAM", "00-00-00-00-00-00-A0-40-9A-99-59-3F-00-00-00-00-00-01-01-CD-CC-CC-3F-00-31-6E-03-00-00-00-20-40-9A-01-06-00"),
        };
        foreach (var weap in Read(subs))
        {
            weap.UpdateAdvancedAimInfo.ShouldBeTrue();
            weap.EnableOrientFireNodesToAimingDirection.ShouldBeTrue();
            weap.ReticleType.ShouldBe(Weapon.ReticleTypeEnum.CustomCrosshair1);
            weap.InverseReticleSpread.ShouldBeFalse();
            weap.VariablePowerEnabled.ShouldBeTrue();
            weap.VariablePowerTransitionDuration.ShouldBe(1.6f);
            weap.UseChargeOnlyInFocusMode.ShouldBeFalse();
            weap.PowerIntensityCurve.FormKey.ID.ShouldBe(0x036E31u);
            weap.PowerDamageMultiplier.ShouldBe(2.5f);
            WritesBack(weap, subs);
        }
    }

    [Fact]
    public void Microgun_Reticle()
    {
        // Microgun (0546CD:Starfield.esm): Shotgun reticle, both reticle flags set
        var subs = new[] { ("WAIM", "33-33-B3-3E-3E-40-1A-00-A0-B5-27-00-00-00-A1-B5-27-00-00-00-00-00-00-00-00-00-00-00-00-03-00-00-00-01-01") };
        foreach (var weap in Read(subs))
        {
            weap.UpdateAdvancedAimInfo.ShouldBeFalse();
            weap.ReticleType.ShouldBe(Weapon.ReticleTypeEnum.Shotgun);
            weap.InverseReticleSpread.ShouldBeTrue();
            weap.DisplayedReticleIgnoresPlayerMovement.ShouldBeTrue();
            WritesBack(weap, subs);
        }
    }

    [Fact]
    public void Razorback_Firing()
    {
        // Razorback (000FD6:Starfield.esm)
        var subs = new[] { ("WFIR", "00-00-00-00-00-80-3F-00-00-00-34-CD-CC-4C-3F-00-00-00-00-00-00-00-00-A0-3F-CD-CC-4C-3F-00-C3-F5-A8-3E-01-C3-F5-A8-3E-00-00-9A-99-99-3E") };
        foreach (var weap in Read(subs))
        {
            weap.WeaponAttackAnimationDuration.ShouldBe(0.8f);
            weap.OverrideTriggerThreshold.ShouldBeTrue();
            weap.BurstDelaySeconds.ShouldBe(0.3f);
            WritesBack(weap, subs);
        }
    }

    [Fact]
    public void UnarmedHuman_ShipWeaponAmmoDamage()
    {
        // UnarmedHuman (0001F4:Starfield.esm)
        var subs = new[]
        {
            ("WTRM", "00-00-A0-41-00-9A-99-99-3E-00-00-00-3F-00-00-F0-41"),
            ("WAM2", "00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-00-01"),
            ("WDMG", "00-00-E0-40-00-00-FA-44-00-00-A0-41-00-00-80-BF-00-00-00-40-00-00-00-00-01-03-00-00-00-00-00-00-00-00-00-00-80-3F-00-00-80-3F-00-00-80-3F-00-00-80-3F-00-00-80-3F-00-00-80-3F-00-00-80-3F"),
        };
        foreach (var weap in Read(subs))
        {
            weap.AttackActionPointCost.ShouldBe(20f);
            weap.OverrideDefaultCameraShake.ShouldBeFalse();
            weap.FiringCameraShakeIntensity.ShouldBe(0.3f);
            weap.FiringCameraShakeDuration.ShouldBe(0.5f);
            weap.AiFiringArc.ShouldBe(30f);
            weap.NumberOfImpactSounds.ShouldBe((byte)1);
            weap.LowGravityScaleControlPoint.ShouldBe(1f);
            weap.HighGravityRangeMultMax.ShouldBe(1f);
            WritesBack(weap, subs);
        }
    }

    [Fact]
    public void Reza_Flags()
    {
        // SWA_Laser_Shinigami_Reza_30_THz_IR_Auto_lvl58 (22EC6D:Starfield.esm)
        var subs = new[] { ("WFLG", "01-00-00-00-01-00-00-00-01-01-00-00") };
        foreach (var weap in Read(subs))
        {
            weap.PreventUseInNormalCombat.ShouldBeFalse();
            weap.NpcPrefersLongBursts.ShouldBeTrue();
            weap.NpcsOnlyFireWhenMagazineFull.ShouldBeTrue();
            weap.UseSequentialFireNodes.ShouldBeFalse();
            WritesBack(weap, subs);
        }
    }
}
