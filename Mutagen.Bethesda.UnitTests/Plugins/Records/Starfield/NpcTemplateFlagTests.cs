using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield's NPC "Use Template Actors" flags (ACBS) go up to Reaction Radius 0x2000 and Combat Style 0x4000
// (xEdit's Starfield definitions); Starfield.esm sets them on 2,825 and 4,253 NPCs. They are a set of flags.
public class NpcTemplateFlagTests
{
    [Fact]
    public void ReactionRadiusAndCombatStyle_Read()
    {
        // LC074_Creature_Boss (NPC_ 3FC7C4): template flags 0x7D76
        var bytes = Record("NPC_", Subrecord("ACBS", "00-00-00-00-05-00-01-00-00-00-00-00-23-00-76-7D"));
        foreach (var npc in ReadBoth<INpcGetter>(bytes, f => Npc.CreateFromBinary(f), (s, p) => NpcBinaryOverlay.NpcFactory(s, p)))
        {
            npc.UseTemplateActors.ShouldBe(
                Npc.TemplateActorType.Stats | Npc.TemplateActorType.Factions | Npc.TemplateActorType.AiData
                | Npc.TemplateActorType.AiPackages | Npc.TemplateActorType.ModelOrAnimation
                | Npc.TemplateActorType.Inventory | Npc.TemplateActorType.DefPackList | Npc.TemplateActorType.AttackData
                | Npc.TemplateActorType.Keywords | Npc.TemplateActorType.ReactionRadius | Npc.TemplateActorType.CombatStyle);
            npc.UseTemplateActors.HasFlag(Npc.TemplateActorType.Traits).ShouldBeFalse();
        }
    }

    [Fact]
    public void TemplateActorType_IsAFlagSet()
    {
        typeof(Npc.TemplateActorType).IsDefined(typeof(FlagsAttribute), false).ShouldBeTrue();
        (Npc.TemplateActorType.Keywords | Npc.TemplateActorType.CombatStyle).ToString().ShouldBe("Keywords, CombatStyle");
    }

    [Fact]
    public void ReactionRadiusAndCombatStyle_Write()
    {
        var npc = new Npc(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            UseTemplateActors = Npc.TemplateActorType.ReactionRadius | Npc.TemplateActorType.CombatStyle,
        };
        var written = GameConstants.Starfield.MajorRecord(Write(w => npc.WriteToBinary(w)));
        BitConverter.ToUInt16(written.FindSubrecord(new RecordType("ACBS")).Content.Slice(14, 2)).ShouldBe((ushort)0x6000);
    }
}
