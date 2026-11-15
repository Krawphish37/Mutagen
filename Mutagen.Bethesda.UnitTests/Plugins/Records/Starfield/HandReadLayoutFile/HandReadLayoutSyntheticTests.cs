using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.HandReadLayoutFile;

// C186 step 2: the layout file's paths that Starfield.esm and the official plugins never use (six placed trap types,
// perk entry point functions 4/6/7/11/12 and two-float values, string object mod properties, container and unknown-target
// OMODs, VMAD object format 1, bool list properties; C47: a package's topic data input with a topic subtype, the four
// instance naming rule classes Data lacks, a navigation map info's CRCs, volume enders of type 11), held against what
// Mutagen writes and reads back. Runs without game data, so the file is checked in every test run.
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
        // C47: hand-read lists the official plugins never use
        "RecordPACK/SegmentsData/SubANAM/Switch/CaseTopic",
        "RecordPACK/SegmentsData/keyListProcedureTreeDataOrder",
        "RecordPACK/SegmentsData/Switch/CaseTopic/SubTPIC/BytesTPIC",
        "Define/SubRepeatPDTO/Switch/Case1/I32Subtype.TypeInt",
        "RecordINNR/ObjectRules/Switch/Case0",
        "RecordINNR/ObjectRules/Switch/Case36",
        "RecordINNR/ObjectRules/Switch/Case46",
        "RecordINNR/ObjectRules/Switch/Case47",
        "RecordNAVI/SubRepeatNVMI/RepeatUnknownCrcs/U32.",
        "AnyRecordBFCB/SegmentsComponents/TypeSwitch/CaseVolumesComponent/SubVLMS/RepeatItems/ObjectEnder/Switch/Case11/TypeSwitch/CaseVolumesUnknownEnderEmpty",
        "AnyRecordBFCB/SegmentsComponents/TypeSwitch/CaseVolumesComponent/SubVLMS/RepeatItems/ObjectEnder/Switch/Case11/TypeSwitch/CaseVolumesUnknownEnderEmptyPadded/U32Unknown1",
        // C57: the record header of every record written, and the plugin header
        "Header/I32MajorRecordFlagsRaw", "Header/BitIsDeleted", "Header/BitIsCompressed", "Header/ViewStarfieldMajorRecordFlags",
        "Header/ViewMajorFlags", "Header/FormIDFormKey", "Header/U32VersionControl", "Header/U16FormVersion", "Header/U16Version2",
        "HeaderTES4/U32Flags", "HeaderTES4/U32FormID", "HeaderTES4/I32Version", "HeaderTES4/U16FormVersion", "HeaderTES4/U16Version2",
        "RecordTES4/SegmentsMasterReferences",
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
            AddHandReadLists(mod, target);
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

    private static void AddHandReadLists(StarfieldMod mod, FormKey target)
    {
        var topic = new PackageDataTopic { TPIC = new byte[] { 1, 2, 3, 4 } };
        var reference = new TopicReference();
        reference.Reference.SetTo(target);
        topic.Topics.Add(reference);
        topic.Topics.Add(new TopicReferenceSubtype { Subtype = new RecordType("ABCD") });
        var package = new Package(mod) { EditorID = "LayoutPackage" };
        package.Data[0] = topic;
        package.Data[1] = new PackageDataBool { Data = true, Name = "Flag", Flags = APackageData.Flag.Public };
        mod.Packages.Add(package);

        // C52: a target input whose condition has both text parameters (CIS1, CIS2 in its value block), then another input
        var cisTarget = new PackageDataTarget { Type = PackageDataTarget.Types.SingleRef, Name = "Target" };
        IConditionParameters cisData = new ActorExposedToSkyConditionData();
        cisData.StringParameter1 = "abc";
        cisData.StringParameter2 = "def";
        cisTarget.Conditions.Add(new ConditionFloat { ComparisonValue = 1, Data = (ConditionData)cisData });
        var cisPackage = new Package(mod) { EditorID = "LayoutPackageCis" };
        cisPackage.Data[0] = cisTarget;
        cisPackage.Data[1] = new PackageDataBool { Data = true, Name = "Flag" };
        mod.Packages.Add(cisPackage);

        mod.InstanceNamingRules.Add(new InstanceNamingRules(mod) { EditorID = "LayoutRulesNone", Rules = new NoInstanceNamingRules() });
        mod.InstanceNamingRules.Add(new InstanceNamingRules(mod) { EditorID = "LayoutRulesContainer", Rules = new ContainerInstanceNamingRules() });
        mod.InstanceNamingRules.Add(new InstanceNamingRules(mod) { EditorID = "LayoutRulesFlora", Rules = new FloraInstanceNamingRules() });
        mod.InstanceNamingRules.Add(new InstanceNamingRules(mod) { EditorID = "LayoutRulesFurniture", Rules = new FurnitureInstanceNamingRules() });

        var mapInfo = new NavigationMapInfo { PathingCell = new NavmeshPathingCell { Crc = 5 } };
        mapInfo.PathingCell.ParentCell.SetTo(target);
        mapInfo.UnknownCrcs.AddRange([0x11111111u, 0x22222222u]);
        var navi = new NavigationMeshInfoMap(mod) { EditorID = "LayoutNavi" };
        navi.MapInfos.Add(mapInfo);
        mod.NavigationMeshInfoMaps.Add(navi);

        // type 11 twice: with nothing after it (another item follows) and with a uint32 (the last, 4 bytes left)
        var volumes = new VolumesComponent
        {
            Items =
            [
                new VolumesComponentItem { Ender = new VolumesUnknownEnderEmpty() },
                new VolumesComponentItem { Ender = new VolumesUnknownEnderEmptyPadded { Unknown1 = 7 } },
            ],
        };
        var stat = new Static(mod) { EditorID = "LayoutVolumes" };
        stat.Components.Add(volumes);
        mod.Statics.Add(stat);
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
