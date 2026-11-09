using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A placed object's ship arrival (XSAD, 17 bytes) was raw bytes. xEdit: Target (a placed reference, so a link that must
// be remapped), Location (u32 enum: Editor Location, Target - Above/Below/Behind/Front/Left/Right), Distance (m),
// Delay (s), GravJump FX (u8 bool). Starfield.esm, 12 references: Target null, Location 0, Distance 0, Delay 0/3/4,
// GravJump FX 1 on all.
public class ShipArrivalTests
{
    // REFR 29D67F (Starfield.esm) of QDTempShipNEW (20BA52:Starfield.esm): NAME, XSAD, DATA
    private const string Ref29D67F = "4E414D45040052BA20005853414411000000000000000000000000000000000001444154411800FF2BB3C275895644FADC09410000000000000080E4CB3640";

    // XSAD of REFR 0011F0 (Starfield.esm) of MQ101_CrimsonFleet_Ship (229424:Starfield.esm): Delay 4 s
    private const string Delay4 = "00-00-00-00-00-00-00-00-00-00-00-00-00-00-80-40-01";

    private static IEnumerable<IPlacedObjectGetter> Read(byte[] bytes)
        => ReadBoth<IPlacedObjectGetter>(bytes, f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p));

    [Fact]
    public void WholeRecord()
    {
        foreach (var refr in Read(Record("REFR", Hex(Ref29D67F))))
        {
            var arrival = refr.ShipArrival!;
            arrival.Target.IsNull.ShouldBeTrue();
            arrival.Location.ShouldBe(PlacedObjectShipArrival.LocationType.EditorLocation);
            arrival.Distance.ShouldBe(0f);
            arrival.Delay.ShouldBe(0f);
            arrival.GravJumpFx.ShouldBeTrue();
            var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
            // Only XSAD: the DATA rotation holds a -0.0 that Mutagen writes as 0 (not part of this change).
            written.FindSubrecord(new RecordType("XSAD")).Content.ToArray().ShouldBe(Hex(Ref29D67F).AsSpan(16, 17).ToArray());
        }
    }

    [Fact]
    public void Delay()
    {
        foreach (var refr in Read(Record("REFR", Subrecord("XSAD", Delay4))))
        {
            refr.ShipArrival!.Delay.ShouldBe(4f);
            var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("XSAD")).Content.ToArray().ShouldBe(Hex(Delay4));
        }
    }

    [Fact]
    public void Target_IsALink()
    {
        // Target set to a placed reference, Location Target - Behind (3), Distance 100: the link is enumerated and written
        var refr = new PlacedObject(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            ShipArrival = new PlacedObjectShipArrival
            {
                Location = PlacedObjectShipArrival.LocationType.TargetBehind,
                Distance = 100f,
            }
        };
        var target = new FormKey(TestModKey, 0x801);
        refr.ShipArrival.Target.SetTo(target);
        refr.EnumerateFormLinks().Select(l => l.FormKey).ShouldContain(target);
        var written = GameConstants.Starfield.MajorRecord(Write(w => ((IPlacedObjectGetter)refr).WriteToBinary(w)));
        written.FindSubrecord(new RecordType("XSAD")).Content.ToArray().ShouldBe(Hex("01-08-00-00-03-00-00-00-00-00-C8-42-00-00-00-00-00"));
    }
}
