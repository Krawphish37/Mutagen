using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Analysis.DI;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;
using Shouldly;
using Fo4 = Mutagen.Bethesda.Fallout4;
using Sky = Mutagen.Bethesda.Skyrim;
using Sf = Mutagen.Bethesda.Starfield;

namespace Mutagen.Bethesda.UnitTests.Plugins.Analysis;

/// <summary>
/// A mod that creates a parent record (topic, worldspace, quest) together with records nested in it that it also creates
/// </summary>
public class MultiModFileSplitterNestedCreatedTests
{
    private static readonly ModKey InputKey = ModKey.FromFileName("Input.esp");
    private static readonly ModKey SecondKey = ModKey.FromFileName("Input_2.esp");

    /// <summary>
    /// Fills the first mod fragment (5 masters at a limit of 5), so a record linking to it and to one more master
    /// has to go in the second fragment
    /// </summary>
    private static FormKey AddSkyrimBlocker(Sky.SkyrimMod mod)
    {
        var formList = new Sky.FormList(new FormKey(InputKey, 0x900), Sky.SkyrimRelease.SkyrimSE);
        for (int i = 0; i < 5; i++)
        {
            formList.Items.Add(new FormKey(ModKey.FromFileName($"Master{i}.esm"), 0x800));
        }
        mod.FormLists.Add(formList);
        return formList.FormKey;
    }

    private static readonly FormKey OtherMasterRecord = new(ModKey.FromFileName("Master5.esm"), 0x800);

    private static Sky.SkyrimMod SkyrimTopic(bool secondFragment)
    {
        var mod = new Sky.SkyrimMod(InputKey, Sky.SkyrimRelease.SkyrimSE);
        var topic = new Sky.DialogTopic(new FormKey(InputKey, 0x800), Sky.SkyrimRelease.SkyrimSE);
        for (uint i = 1; i <= 3; i++)
        {
            topic.Responses.Add(new Sky.DialogResponses(new FormKey(InputKey, 0x800 + i), Sky.SkyrimRelease.SkyrimSE));
        }
        mod.DialogTopics.Add(topic);
        if (secondFragment)
        {
            topic.Responses[0].Speaker.SetTo(AddSkyrimBlocker(mod));
            topic.Responses[0].WalkAwayTopic.SetTo(OtherMasterRecord);
        }
        return mod;
    }

    private static Sky.SkyrimMod SkyrimWorldspace(bool secondFragment, bool movedExternalRef = false)
    {
        var mod = new Sky.SkyrimMod(InputKey, Sky.SkyrimRelease.SkyrimSE);
        var worldspace = new Sky.Worldspace(new FormKey(InputKey, 0x800), Sky.SkyrimRelease.SkyrimSE);
        var cell = new Sky.Cell(new FormKey(InputKey, 0x801), Sky.SkyrimRelease.SkyrimSE)
        {
            Grid = new Sky.CellGrid { Point = new P2Int(0, 0) },
        };
        var placed = new Sky.PlacedObject(new FormKey(InputKey, 0x802), Sky.SkyrimRelease.SkyrimSE);
        cell.Temporary.Add(placed);
        if (movedExternalRef)
        {
            cell.Temporary.Add(new Sky.PlacedObject(new FormKey(ModKey.FromFileName("Skyrim.esm"), 0x1234), Sky.SkyrimRelease.SkyrimSE));
        }
        worldspace.SubCells.Add(new Sky.WorldspaceBlock
        {
            BlockNumberX = 0,
            BlockNumberY = 0,
            GroupType = Sky.GroupTypeEnum.ExteriorCellBlock,
            Items =
            {
                new Sky.WorldspaceSubBlock
                {
                    BlockNumberX = 0,
                    BlockNumberY = 0,
                    GroupType = Sky.GroupTypeEnum.ExteriorCellSubBlock,
                    Items = { cell }
                }
            }
        });
        mod.Worldspaces.Add(worldspace);
        if (secondFragment)
        {
            placed.Base.SetTo(AddSkyrimBlocker(mod));
            placed.EncounterZone.SetTo(OtherMasterRecord);
        }
        return mod;
    }

