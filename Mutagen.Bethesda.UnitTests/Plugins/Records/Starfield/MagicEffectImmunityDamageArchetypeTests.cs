using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using TempFile = Noggog.IO.TempFile;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C190: MGEF archetypes 45 (Damage) and 46 (Immunity) link a damage type (xEdit: DMGT for both). ReadArchetype had
// no case for them, so MagicEffectDamageTypeModArchetype / MagicEffectImmunityModArchetype were never created and the
// records read back as the generic archetype. Starfield.esm has 19 Damage and 3 Immunity effects.
public class MagicEffectImmunityDamageArchetypeTests
{
    public static TheoryData<MagicEffectArchetype.TypeEnum, bool> Cases() => new()
    {
        { MagicEffectArchetype.TypeEnum.Damage, true },
        { MagicEffectArchetype.TypeEnum.Damage, false },
        { MagicEffectArchetype.TypeEnum.Immunity, true },
        { MagicEffectArchetype.TypeEnum.Immunity, false },
    };

    // typed: written from the dedicated class; else from the generic archetype with the same type number
    [Theory]
    [MemberData(nameof(Cases))]
    public void ReadsAsItsOwnArchetype_BothReaders(MagicEffectArchetype.TypeEnum type, bool typed)
    {
        var mod = new StarfieldMod(ModKey.FromNameAndExtension("MgefArchetype.esp"), StarfieldRelease.Starfield);
        var damageType = new DamageType(mod) { EditorID = "TestDamageType" };
        mod.DamageTypes.Add(damageType);
        AMagicEffectArchetype archetype = (type, typed) switch
        {
            (MagicEffectArchetype.TypeEnum.Damage, true) => new MagicEffectDamageTypeModArchetype
            {
                Association = damageType.ToLink<IDamageTypeGetter>(),
            },
            (MagicEffectArchetype.TypeEnum.Immunity, true) => new MagicEffectImmunityModArchetype
            {
                Association = damageType.ToLink<IDamageTypeGetter>(),
            },
            _ => new MagicEffectArchetype(type) { Association = damageType.ToLink<IStarfieldMajorRecordGetter>() },
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
            read.Archetype.AssociationKey.FormKey.ShouldBe(damageType.FormKey);
            if (type == MagicEffectArchetype.TypeEnum.Damage)
            {
                read.Archetype.ShouldBeOfType<MagicEffectDamageTypeModArchetype>()
                    .Association.FormKey.ShouldBe(damageType.FormKey);
            }
            else
            {
                read.Archetype.ShouldBeOfType<MagicEffectImmunityModArchetype>()
                    .Association.FormKey.ShouldBe(damageType.FormKey);
            }
        }

        // and writes back the same bytes
        using var tmp2 = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        full.BeginWrite.ToPath(new ModPath(mod.ModKey, tmp2.File.Path)).WithNoLoadOrder().NoModKeySync().Write();
        File.ReadAllBytes(tmp2.File.Path).ShouldBe(File.ReadAllBytes(tmp.File.Path));
    }
}
