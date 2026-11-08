using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield magic effect archetypes include 26 Summon Duplicate, 36 Command and 50-55 (Capacity Value
// Modifier, Reset Flora, Drop Resources, Antigravity, Track Damage, Gravity Wielder), per xEdit's Starfield
// definitions; 14 magic effects in Starfield.esm use them.
public class MagicEffectArchetypeTests
{
    // DATA of CrTerrormorphMindControlEffect_NPC_Test (MGEF 3E88DC) in Starfield.esm: archetype 36 = Command
    private const string MindControlData =
        "00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-A4-DF-02-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-64-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-C4-C8-13-00-00-00-00-00-00-00-00-00-00-00-00-00-00-3C-1C-46-24-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-00-00-00-01-02-00-00-00-00-01-18-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00";

    [Fact]
    public void Command_Reads()
    {
        var bytes = Record("MGEF", Subrecord("DATA", MindControlData));
        foreach (var effect in ReadBoth<IMagicEffectGetter>(bytes, f => MagicEffect.CreateFromBinary(f), (s, p) => MagicEffectBinaryOverlay.MagicEffectFactory(s, p)))
        {
            effect.Archetype.Type.ShouldBe(MagicEffectArchetype.TypeEnum.Command);
        }
    }

    [Theory]
    [InlineData(MagicEffectArchetype.TypeEnum.SummonDuplicate, 26)]
    [InlineData(MagicEffectArchetype.TypeEnum.Command, 36)]
    [InlineData(MagicEffectArchetype.TypeEnum.CapacityValueModifier, 50)]
    [InlineData(MagicEffectArchetype.TypeEnum.ResetFlora, 51)]
    [InlineData(MagicEffectArchetype.TypeEnum.DropResources, 52)]
    [InlineData(MagicEffectArchetype.TypeEnum.Antigravity, 53)]
    [InlineData(MagicEffectArchetype.TypeEnum.TrackDamage, 54)]
    [InlineData(MagicEffectArchetype.TypeEnum.GravityWielder, 55)]
    public void NewArchetypes_WriteAndReadBack(MagicEffectArchetype.TypeEnum type, int stored)
    {
        ((int)type).ShouldBe(stored);
        var effect = new MagicEffect(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            Archetype = new MagicEffectArchetype(type),
        };
        var bytes = Write(w => effect.WriteToBinary(w));
        foreach (var read in ReadBoth<IMagicEffectGetter>(bytes, f => MagicEffect.CreateFromBinary(f), (s, p) => MagicEffectBinaryOverlay.MagicEffectFactory(s, p)))
        {
            read.Archetype.Type.ShouldBe(type);
        }
    }
}
