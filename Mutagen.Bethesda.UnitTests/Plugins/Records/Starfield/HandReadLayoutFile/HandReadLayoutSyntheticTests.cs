using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.HandReadLayoutFile;

// C186 step 2: the layout file's paths that Starfield.esm and the official plugins never use (six placed trap types,
// perk entry point functions 4/6/7/11/12 and two-float values, string object mod properties, container and unknown-target
// OMODs, VMAD object format 1, bool list properties), held against what Mutagen writes and reads back. Runs without game
// data, so the file is checked in every test run.
public class HandReadLayoutSyntheticTests
{
    private static readonly string[] Targets =
    [
        "RecordPARW/SubNAME/FormIDProjectile", "RecordPBEA/SubNAME/FormIDProjectile", "RecordPFLA/SubNAME/FormIDProjectile",
        "RecordPCON/SubNAME/FormIDProjectile", "RecordPBAR/SubNAME/FormIDProjectile", "RecordPMIS/SubNAME/FormIDProjectile",
        "RecordPGRE/SubNAME/FormIDProjectile", "RecordPHZD/SubNAME/FormIDHazard",
        "RecordPERK/SegmentsRanks/SegmentsEffects/AnyOrder/SubDATA/Switch/Case2/Switch/Case4",
        "RecordPERK/SegmentsRanks/SegmentsEffects/AnyOrder/SubDATA/Switch/Case2/Switch/Case12/ConstModification",
        "RecordPERK/SegmentsRanks/SegmentsEffects/AnyOrder/SubDATA/Switch/Case2/Switch/Case6/ConstNegative",
        "RecordPERK/SegmentsRanks/SegmentsEffects/AnyOrder/SubDATA/Switch/Case2/Switch/Case7/ConstNegative",
        "RecordPERK/SegmentsRanks/SegmentsEffects/AnyOrder/SubDATA/Switch/Case2/Switch/Case11",
        "RecordPERK/SegmentsRanks/SegmentsEffects/AnyOrder/SubEPFD/Switch/Case1,2,3/Switch/Case2/F32Value2",
        "RecordPERK/SegmentsRanks/SegmentsEffects/AnyOrder/SubEPFD/Switch/Case4/F32To",
        "RecordPERK/SegmentsRanks/SegmentsEffects/AnyOrder/SubEPFD/Switch/Case11/ZStringText",
        "Define/Switch/Case3/ZStringValue",
        "RecordOMOD/SubDATA/Switch/CaseContainer_InstanceData",
        "RecordOMOD/SubDATA/Switch/Default/ConstObjectModificationTargetName",
        "Define/Switch/Case1/FormIDObject",
        "Define/Switch/Case15/RepeatData/U8.",
    ];

    [Fact]
    public void PathsTheOfficialPluginsNeverUse_MatchWhatMutagenWrites()
    {
        var dir = Path.Combine(TestPathing.TempFolderPath, "HandReadLayoutSynthetic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var mod = new StarfieldMod(ModKey.FromNameAndExtension("HandReadLayoutSynthetic.esp"), StarfieldRelease.Starfield);
            var target = mod.GetNextFormKey();
            AddTraps(mod, target);
            AddPerk(mod, target);
            AddObjectMods(mod, target);
            AddScripts(mod, target);
            var path = new ModPath(mod.ModKey, Path.Combine(dir, mod.ModKey.FileName));
            mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();

            var result = HandReadLayoutFileTests.CheckPlugins(dir, [mod.ModKey.FileName]);
            result.Errors.ShouldBeEmpty(string.Join(Environment.NewLine, result.Errors.Take(40)));
            Targets.Where(t => !result.Checked.ContainsKey(t)).ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void AddTraps(StarfieldMod mod, FormKey target)
    {
        var cell = new Cell(mod) { EditorID = "TrapCell" };
        APlacedTrap[] traps =
        [
            new PlacedArrow(mod), new PlacedBeam(mod), new PlacedFlame(mod), new PlacedCone(mod), new PlacedBarrier(mod),
            new PlacedMissile(mod), new PlacedTrap(mod),
        ];
        foreach (var trap in traps)
        {
            ((IFormLink<IProjectileGetter>)trap.GetType().GetProperty("Projectile")!.GetValue(trap)!).SetTo(target);
            cell.Temporary.Add(trap);
        }
        var hazard = new PlacedHazard(mod);
        hazard.Hazard.SetTo(target);
        cell.Temporary.Add(hazard);
        mod.Cells.Add(new CellBlock
        {
            BlockNumber = 0,
            GroupType = GroupTypeEnum.InteriorCellBlock,
            SubBlocks = { new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = { cell } } },
        });
    }

    private static void AddPerk(StarfieldMod mod, FormKey target)
    {
        var rank = new PerkRank();
        var modifyActorValue = new PerkEntryPointModifyActorValue
        {
            Modification = PerkEntryPointModifyActorValue.ModificationType.SetToAVMult,
            Value = 2.5f,
        };
        modifyActorValue.ActorValue.SetTo(target);
        rank.Effects.AddRange(new APerkEffect[]
        {
            new PerkEntryPointAddRangeToValue { From = 1, To = 4 },
            modifyActorValue,
            new PerkEntryPointAbsoluteValue { Negative = false },
            new PerkEntryPointAbsoluteValue { Negative = true },
            new PerkEntryPointSelectText { Text = "Choose" },
            new PerkEntryPointModifyValues
            {
                Modification = PerkEntryPointModifyValue.ModificationType.Multiply,
                Value = 0.5f,
                Value2 = 1.5f,
            },
        });
        var perk = new Perk(mod) { EditorID = "LayoutPerk" };
        perk.Ranks.Add(rank);
        mod.Perks.Add(perk);
    }

    private static void AddObjectMods(StarfieldMod mod, FormKey target)
    {
        var container = new ContainerModification(mod) { EditorID = "LayoutContainerMod" };
        container.Properties.Add(new ObjectModStringProperty<Container.Property>
        {
            Property = Container.Property.Keyword,
            Value = "abc",
            Step = 1,
        });
        container.AttachPoint.SetTo(target);
        mod.ObjectModifications.Add(container);

        var unknown = new UnknownObjectModification(mod)
        {
            EditorID = "LayoutUnknownMod",
            ObjectModificationTargetName = "LayoutTest_InstanceData",
        };
        mod.ObjectModifications.Add(unknown);
    }

    private static void AddScripts(StarfieldMod mod, FormKey target)
    {
        var obj = new ScriptObjectProperty { Name = "Target", Alias = 3 };
        obj.Object.SetTo(target);
        var bools = new ScriptBoolListProperty { Name = "Switches" };
        bools.Data.AddRange([true, false, true]);
        var activator = new Mutagen.Bethesda.Starfield.Activator(mod)
        {
            EditorID = "LayoutActivator",
            VirtualMachineAdapter = new VirtualMachineAdapter
            {
                ObjectFormat = 1,
                Scripts =
                {
                    new ScriptEntry { Name = "LayoutScript", Properties = { obj, bools } },
                },
            },
        };
        mod.Activators.Add(activator);
    }
}