    private static Sf.StarfieldMod StarfieldQuest(bool secondFragment)
    {
        var mod = new Sf.StarfieldMod(InputKey, Sf.StarfieldRelease.Starfield);
        var quest = new Sf.Quest(new FormKey(InputKey, 0x800), Sf.StarfieldRelease.Starfield);
        quest.Scenes.Add(new Sf.Scene(new FormKey(InputKey, 0x801), Sf.StarfieldRelease.Starfield));
        var topic = new Sf.DialogTopic(new FormKey(InputKey, 0x802), Sf.StarfieldRelease.Starfield);
        var responses = new Sf.DialogResponses(new FormKey(InputKey, 0x803), Sf.StarfieldRelease.Starfield);
        topic.Responses.Add(responses);
        quest.DialogTopics.Add(topic);
        mod.Quests.Add(quest);
        if (secondFragment)
        {
            var formList = new Sf.FormList(new FormKey(InputKey, 0x900), Sf.StarfieldRelease.Starfield);
            for (int i = 0; i < 5; i++)
            {
                formList.Items.Add(new FormKey(ModKey.FromFileName($"Master{i}.esm"), 0x800));
            }
            mod.FormLists.Add(formList);
            responses.Speaker.SetTo(formList.FormKey);
            responses.ResetGlobal.SetTo(OtherMasterRecord);
        }
        return mod;
    }

    /// <summary>
    /// Each record with its closest parent record, by FormKey
    /// </summary>
    private static List<(FormKey Record, FormKey? Parent)> Layout(IModGetter mod)
    {
        var parents = new Dictionary<FormKey, FormKey>();
        void Walk(IMajorRecordGetter parent)
        {
            foreach (var child in parent.EnumerateMajorRecords())
            {
                if (parents.ContainsKey(child.FormKey)) continue;
                parents[child.FormKey] = parent.FormKey;
            }
        }
        // Closest parents first, so they claim their children before a grandparent does
        foreach (var record in mod.EnumerateMajorRecords().OrderBy(x => x.EnumerateMajorRecords().Count()))
        {
            Walk(record);
        }
        return mod.EnumerateMajorRecords()
            .Select(x => (x.FormKey, parents.TryGetValue(x.FormKey, out var p) ? p : (FormKey?)null))
            .ToList();
    }

    private static FormKey Key(ModKey modKey, uint id) => new(modKey, id);

    private static List<(FormKey Record, FormKey? Parent)> Expect(params (FormKey Record, FormKey? Parent)[] items) => items.ToList();

    [Fact]
    public void SkyrimTopicWithCreatedResponses_OneFragment()
    {
        var outputs = new MultiModFileSplitter().Split<Sky.ISkyrimMod, Sky.ISkyrimModGetter>(SkyrimTopic(secondFragment: false), 254);
        outputs.Count.ShouldBe(1);
        Layout(outputs[0]).ShouldBe(
            Expect(
                (Key(InputKey, 0x800), null),
                (Key(InputKey, 0x801), Key(InputKey, 0x800)),
                (Key(InputKey, 0x802), Key(InputKey, 0x800)),
                (Key(InputKey, 0x803), Key(InputKey, 0x800))),
            ignoreOrder: true);
    }

    [Fact]
    public void SkyrimTopicWithCreatedResponses_SecondFragment()
    {
        var outputs = new MultiModFileSplitter().Split<Sky.ISkyrimMod, Sky.ISkyrimModGetter>(SkyrimTopic(secondFragment: true), 5);
        outputs.Select(x => x.ModKey).ShouldBe(new[] { InputKey, SecondKey });
        Layout(outputs[0]).ShouldBe(Expect((Key(InputKey, 0x900), null)));
        Layout(outputs[1]).ShouldBe(
            Expect(
                (Key(SecondKey, 0x800), null),
                (Key(SecondKey, 0x801), Key(SecondKey, 0x800)),
                (Key(SecondKey, 0x802), Key(SecondKey, 0x800)),
                (Key(SecondKey, 0x803), Key(SecondKey, 0x800))),
            ignoreOrder: true);
        // The re-keyed topic is found in its group under its new FormKey, and links to fragment 0 are left alone
        var topic = outputs[1].DialogTopics.RecordCache[Key(SecondKey, 0x800)];
        outputs[1].DialogTopics.Count.ShouldBe(1);
        topic.Responses.First(x => x.FormKey.ID == 0x801).Speaker.FormKey.ShouldBe(Key(InputKey, 0x900));
    }

