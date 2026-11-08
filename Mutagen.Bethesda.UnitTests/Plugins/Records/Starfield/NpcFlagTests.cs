using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield NPC flags (ACBS) bit 25 is Forced Starts Dead and bit 30 Ignore Friendly Hits (xEdit's Starfield
// definitions). Starfield.esm sets them on 344 and 287 NPCs.
public class NpcFlagTests
{
    private static IEnumerable<INpcGetter> ReadNpc(string acbs) =>
        ReadBoth<INpcGetter>(Record("NPC_", Subrecord("ACBS", acbs)),
            f => Npc.CreateFromBinary(f),
            (s, p) => NpcBinaryOverlay.NpcFactory(s, p));

    [Fact]
    public void ForcedStartsDead_Reads()
    {
        // Loot_Corpse_RobotModelS (NPC_ 3E57A7): flags 0x02000008 = Respawn + Forced Starts Dead
        foreach (var npc in ReadNpc("08-00-00-02-00-00-01-00-00-00-00-00-23-00-81-00"))
        {
            npc.Flags.ShouldBe(Npc.Flag.Respawn | Npc.Flag.ForcedStartsDead);
        }
    }

    [Fact]
    public void IgnoreFriendlyHits_Reads()
    {
        // UC01_Mannequin_Male_Varuun01_Pose06 (NPC_ 3D9CAD): flags 0xE01100D2
        foreach (var npc in ReadNpc("D2-00-11-E0-00-00-E8-03-00-00-00-00-23-00-BE-52"))
        {
            npc.Flags.HasFlag(Npc.Flag.IgnoreFriendlyHits).ShouldBeTrue();
            npc.Flags.HasFlag(Npc.Flag.IsGhost).ShouldBeTrue();
            npc.Flags.HasFlag(Npc.Flag.Invulnerable).ShouldBeTrue();
            npc.Flags.HasFlag(Npc.Flag.ForcedStartsDead).ShouldBeFalse();
        }
    }

    [Theory]
    [InlineData(Npc.Flag.ForcedStartsDead, 0x0200_0000u)]
    [InlineData(Npc.Flag.IgnoreFriendlyHits, 0x4000_0000u)]
    public void Flag_Writes(Npc.Flag flag, uint expected)
    {
        var npc = new Npc(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield) { Flags = flag };
        var written = GameConstants.Starfield.MajorRecord(Write(w => npc.WriteToBinary(w)));
        BitConverter.ToUInt32(written.FindSubrecord(new RecordType("ACBS")).Content.Slice(0, 4)).ShouldBe(expected);
    }
}
