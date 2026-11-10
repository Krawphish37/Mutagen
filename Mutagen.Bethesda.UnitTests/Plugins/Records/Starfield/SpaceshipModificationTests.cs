using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// OMOD whose DATA target is "Spaceship_InstanceData" (3 in Starfield.esm) had no class and was read as an
// UnknownObjectModification with property names of the empty NoneProperty enum. They are now SpaceshipModification,
// with xEdit's Spaceship property names (SPAV Actor Value, SDMG System Damage, ...).
// The records are from Starfield.esm minus FULL and DESC (localized string IDs this unlocalized test reader would read
// as text); both readers pick the class from the target name, and the whole record is written back byte for byte.
public class SpaceshipModificationTests
{
    // SpaceshipEnergyGatlingWeaponMod (018DE6:Starfield.esm): attach point ap_WeaponTypeA (249FEA:Starfield.esm),
    // SDMG ADD ShipSystemDamageWeightWeaponSlotA (011586:Starfield.esm) 0, SPAV ADD (no actor value) 2.0
    private const string Gatling =
        "454449442000537061636573686970456E657267794761746C696E67576561706F6E4D6F6400" +
        "4D4F444C260053686970735C446973636F766572795C50617274735C576561706F6E54797065412E6E696600" +
        "464C4C4404000100000044415441630000000000020000000000170000005370616365736869705F496E7374616E636544617461000000EA9F24000000000000000000040000000200000053444D47861501000000000000000000060000000200000053504156000000000000004000000000";

    // BuildableSpaceshipGravJumpMod (01D618:Starfield.esm): attach point ap_GravDrive (008226:Starfield.esm),
    // SDMG ADD ShipSystemDamageWeightGravDrive (01158B:Starfield.esm) 0, SPAV ADD SpaceshipGravJumpMaxPower (008223:Starfield.esm) 3.0
    private const string GravJump =
        "454449441E004275696C6461626C65537061636573686970477261764A756D704D6F6400" +
        "464C4C4404000100000044415441630000000000020000000000170000005370616365736869705F496E7374616E636544617461000000268200000000000000000000040000000200000053444D478B1501000000000000000000060000000200000053504156238200000000404000000000";

    // BuildableSpaceshipThrusterMod (00ACEA:Starfield.esm): attach point ap_Thrusters (001016:Starfield.esm), no properties
    private const string Thruster =
        "454449441E004275696C6461626C6553706163657368697054687275737465724D6F6400" +
        "464C4C4404000100000044415441330000000000000000000000170000005370616365736869705F496E7374616E636544617461000000161000000000000000000000";

    // bot_ModelA_Viator (137046:Starfield.esm): target " NONE", already ObjectModification; checked so it stays that way
    private const string None =
        "454449441200626F745F4D6F64656C415F566961746F7200" +
        "464C4C440400010000004441544122000000000000000000000006000000204E4F4E45000000000000000000000000000000";

    private static IEnumerable<IAObjectModificationGetter> Read(byte[] bytes) => ReadBoth<IAObjectModificationGetter>(
        bytes,
        f => AObjectModification.CreateFromBinary(f, default),
        (s, p) => AObjectModificationBinaryOverlay.AObjectModificationFactory(s, p, default));

    [Fact]
    public void Gatling_ReadsSpaceshipProperties()
    {
        var bytes = Record("OMOD", Hex(Gatling));
        foreach (var omod in Read(bytes))
        {
            var ship = omod.ShouldBeAssignableTo<ISpaceshipModificationGetter>()!;
            ship.EditorID.ShouldBe("SpaceshipEnergyGatlingWeaponMod");
            ship.AttachPoint.FormKey.ID.ShouldBe(0x249FEAu);
            ship.Properties.Count.ShouldBe(2);
            var damage = ship.Properties[0].ShouldBeAssignableTo<IObjectModFormLinkIntPropertyGetter<SpaceshipModification.Property>>()!;
            damage.Property.ShouldBe(SpaceshipModification.Property.SystemDamage);
            damage.FunctionType.ShouldBe(ObjectModProperty.FormLinkFunctionType.Add);
            damage.Record.FormKey.ID.ShouldBe(0x011586u);
            damage.Value.ShouldBe(0u);
            var actorValue = ship.Properties[1].ShouldBeAssignableTo<IObjectModFormLinkFloatPropertyGetter<SpaceshipModification.Property>>()!;
            actorValue.Property.ShouldBe(SpaceshipModification.Property.ActorValue);
            actorValue.FunctionType.ShouldBe(ObjectModProperty.FloatFunctionType.Add);
            actorValue.Record.IsNull.ShouldBeTrue();
            actorValue.Value.ShouldBe(2f);
            Write(w => omod.WriteToBinary(w)).ShouldBe(bytes);
        }
    }

    [Fact]
    public void GravJump_ReadsSpaceshipProperties()
    {
        var bytes = Record("OMOD", Hex(GravJump));
        foreach (var omod in Read(bytes))
        {
            var ship = omod.ShouldBeAssignableTo<ISpaceshipModificationGetter>()!;
            ship.AttachPoint.FormKey.ID.ShouldBe(0x008226u);
            ship.Properties.Select(p => p.Property).ShouldBe(new[]
            {
                SpaceshipModification.Property.SystemDamage,
                SpaceshipModification.Property.ActorValue,
            });
            var actorValue = ship.Properties[1].ShouldBeAssignableTo<IObjectModFormLinkFloatPropertyGetter<SpaceshipModification.Property>>()!;
            actorValue.Record.FormKey.ID.ShouldBe(0x008223u);
            actorValue.Value.ShouldBe(3f);
            Write(w => omod.WriteToBinary(w)).ShouldBe(bytes);
        }
    }

    [Fact]
    public void Thruster_NoProperties()
    {
        var bytes = Record("OMOD", Hex(Thruster));
        foreach (var omod in Read(bytes))
        {
            var ship = omod.ShouldBeAssignableTo<ISpaceshipModificationGetter>()!;
            ship.AttachPoint.FormKey.ID.ShouldBe(0x001016u);
            ship.Properties.ShouldBeEmpty();
            Write(w => omod.WriteToBinary(w)).ShouldBe(bytes);
        }
    }

    [Fact]
    public void None_StaysObjectModification()
    {
        var bytes = Record("OMOD", Hex(None));
        foreach (var omod in Read(bytes))
        {
            omod.ShouldBeAssignableTo<IObjectModificationGetter>();
            omod.ShouldNotBeAssignableTo<IUnknownObjectModificationGetter>();
            Write(w => omod.WriteToBinary(w)).ShouldBe(bytes);
        }
    }

    [Fact]
    public void NewSpaceshipModification_WritesTargetName()
    {
        var omod = new SpaceshipModification(new Mutagen.Bethesda.Plugins.FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            EditorID = "TestShipMod",
        };
        omod.Properties.Add(new ObjectModFormLinkFloatProperty<SpaceshipModification.Property>()
        {
            Property = SpaceshipModification.Property.ActorValue,
            FunctionType = ObjectModProperty.FloatFunctionType.Add,
            Value = 2f,
        });
        var written = Write(w => omod.WriteToBinary(w));
        foreach (var reread in Read(written))
        {
            var ship = reread.ShouldBeAssignableTo<ISpaceshipModificationGetter>()!;
            ship.Properties.ShouldHaveSingleItem().Property.ShouldBe(SpaceshipModification.Property.ActorValue);
        }
    }
}
