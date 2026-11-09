using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Race attack data (ATKD), per xEdit's wbAttackData: the float at offset 20 is Hit Cone (Mutagen called it StrikeAngle),
// and the last 4 bytes are the float Strike Angle (Mutagen read them as an Int32 `Unknown`, so 135.0 read as 1124532224).
public class AttackDataStrikeAngleTests
{
    // A race writes its 64 biped object names (NAME) and their conditions (RBPC); the binary overlay needs them present.
    private static byte[][] BipedObjectNames()
        => Enumerable.Range(0, 64).Select(_ => Subrecord("NAME", "00")).Append(Subrecord("RBPC", new byte[256])).ToArray();

    // An ATKD of CCT_DummyRace (1BB965:Starfield.esm): Attack Angle 135, Hit Cone 90, AP Mult 2, Strike Angle 135
    private const string Atkd = "0000803F0000803F0000000000000000000007430000B442000000000000000000000000000000400000000000000743";

    [Fact]
    public void StrikeAngle_IsAFloat_HitConeNamed()
    {
        var bytes = Record("RACE", [Subrecord("ATKD", Atkd), .. BipedObjectNames()]);
        foreach (var race in ReadBoth<IRaceGetter>(bytes, f => Race.CreateFromBinary(f), (s, p) => RaceBinaryOverlay.RaceFactory(s, p)))
        {
            var data = race.Attacks.Single().AttackData!;
            data.DamageMult.ShouldBe(1f);
            data.AttackAngle.ShouldBe(135f);
            data.HitCone.ShouldBe(90f);
            data.ActionPointsMult.ShouldBe(2f);
            data.StaggerOffset.ShouldBe(0);
            data.StrikeAngle.ShouldBe(135f);
            var written = GameConstants.Starfield.MajorRecord(Write(w => race.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("ATKD")).Content.ToArray().ShouldBe(Hex(Atkd));
        }
    }
}
