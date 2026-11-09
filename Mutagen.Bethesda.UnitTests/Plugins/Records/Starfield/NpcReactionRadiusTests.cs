using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// NPC reaction radius behavior (RDSA), per xEdit. The Specific Reference (a placed reference) was read as a plain Int32,
// so FormID remapping missed it (17 set in Starfield.esm), and the other links were untyped. Cool Down Timer is a float
// (it was an Int32).
public class NpcReactionRadiusTests
{
    // RDSA of LC115_LvlSyndicate_Trespass (NPC_ 28FDCD): 2 entries, both with Specific Reference bomani_bridgeMarker01
    // (REFR 273F5D) and Linked Ref Keyword LinkReactionPrimitive (KYWD 208087)
    private const string Rdsa =
        "05-00-00-00-00-00-00-00-00-00-00-00-60-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-40-87-80-20-00-5D-3F-27-00-00-00-00-00-00-00-80-3F-00-00-00-00-00-00-00-00-02-00-00-00-00-00-00-00-00-00-00-00-60-80-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-40-87-80-20-00-5D-3F-27-00-00-00-00-00-00-00-80-3F-00-00-00-00-00-00-00-00";

    private static IEnumerable<INpcGetter> ReadNpc() =>
        ReadBoth<INpcGetter>(Record("NPC_", Subrecord("RDSA", Rdsa)), f => Npc.CreateFromBinary(f), (s, p) => NpcBinaryOverlay.NpcFactory(s, p));

    [Fact]
    public void Entries_Read()
    {
        foreach (var npc in ReadNpc())
        {
            npc.RDSAs!.Count.ShouldBe(2);
            var first = npc.RDSAs[0];
            first.ReactionType.ShouldBe(Npc.ReactionTypeOption.WarnAndAlarm);
            first.ReactionTarget.ShouldBe(Npc.ReactionTargetOption.Player);
            first.Flags.ShouldBe(Npc.ReactionRadiusFlag.ReactionCenterOverride | Npc.ReactionRadiusFlag.UseLinkedReference);
            first.NumberOfWarningsBeforeAttack.ShouldBe(2f);
            first.LinkedRefKeyword.FormKey.ID.ShouldBe(0x208087u);
            first.SpecificReference.FormKey.ID.ShouldBe(0x273F5Du);
            first.WarnTimerOnWarnAttack.ShouldBe(1f);
            first.CoolDownTimer.ShouldBe(0f);
            var second = npc.RDSAs[1];
            second.ReactionType.ShouldBe(Npc.ReactionTypeOption.Attack);
            second.Flags.HasFlag(Npc.ReactionRadiusFlag.DisallowThreatBackdown).ShouldBeTrue();
            npc.EnumerateFormLinks().Select(l => l.FormKey.ID).ShouldContain(0x273F5Du);
        }
    }

    [Fact]
    public void Rdsa_WritesSameBytes()
    {
        foreach (var npc in ReadNpc())
        {
            // A whole NPC needs more subrecords (ACBS) to write, so the entries are written on their own.
            var bytes = npc.RDSAs!.SelectMany(e => Write(w => e.WriteToBinary(w))).ToArray();
            bytes.ShouldBe(Hex(Rdsa));
        }
    }
}
