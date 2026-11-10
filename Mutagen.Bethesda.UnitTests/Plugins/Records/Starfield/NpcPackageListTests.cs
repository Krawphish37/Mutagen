using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C62: NPC_ package list links the Creation Kit writes into mods (none in Starfield.esm): OCOR Observe Dead Body
// Override, GWOR Guard Warn Override, FCPL Follower Command, RCLR Follower Elevator, RPLD Sound Detection and RPLI
// Sight Detection Package List, all FLST links in xEdit, in xEdit's order around SPOR and ECOR. They weren't in
// Mutagen's Npc, so a strict read threw and a lenient one dropped them.
public class NpcPackageListTests
{
    private static readonly string[] Lists = { "SPOR", "OCOR", "GWOR", "ECOR", "FCPL", "RCLR", "RPLD", "RPLI" };

    [Fact]
    public void PackageLists_ReadAndWrittenBack()
    {
        var subs = new List<byte[]>
        {
            Subrecord("EDID", System.Text.Encoding.ASCII.GetBytes("TestNpc\0")),
            Subrecord("RNAM", "01080000"),
        };
        for (int i = 0; i < Lists.Length; i++)
        {
            subs.Add(Subrecord(Lists[i], BitConverter.GetBytes(0x810u + (uint)i)));
        }
        var bytes = Record("NPC_", subs.ToArray());
        foreach (var npc in ReadBoth<INpcGetter>(bytes, f => Npc.CreateFromBinary(f), (s, p) => NpcBinaryOverlay.NpcFactory(s, p)))
        {
            npc.SpectatorOverridePackageList.FormKey.ID.ShouldBe(0x810u);
            npc.ObserveDeadBodyOverridePackageList.FormKey.ID.ShouldBe(0x811u);
            npc.GuardWarnOverridePackageList.FormKey.ID.ShouldBe(0x812u);
            npc.CombatOverridePackageList.FormKey.ID.ShouldBe(0x813u);
            npc.FollowerCommandPackageList.FormKey.ID.ShouldBe(0x814u);
            npc.FollowerElevatorPackageList.FormKey.ID.ShouldBe(0x815u);
            npc.SoundDetectionPackageList.FormKey.ID.ShouldBe(0x816u);
            npc.SightDetectionPackageList.FormKey.ID.ShouldBe(0x817u);
            npc.SightDetectionPackageList.ShouldBeAssignableTo<IFormLinkGetter<IFormListGetter>>();
            npc.EnumerateFormLinks().Select(x => x.FormKey.ID).ShouldContain(0x817u);

            var written = GameConstants.Starfield.MajorRecord(Write(w => npc.WriteToBinary(w)));
            written.EnumerateSubrecords()
                .Where(s => Lists.Contains(s.RecordType.Type))
                .Select(s => $"{s.RecordType.Type} {Convert.ToHexString(s.Content)}")
                .ShouldBe(Lists.Select((x, i) => $"{x} {Convert.ToHexString(BitConverter.GetBytes(0x810u + (uint)i))}"));
        }
    }
}