    [Fact]
    public void SkyrimWorldspaceWithCreatedCellAndRef_OneFragment()
    {
        var outputs = new MultiModFileSplitter().Split<Sky.ISkyrimMod, Sky.ISkyrimModGetter>(SkyrimWorldspace(secondFragment: false), 254);
        outputs.Count.ShouldBe(1);
        Layout(outputs[0]).ShouldBe(
            Expect(
                (Key(InputKey, 0x800), null),
                (Key(InputKey, 0x801), Key(InputKey, 0x800)),
                (Key(InputKey, 0x802), Key(InputKey, 0x801))),
            ignoreOrder: true);
    }

    [Fact]
    public void SkyrimWorldspaceWithCreatedCellAndRef_SecondFragment()
    {
        var outputs = new MultiModFileSplitter().Split<Sky.ISkyrimMod, Sky.ISkyrimModGetter>(SkyrimWorldspace(secondFragment: true), 5);
        outputs.Select(x => x.ModKey).ShouldBe(new[] { InputKey, SecondKey });
        Layout(outputs[0]).ShouldBe(Expect((Key(InputKey, 0x900), null)));
        Layout(outputs[1]).ShouldBe(
            Expect(
                (Key(SecondKey, 0x800), null),
                (Key(SecondKey, 0x801), Key(SecondKey, 0x800)),
                (Key(SecondKey, 0x802), Key(SecondKey, 0x801))),
            ignoreOrder: true);
    }

    [Fact]
    public void SkyrimOverrideRefInCreatedCell_FollowsTheCell()
    {
        var outputs = new MultiModFileSplitter().Split<Sky.ISkyrimMod, Sky.ISkyrimModGetter>(SkyrimWorldspace(secondFragment: true, movedExternalRef: true), 5);
        outputs.Select(x => x.ModKey).ShouldBe(new[] { InputKey, SecondKey });
        Layout(outputs[0]).ShouldBe(Expect((Key(InputKey, 0x900), null)));
        Layout(outputs[1]).ShouldBe(
            Expect(
                (Key(SecondKey, 0x800), null),
                (Key(SecondKey, 0x801), Key(SecondKey, 0x800)),
                (Key(SecondKey, 0x802), Key(SecondKey, 0x801)),
                (Key(ModKey.FromFileName("Skyrim.esm"), 0x1234), Key(SecondKey, 0x801))),
            ignoreOrder: true);
    }

    [Fact]
    public void StarfieldQuestWithCreatedSceneAndDialogue_OneFragment()
    {
        var outputs = new MultiModFileSplitter().Split<Sf.IStarfieldMod, Sf.IStarfieldModGetter>(StarfieldQuest(secondFragment: false), 254);
        outputs.Count.ShouldBe(1);
        Layout(outputs[0]).ShouldBe(
            Expect(
                (Key(InputKey, 0x800), null),
                (Key(InputKey, 0x801), Key(InputKey, 0x800)),
                (Key(InputKey, 0x802), Key(InputKey, 0x800)),
                (Key(InputKey, 0x803), Key(InputKey, 0x802))),
            ignoreOrder: true);
    }

    [Fact]
    public void StarfieldQuestWithCreatedSceneAndDialogue_SecondFragment()
    {
        var outputs = new MultiModFileSplitter().Split<Sf.IStarfieldMod, Sf.IStarfieldModGetter>(StarfieldQuest(secondFragment: true), 5);
        outputs.Select(x => x.ModKey).ShouldBe(new[] { InputKey, SecondKey });
        Layout(outputs[0]).ShouldBe(Expect((Key(InputKey, 0x900), null)));
        Layout(outputs[1]).ShouldBe(
            Expect(
                (Key(SecondKey, 0x800), null),
                (Key(SecondKey, 0x801), Key(SecondKey, 0x800)),
                (Key(SecondKey, 0x802), Key(SecondKey, 0x800)),
                (Key(SecondKey, 0x803), Key(SecondKey, 0x802))),
            ignoreOrder: true);
    }

