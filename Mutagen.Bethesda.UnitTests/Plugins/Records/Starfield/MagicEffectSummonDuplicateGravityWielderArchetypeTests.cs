using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using TempFile = Noggog.IO.TempFile;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C194: MGEF archetypes 26 (Summon Duplicate) and 55 (Gravity Wielder) had no class, so they read as the generic
// archetype, whose association links any record. xEdit (wbMGEFAssocItemDecider1): 26 links a spell (SPEL), 55 a gravity
// wielder effect data (GWED). Starfield.esm has 4 Summon Duplicate effects (ArtifactPowerParallelSelf_Effect 2C5A6A),
// SFBGS00D.esm 1 of each (ModLegendaryEffect_Gravitational_TelekenesisEffect 08C99D).
public class MagicEffectSummonDuplicateGravityWielderArchetypeTests
{
    public static TheoryData<MagicEffectArchetype.TypeEnum, bool> Cases() => new()
    {
        { MagicEffectArchetype.TypeEnum.SummonDuplicate, true },
        { MagicEffectArchetype.TypeEnum.SummonDuplicate, false },
        { MagicEffectArchetype.TypeEnum.GravityWielder, true },
        { MagicEffectArchetype.TypeEnum.GravityWielder, false },
    };

    // typed: written from the dedicated class; else from the generic archetype with the same type number
    [Theory]
    [MemberData(nameof(Cases))]
    public void ReadsAsItsOwnArchetype_BothReaders(MagicEffectArchetype.TypeEnum type, bool typed)
    {
        var mod = new StarfieldMod(ModKey.FromNameAndExtension("MgefArchetype.esp"), StarfieldRelease.Starfield);
        var spell = new Spell(mod) { EditorID = "TestDuplicateSpell" };
        mod.Spells.Add(spell);
        var gravity = new GravityWielderEffectData(mod) { EditorID = "TestGravityWielder" };
        mod.GravityWielderEffectDatas.Add(gravity);
        var target = type == MagicEffectArchetype.TypeEnum.SummonDuplicate ? spell.FormKey : gravity.FormKey;
        AMagicEffectArchetype archetype = (type, typed) switch
        {
            (MagicEffectArchetype.TypeEnum.SummonDuplicate, true) => new MagicEffectSummonDuplicateArchetype
            {
                Association = spell.ToLink<ISpellGetter>(),
            },
            (MagicEffectArchetype.TypeEnum.GravityWielder, true) => new MagicEffectGravityWielderArchetype
            {
                Association = gravity.ToLink<IGravityWielderEffectDataGetter>(),
            },
            _ => new MagicEffectArchetype(type) { Association = new FormLink<IStarfieldMajorRecordGetter>(target) },
        };
        var effect = new MagicEffect(mod) { EditorID = "TestEffect", Archetype = archetype };
        mod.MagicEffects.Add(effect);

        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var path = new ModPath(mod.ModKey, tmp.File.Path);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();

        var full = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield);
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield);
        foreach (var read in new[] { full.MagicEffects.Records.Single(), overlay.MagicEffects.Records.Single() })
        {
            read.Archetype.Type.ShouldBe(type);
            read.Archetype.AssociationKey.FormKey.ShouldBe(target);
            if (type == MagicEffectArchetype.TypeEnum.SummonDuplicate)
            {
                var link = read.Archetype.ShouldBeOfType<MagicEffectSummonDuplicateArchetype>().Association;
                link.FormKey.ShouldBe(spell.FormKey);
                read.EnumerateFormLinks().ShouldContain(l => l.FormKey == spell.FormKey && l.Type == typeof(ISpellGetter));
            }
            else
            {
                var link = read.Archetype.ShouldBeOfType<MagicEffectGravityWielderArchetype>().Association;
                link.FormKey.ShouldBe(gravity.FormKey);
                read.EnumerateFormLinks().ShouldContain(l => l.FormKey == gravity.FormKey && l.Type == typeof(IGravityWielderEffectDataGetter));
            }
        }

        // and writes back the same bytes
        using var tmp2 = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        full.BeginWrite.ToPath(new ModPath(mod.ModKey, tmp2.File.Path)).WithNoLoadOrder().NoModKeySync().Write();
        File.ReadAllBytes(tmp2.File.Path).ShouldBe(File.ReadAllBytes(tmp.File.Path));
    }
}
