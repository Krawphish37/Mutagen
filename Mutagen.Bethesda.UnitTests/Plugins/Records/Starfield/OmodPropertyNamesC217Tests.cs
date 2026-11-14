using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C217: object mod property members renamed to the game's own names (its property tables, C209 dump), each still the
// same 4-letter ID, so no stored byte changes.
public class OmodPropertyNamesC217Tests
{
    private static uint Id(string fourCc) => (uint)new RecordType(fourCc).TypeInt;

    [Fact]
    public void RenamedMembersKeepTheirIds()
    {
        ((uint)Weapon.Property.CantDrop).ShouldBe(Id("WNDP"));
        ((uint)Weapon.Property.AttackDamage).ShouldBe(Id("WDMG"));
        ((uint)Weapon.Property.PowerActorValue).ShouldBe(Id("PAVI"));
        ((uint)Weapon.Property.PowerBonusActorValue).ShouldBe(Id("PBAV"));
        ((uint)Weapon.Property.EnableMarkingTargets).ShouldBe(Id("WEMT"));
        ((uint)Weapon.Property.HasScope).ShouldBe(Id("WSCP"));
        ((uint)Weapon.Property.RepeatableFire).ShouldBe(Id("FREP"));
        ((uint)Weapon.Property.AimOpticalSightModel).ShouldBe(Id("WAOS"));
        ((uint)Weapon.Property.AimDownSightTemplate).ShouldBe(Id("ZDTA"));
        ((uint)Weapon.Property.AimModelConeDecreaseDelaySec).ShouldBe(Id("WCO5"));
        ((uint)Npc.Property.XPOffset).ShouldBe(Id("NXPO"));
        ((uint)Npc.Property.HeightMinMax).ShouldBe(Id("NMMX"));
        ((uint)Armor.Property.DamageTypeValues).ShouldBe(Id("ADMG"));
        ((uint)SpaceshipModification.Property.DamageChanceActorValue).ShouldBe(Id("SDMG"));
        ((uint)SpaceshipModification.Property.PowerActorValue).ShouldBe(Id("SPAV"));
    }

    [Fact]
    public void OldNamesAreGone()
    {
        Enum.GetNames<Weapon.Property>().ShouldNotContain("DamagePhysical");
        Enum.GetNames<Weapon.Property>().ShouldNotContain("Can");
        Enum.GetNames<Armor.Property>().ShouldNotContain("DamageResistance");
        Enum.GetNames<SpaceshipModification.Property>().ShouldNotContain("SystemDamage");
        Enum.GetNames<Npc.Property>().ShouldNotContain("XPOverride");
    }
}