    /// <summary>
    /// A created weapon mod and a form list that link to each other, so both go in the second fragment.
    /// The OMOD group holds the abstract AObjectModification, and the record is a WeaponModification
    /// </summary>
    private static Fo4.Fallout4Mod Fallout4WeaponModInSecondFragment()
    {
        var mod = new Fo4.Fallout4Mod(InputKey, Fo4.Fallout4Release.Fallout4);
        var blocker = new Fo4.FormList(new FormKey(InputKey, 0x900), Fo4.Fallout4Release.Fallout4);
        for (int i = 0; i < 5; i++)
        {
            blocker.Items.Add(new FormKey(ModKey.FromFileName($"Master{i}.esm"), 0x800));
        }
        mod.FormLists.Add(blocker);
        var weaponMod = new Fo4.WeaponModification(new FormKey(InputKey, 0x800), Fo4.Fallout4Release.Fallout4);
        var formList = new Fo4.FormList(new FormKey(InputKey, 0x802), Fo4.Fallout4Release.Fallout4);
        formList.Items.Add(blocker.FormKey);
        formList.Items.Add(OtherMasterRecord);
        formList.Items.Add(weaponMod.FormKey);
        weaponMod.LooseMod.SetTo(formList.FormKey);
        mod.ObjectModifications.Add(weaponMod);
        mod.FormLists.Add(formList);
        return mod;
    }

    [Fact]
    public void Fallout4CreatedWeaponMod_SecondFragment()
    {
        var outputs = new MultiModFileSplitter().Split<Fo4.IFallout4Mod, Fo4.IFallout4ModGetter>(Fallout4WeaponModInSecondFragment(), 5);
        outputs.Select(x => x.ModKey).ShouldBe(new[] { InputKey, SecondKey });
        var second = outputs[1];
        second.ObjectModifications.Select(x => x.FormKey).ShouldBe(new[] { Key(SecondKey, 0x800) });
        second.FormLists.Select(x => x.FormKey).ShouldBe(new[] { Key(SecondKey, 0x802) });
        var weaponMod = second.ObjectModifications.RecordCache[Key(SecondKey, 0x800)].ShouldBeOfType<Fo4.WeaponModification>();
        weaponMod.LooseMod.FormKey.ShouldBe(Key(SecondKey, 0x802));

        // The fragment writes and reads back with the one re-keyed OMOD
        var stream = new MemoryTributary();
        second.WriteToBinary(stream);
        stream.Position = 0;
        var read = Fo4.Fallout4Mod.CreateFromBinaryOverlay(stream, Fo4.Fallout4Release.Fallout4, second.ModKey);
        read.ObjectModifications.Select(x => x.FormKey).ShouldBe(new[] { Key(SecondKey, 0x800) });
    }

    [Fact]
    public void StarfieldCreatedWeaponMod_SecondFragment()
    {
        var mod = new Sf.StarfieldMod(InputKey, Sf.StarfieldRelease.Starfield);
        var blocker = new Sf.FormList(new FormKey(InputKey, 0x900), Sf.StarfieldRelease.Starfield);
        for (int i = 0; i < 5; i++)
        {
            blocker.Items.Add(new FormKey(ModKey.FromFileName($"Master{i}.esm"), 0x800));
        }
        mod.FormLists.Add(blocker);
        var weaponMod = new Sf.WeaponModification(new FormKey(InputKey, 0x800), Sf.StarfieldRelease.Starfield);
        var formList = new Sf.FormList(new FormKey(InputKey, 0x802), Sf.StarfieldRelease.Starfield);
        formList.Items.Add(blocker.FormKey);
        formList.Items.Add(OtherMasterRecord);
        formList.Items.Add(weaponMod.FormKey);
        weaponMod.LooseMod.SetTo(formList.FormKey);
        mod.ObjectModifications.Add(weaponMod);
        mod.FormLists.Add(formList);

        var outputs = new MultiModFileSplitter().Split<Sf.IStarfieldMod, Sf.IStarfieldModGetter>(mod, 5);
        outputs.Select(x => x.ModKey).ShouldBe(new[] { InputKey, SecondKey });
        var second = outputs[1];
        second.ObjectModifications.Select(x => x.FormKey).ShouldBe(new[] { Key(SecondKey, 0x800) });
        second.ObjectModifications.RecordCache[Key(SecondKey, 0x800)].ShouldBeOfType<Sf.WeaponModification>()
            .LooseMod.FormKey.ShouldBe(Key(SecondKey, 0x802));
    }
}
