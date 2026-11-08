using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A footstep set's XCNT counts are Walking, Running, Sprinting, Sneaking, Swimming, but DATA stores the lists the
// other way round: Swimming, Sneaking, Sprinting, Running, Walking (xEdit). Mutagen read DATA in count order, so
// e.g. FSTBarefootFootstepSet's Walk list got the 2 swimming and 5 of the sneaking footsteps.
public class FootstepSetListOrderTests
{
    // XCNT and DATA of FSTBarefootFootstepSet (FSTS 08F4F2): Walk 7, Run 6, Sprint 6, Sneak 6, Swim 2
    private const string Counts = "07-00-00-00-06-00-00-00-06-00-00-00-06-00-00-00-02-00-00-00";
    private const string Data =
        "15-2F-01-00-89-A2-05-00-67-88-0D-00-37-BA-11-00-36-BA-11-00-15-F5-08-00-7A-66-0A-00-77-46-21-00-67-88-0D-00-EF-41-0E-00-EE-41-0E-00-7A-66-0A-00-15-F5-08-00-77-46-21-00-67-88-0D-00-15-F5-08-00-7A-66-0A-00-ED-41-0E-00-EC-41-0E-00-77-46-21-00-15-F5-08-00-7A-66-0A-00-67-14-02-00-66-14-02-00-67-88-0D-00-77-46-21-00-3E-8F-06-00";

    private static IEnumerable<IFootstepSetGetter> ReadSet() =>
        ReadBoth<IFootstepSetGetter>(Record("FSTS", Subrecord("XCNT", Counts), Subrecord("DATA", Data)), f => FootstepSet.CreateFromBinary(f), (s, p) => FootstepSetBinaryOverlay.FootstepSetFactory(s, p));

    private static uint[] Ids(IReadOnlyList<IFormLinkGetter<IFootstepGetter>> list) => list.Select(l => l.FormKey.ID).ToArray();

    [Fact]
    public void Lists_ReadInStoredOrder()
    {
        foreach (var set in ReadSet())
        {
            Ids(set.SwimFootsteps).ShouldBe(new uint[] { 0x012F15, 0x05A289 }); // FSTDefaultFootstepJumpDownFootstep ...
            Ids(set.SneakFootsteps).ShouldContain(0x11BA37u); // FSTBarefootSneakRFootstep
            Ids(set.SneakFootsteps).ShouldContain(0x11BA36u); // FSTBarefootSneakLFootstep
            Ids(set.SprintFootsteps).ShouldContain(0x0E41EFu); // FSTBarefootSprintRFootstep
            Ids(set.RunFootsteps).ShouldContain(0x0E41EDu); // FSTBarefootRunRFootstep
            Ids(set.WalkFootsteps).ShouldBe(new uint[] { 0x08F515, 0x0A667A, 0x021467, 0x021466, 0x0D8867, 0x214677, 0x068F3E }); // ... WalkR, WalkL ...
            set.SneakFootsteps.Count.ShouldBe(6);
            set.SprintFootsteps.Count.ShouldBe(6);
            set.RunFootsteps.Count.ShouldBe(6);
        }
    }

    [Fact]
    public void Lists_WriteSameBytes()
    {
        foreach (var set in ReadSet())
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => set.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("XCNT")).Content.ToArray().ShouldBe(Hex(Counts));
            written.FindSubrecord(new RecordType("DATA")).Content.ToArray().ShouldBe(Hex(Data));
        }
    }

    [Fact]
    public void Swim_WritesFirst()
    {
        var set = new FootstepSet(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield);
        set.WalkFootsteps.Add(new FormKey(TestModKey, 0x111));
        set.SwimFootsteps.Add(new FormKey(TestModKey, 0x555));
        var written = GameConstants.Starfield.MajorRecord(Write(w => set.WriteToBinary(w)));
        var data = written.FindSubrecord(new RecordType("DATA")).Content;
        (BitConverter.ToUInt32(data.Slice(0, 4)) & 0xFFFFFF).ShouldBe(0x555u);
        (BitConverter.ToUInt32(data.Slice(4, 4)) & 0xFFFFFF).ShouldBe(0x111u);
    }
}
