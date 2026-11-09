using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// SPED (movement data, 252 bytes) was raw bytes on both MovementType and Race's movement data overrides. It is xEdit's
// wbSPED, one shared MovementData: max speeds forward/left/right/back/up/down (7 floats each) and max rotation speeds
// pitch/roll/yaw (7 floats each, radians as stored). MovementType's KNAM is Fast Walk, INTV the Clamp Max Animation
// Speeds flag (+3 unused bytes), BOLV Display For Non Characters.
public class MovementDataTests
{
    // A race writes its 64 biped object names (NAME) and their conditions (RBPC); the binary overlay needs them present.
    private static byte[][] BipedObjectNames()
        => Enumerable.Range(0, 64).Select(_ => Subrecord("NAME", "00")).Append(Subrecord("RBPC", new byte[256])).ToArray();

    // Human_Default_MT (003482:Starfield.esm)
    private const string Human = "45444944110048756D616E5F44656661756C745F4D54004D4E414D0D0048756D616E44656661756C740053504544FC00000000001F858B3F48E142400000A0406666F6400000000000000000000000001F858B3F48E142400000A040000000000000000000000000000000001F858B3F48E142400000A040000000000000000000000000000000001F858B3F15AE2F40CDCC8C40000000000000000000000000000000000000A0400000A0400000A040000000000000803F0000803F000000000000A0400000A0400000A040000000000000803F0000803FDB0F4940DB0F4940DB0F4940DB0F4940DB0F49400000000000000000DB0F4940DB0F4940DB0F4940DB0F4940DB0F49400000000000000000DB0F4940DB0F4940DB0F4940DB0F4940DB0F494000000000000000004C4E414D0400CDCCCC3D4B4E414D04001F858B3F494E5456040000000000424F4C560000";

    // Spaceship_Default (008E74:Starfield.esm)
    private const string Spaceship = "4544494412005370616365736869705F44656661756C74004D4E414D110053706163657368697044656661756C740053504544FC00000000000000403FAE47613F0000803F00004842000034420000A04100000000CDCCCC3D0000003F0000803F00000000000034420000A04000000000CDCCCC3D0000003F0000803F00000000000034420000A04000000000CDCCCC3D0000003F0000803F00000000000070410000E04000000000CDCCCC3D0000003F0000803F00000000000034420000A04000000000CDCCCC3D0000003F0000803F00000000000034420000A040A9619C3F34FA8E3CC1B8323D34FA8E3DA9619C3FA9619C3F00000000A9619C3F34FA8E3CC1B8323D34FA8E3DA9619C3F920A864000000000A9619C3F34FA8E3CC1B8323D34FA8E3DA9619C3FA9619C3F000000004C4E414D0400CDCCCC3D4B4E414D0400CDCC8C3F494E5456040000000000424F4C560000";

    // MTYP + SPED of LarvaARace (29A905:Starfield.esm), the only race with a movement data override
    private const string LarvaMtyp = "E7770100";
    private const string LarvaSped = "000000000AD7A33D52B81E3E1F856B3E1F856B3E00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000803F0000803F0000803F0000803F00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000DB0F4940DB0F4940DB0F4940DB0F49400000000000000000";

    private static IEnumerable<IMovementTypeGetter> Read(string hex)
        => ReadBoth<IMovementTypeGetter>(Record("MOVT", Hex(hex)), f => MovementType.CreateFromBinary(f), (s, p) => MovementTypeBinaryOverlay.MovementTypeFactory(s, p));

    private static void WritesBack(IMovementTypeGetter movt, string hex)
    {
        var written = GameConstants.Starfield.MajorRecord(Write(w => movt.WriteToBinary(w)));
        written.Content.ToArray().ShouldBe(Hex(hex));
    }

    [Fact]
    public void Spaceship_MovementData()
    {
        foreach (var movt in Read(Spaceship))
        {
            var sped = movt.MovementData!;
            sped.MaxSpeedForward.Walk.ShouldBe(0.75f);
            sped.MaxSpeedForward.Run.ShouldBe(1f);
            sped.MaxSpeedForward.Sprint.ShouldBe(50f);
            sped.MaxSpeedForward.Acceleration.ShouldBe(45f);
            sped.MaxSpeedForward.Deceleration.ShouldBe(20f);
            sped.MaxSpeedBack.Acceleration.ShouldBe(15f);
            sped.MaxSpeedBack.Deceleration.ShouldBe(7f);
            sped.MaxRotationSpeedYaw.Stand.ShouldBe(1.2217305f, 0.000001f);
            sped.MaxRotationSpeedRoll.Acceleration.ShouldBe(4.1887903f, 0.000001f);
            sped.MaxRotationSpeedRoll.Unused.ShouldBe(0f);
            movt.FastWalk.ShouldBe(1.1f);
            movt.ClampMaxAnimationSpeeds.ShouldBeFalse();
            movt.DisplayForNonCharacters.ShouldBeTrue();
            WritesBack(movt, Spaceship);
        }
    }

    [Fact]
    public void Human_WholeRecord()
    {
        foreach (var movt in Read(Human))
        {
            movt.MovementData.ShouldNotBeNull();
            movt.FastWalk.ShouldBe(1.09f);
            movt.DisplayForNonCharacters.ShouldBeTrue();
            WritesBack(movt, Human);
        }
    }

    [Fact]
    public void Race_MovementDataOverride()
    {
        var bytes = Record("RACE", [.. BipedObjectNames(), Subrecord("MTYP", LarvaMtyp), Subrecord("SPED", LarvaSped)]);
        foreach (var race in ReadBoth<IRaceGetter>(bytes, f => Race.CreateFromBinary(f), (s, p) => RaceBinaryOverlay.RaceFactory(s, p)))
        {
            var o = race.MovementDataOverrides.Single();
            o.MovementType.FormKey.ID.ShouldBe(0x0177E7u);
            o.MovementData!.MaxSpeedForward.Walk.ShouldBe(0.08f);
            o.MovementData.MaxSpeedBack.Walk.ShouldBe(1f);
            o.MovementData.MaxRotationSpeedYaw.Walk.ShouldBe(MathF.PI);
            var written = GameConstants.Starfield.MajorRecord(Write(w => race.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("SPED")).Content.ToArray().ShouldBe(Hex(LarvaSped));
            written.FindSubrecord(new RecordType("MTYP")).Content.ToArray().ShouldBe(Hex(LarvaMtyp));
        }
    }
}
