using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Projectile PROD per xEdit: Flags is one UInt32 (Mutagen read 2 bytes of flags and 2 bytes as "Type", which then held
// flag bits 16-19), the real Type is a byte after Sound Activation Condition (Missile 0 ... Emitter 8), and the UInt32
// after Tracer Frequency is the VATS Projectile link. In Starfield.esm: Missile 158, Beam 81, Lobber 18, Cone 7,
// Emitter 1; flag bits 16-19 on 6, 17 and 4 projectiles; Align On Projectile on 32.
public class ProjectileDataTests
{
    // PROD of Creature_Oxygen_Projectile (PROJ 3FA255): flags 0x20006, Cone, Sound Activation Default
    private const string OxygenProd =
        "06-00-02-00-00-00-00-3F-00-00-A0-41-00-00-20-41-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-54-A2-3F-00-00-00-80-3F-00-00-80-3F-00-00-20-41-00-00-00-00-00-00-34-42-0A-D7-A3-3D-00-00-20-41-00-00-80-3E-00-00-00-00-66-87-08-00-00-00-00-00-00-09-00-00-00-4D-75-7A-7A-6C-65-46-58-00-A6-9B-44-3B-02-00-00-00-04-00-00-00-00-00-00-48-42-CD-CC-4C-3E-9A-99-19-3E-00-00-00-00";

    private static IEnumerable<IProjectileGetter> ReadProjectile() =>
        ReadBoth<IProjectileGetter>(Record("PROJ", Subrecord("PROD", OxygenProd)), f => Projectile.CreateFromBinary(f), (s, p) => ProjectileBinaryOverlay.ProjectileFactory(s, p));

    [Fact]
    public void OxygenProjectile_Reads()
    {
        foreach (var proj in ReadProjectile())
        {
            proj.Flags.ShouldBe(Projectile.Flag.Explosion | Projectile.Flag.AltTrigger | Projectile.Flag.NoExplosiveIndicator);
            proj.Type.ShouldBe(Projectile.TypeEnum.Cone);
            proj.SoundActivationCondition.ShouldBe(Projectile.SoundActivationConditionOption.Default);
            proj.VatsProjectile.IsNull.ShouldBeTrue();
            proj.CollisionLayer.FormKey.ID.ShouldBe(0x088766u); // L_Weapon
            proj.MuzzleFlashParticleDefine.ShouldBe("MuzzleFX");
            proj.SeekStrength.ShouldBe(0.003f, 1e-6f);
            proj.ParticleDefine.ShouldBe("");
            proj.TracerSpeed.ShouldBe(50f);
            proj.ZeroGRecoilStrength.ShouldBe(0.2f, 1e-6f);
            proj.ZeroGRecoilLength.ShouldBe(0.15f, 1e-6f);
            proj.ExplosionDelayBeforeRemoval.ShouldBe(0f);
        }
    }

    [Fact]
    public void Prod_WritesSameBytes()
    {
        foreach (var proj in ReadProjectile())
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => proj.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("PROD")).Content.ToArray().ShouldBe(Hex(OxygenProd));
        }
    }

    [Fact]
    public void HighFlags_And_VatsLink_Write()
    {
        var proj = new Projectile(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            Flags = Projectile.Flag.AlignOnProjectile | Projectile.Flag.ApplyChargeMultiplier,
            Type = Projectile.TypeEnum.Emitter,
        };
        proj.VatsProjectile.SetTo(new FormKey(TestModKey, 0x123));
        var prod = GameConstants.Starfield.MajorRecord(Write(w => proj.WriteToBinary(w))).FindSubrecord(new RecordType("PROD")).Content;
        BitConverter.ToUInt32(prod.Slice(0, 4)).ShouldBe(0x8_8000u);
        (BitConverter.ToUInt32(prod.Slice(77, 4)) & 0xFFFFFF).ShouldBe(0x123u);
        proj.EnumerateFormLinks().Select(l => l.FormKey.ID).ShouldContain(0x123u);
    }
}
