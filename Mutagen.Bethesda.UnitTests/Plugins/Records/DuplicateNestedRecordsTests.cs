using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Utility;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing;
using Shouldly;
using Xunit;
using SK = Mutagen.Bethesda.Skyrim;
using SF = Mutagen.Bethesda.Starfield;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

/// <summary>
/// Nested major records keep their own FormKeys when copied, so a duplicate must not carry
/// them along.  Otherwise the duplicate holds overrides of the source's children, re-parented
/// under the new record.
/// </summary>
public class DuplicateNestedRecordsTests
{
    private static readonly ModKey SourceModKey = new("Source", ModType.Master);
    private static readonly ModKey PatchModKey = new("Patch", ModType.Plugin);

    private static void ShouldOnlyHaveNewRecords(IModGetter patch)
    {
        patch.EnumerateMajorRecords()
            .Where(r => r.FormKey.ModKey != patch.ModKey)
            .Select(r => r.FormKey)
            .ShouldBeEmpty();
    }

    [Fact]
    public void SkyrimDialogTopicDuplicateInAsNewRecordSkipsResponses()
    {
        var source = new SK.SkyrimMod(SourceModKey, SK.SkyrimRelease.SkyrimSE);
        var topic = source.DialogTopics.AddNew("Topic");
        topic.Name = "Hello";
        var response = new SK.DialogResponses(source.GetNextFormKey(), SK.SkyrimRelease.SkyrimSE);
        topic.Responses.Add(response);

        var patch = new SK.SkyrimMod(PatchModKey, SK.SkyrimRelease.SkyrimSE);
        var dup = patch.DialogTopics.DuplicateInAsNewRecord(topic);

        dup.FormKey.ModKey.ShouldBe(PatchModKey);
        dup.Name!.String.ShouldBe("Hello");
        dup.Responses.ShouldBeEmpty();
        topic.Responses.ShouldHaveSingleItem().ShouldBeSameAs(response);
        ShouldOnlyHaveNewRecords(patch);
    }

    [Fact]
    public void SkyrimDialogTopicDuplicateTwiceInSameModHasNoSharedFormKeys()
    {
        var mod = new SK.SkyrimMod(PatchModKey, SK.SkyrimRelease.SkyrimSE);
        var topic = mod.DialogTopics.AddNew("Topic");
        topic.Responses.Add(new SK.DialogResponses(mod.GetNextFormKey(), SK.SkyrimRelease.SkyrimSE));

        mod.DialogTopics.DuplicateInAsNewRecord(topic);
        mod.DialogTopics.DuplicateInAsNewRecord(topic);

        mod.EnumerateMajorRecords()
            .GroupBy(r => r.FormKey)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ShouldBeEmpty();
    }

    [Fact]
    public void SkyrimCellDuplicateSkipsNestedRecords()
    {
        var cell = new SK.Cell(new FormKey(SourceModKey, 0x800), SK.SkyrimRelease.SkyrimSE)
        {
            EditorID = "Cell",
            Landscape = new SK.Landscape(new FormKey(SourceModKey, 0x801), SK.SkyrimRelease.SkyrimSE),
        };
        cell.Persistent.Add(new SK.PlacedObject(new FormKey(SourceModKey, 0x802), SK.SkyrimRelease.SkyrimSE));
        cell.Temporary.Add(new SK.PlacedObject(new FormKey(SourceModKey, 0x803), SK.SkyrimRelease.SkyrimSE));
        cell.NavigationMeshes.Add(new SK.NavigationMesh(new FormKey(SourceModKey, 0x804), SK.SkyrimRelease.SkyrimSE));

        var dup = cell.Duplicate(new FormKey(PatchModKey, 0x800));

        dup.FormKey.ShouldBe(new FormKey(PatchModKey, 0x800));
        dup.EditorID.ShouldBe("Cell");
        dup.Landscape.ShouldBeNull();
        dup.Persistent.ShouldBeEmpty();
        dup.Temporary.ShouldBeEmpty();
        dup.NavigationMeshes.ShouldBeEmpty();
        dup.EnumerateMajorRecords().ShouldBeEmpty();
    }

    [Fact]
    public void SkyrimCellDuplicateWithExplicitMaskCopiesWhatTheMaskSays()
    {
        var cell = new SK.Cell(new FormKey(SourceModKey, 0x800), SK.SkyrimRelease.SkyrimSE);
        cell.Temporary.Add(new SK.PlacedObject(new FormKey(SourceModKey, 0x801), SK.SkyrimRelease.SkyrimSE));

        var dup = cell.Duplicate(new FormKey(PatchModKey, 0x800), new SK.Cell.TranslationMask(defaultOn: true));

        dup.Temporary.Select(x => x.FormKey).ShouldBe(new[] { new FormKey(SourceModKey, 0x801) });
    }

    [Fact]
    public void SkyrimWorldspaceDuplicateSkipsCells()
    {
        var subCell = new SK.Cell(new FormKey(SourceModKey, 0x802), SK.SkyrimRelease.SkyrimSE);
        var worldspace = new SK.Worldspace(new FormKey(SourceModKey, 0x800), SK.SkyrimRelease.SkyrimSE)
        {
            EditorID = "World",
            TopCell = new SK.Cell(new FormKey(SourceModKey, 0x801), SK.SkyrimRelease.SkyrimSE),
            SubCells =
            {
                new SK.WorldspaceBlock()
                {
                    Items = { new SK.WorldspaceSubBlock() { Items = { subCell } } }
                }
            }
        };

        var patch = new SK.SkyrimMod(PatchModKey, SK.SkyrimRelease.SkyrimSE);
        var dup = patch.Worldspaces.DuplicateInAsNewRecord(worldspace);

        dup.EditorID.ShouldBe("World");
        dup.TopCell.ShouldBeNull();
        dup.SubCells.ShouldBeEmpty();
        ShouldOnlyHaveNewRecords(patch);
    }

    [Fact]
    public void CompactingKeepsNestedRecordsWithTheirParent()
    {
        var mod = new SK.SkyrimMod(PatchModKey, SK.SkyrimRelease.SkyrimSE);
        var topic = new SK.DialogTopic(new FormKey(PatchModKey, 0x010000), SK.SkyrimRelease.SkyrimSE);
        topic.Responses.Add(new SK.DialogResponses(new FormKey(PatchModKey, 0x000900), SK.SkyrimRelease.SkyrimSE));
        mod.DialogTopics.Add(topic);
        var worldspace = new SK.Worldspace(new FormKey(PatchModKey, 0x010001), SK.SkyrimRelease.SkyrimSE)
        {
            SubCells =
            {
                new SK.WorldspaceBlock()
                {
                    Items =
                    {
                        new SK.WorldspaceSubBlock()
                        {
                            Items = { new SK.Cell(new FormKey(PatchModKey, 0x000901), SK.SkyrimRelease.SkyrimSE) }
                        }
                    }
                }
            }
        };
        mod.Worldspaces.Add(worldspace);

        ModCompaction.CompactToSmallMaster(mod);

        var newTopic = mod.DialogTopics.ShouldHaveSingleItem();
        newTopic.FormKey.ShouldNotBe(topic.FormKey);
        newTopic.Responses.Select(x => x.FormKey).ShouldBe(new[] { new FormKey(PatchModKey, 0x000900) });
        var newWorldspace = mod.Worldspaces.ShouldHaveSingleItem();
        newWorldspace.FormKey.ShouldNotBe(worldspace.FormKey);
        newWorldspace.EnumerateMajorRecords().Select(x => x.FormKey).ShouldBe(new[] { new FormKey(PatchModKey, 0x000901) });
    }

    [Fact]
    public void StarfieldQuestDuplicateSkipsNestedRecords()
    {
        var source = new SF.StarfieldMod(SourceModKey, SF.StarfieldRelease.Starfield);
        var quest = source.Quests.AddNew("Quest");
        quest.Name = "A Quest";
        var scene = new SF.Scene(source.GetNextFormKey(), SF.StarfieldRelease.Starfield);
        scene.Quest.SetTo(quest);
        quest.Scenes.Add(scene);
        quest.DialogBranches.Add(new SF.DialogBranch(source.GetNextFormKey(), SF.StarfieldRelease.Starfield));
        var topic = new SF.DialogTopic(source.GetNextFormKey(), SF.StarfieldRelease.Starfield);
        topic.Responses.Add(new SF.DialogResponses(source.GetNextFormKey(), SF.StarfieldRelease.Starfield));
        quest.DialogTopics.Add(topic);

        var dup = quest.Duplicate(new FormKey(PatchModKey, 0x800));

        dup.Name!.String.ShouldBe("A Quest");
        dup.Scenes.ShouldBeEmpty();
        dup.DialogBranches.ShouldBeEmpty();
        dup.DialogTopics.ShouldBeEmpty();
        dup.EnumerateMajorRecords().ShouldBeEmpty();
        quest.EnumerateMajorRecords().Count().ShouldBe(4);
    }

    [Fact]
    public void StarfieldQuestContextDuplicateIntoAsNewRecordSkipsNestedRecords()
    {
        var source = new SF.StarfieldMod(SourceModKey, SF.StarfieldRelease.Starfield);
        var quest = source.Quests.AddNew("Quest");
        quest.Scenes.Add(new SF.Scene(source.GetNextFormKey(), SF.StarfieldRelease.Starfield));
        var topic = new SF.DialogTopic(source.GetNextFormKey(), SF.StarfieldRelease.Starfield);
        topic.Responses.Add(new SF.DialogResponses(source.GetNextFormKey(), SF.StarfieldRelease.Starfield));
        quest.DialogTopics.Add(topic);
        var linkCache = source.ToImmutableLinkCache<SF.IStarfieldMod, SF.IStarfieldModGetter>();

        var patch = new SF.StarfieldMod(PatchModKey, SF.StarfieldRelease.Starfield);
        var dup = linkCache.ResolveContext<SF.IQuest, SF.IQuestGetter>(quest.FormKey)
            .DuplicateIntoAsNewRecord(patch);

        dup.FormKey.ModKey.ShouldBe(PatchModKey);
        dup.Scenes.ShouldBeEmpty();
        dup.DialogTopics.ShouldBeEmpty();
        ShouldOnlyHaveNewRecords(patch);
    }

    [Fact]
    public void StarfieldNestedTopicContextDuplicateIntoAsNewRecordSkipsResponses()
    {
        var source = new SF.StarfieldMod(SourceModKey, SF.StarfieldRelease.Starfield);
        var quest = source.Quests.AddNew("Quest");
        var topic = new SF.DialogTopic(source.GetNextFormKey(), SF.StarfieldRelease.Starfield);
        var response = new SF.DialogResponses(source.GetNextFormKey(), SF.StarfieldRelease.Starfield);
        topic.Responses.Add(response);
        quest.DialogTopics.Add(topic);
        var linkCache = source.ToImmutableLinkCache<SF.IStarfieldMod, SF.IStarfieldModGetter>();

        var patch = new SF.StarfieldMod(PatchModKey, SF.StarfieldRelease.Starfield);
        var dup = linkCache.ResolveContext<SF.IDialogTopic, SF.IDialogTopicGetter>(topic.FormKey)
            .DuplicateIntoAsNewRecord(patch);

        dup.FormKey.ModKey.ShouldBe(PatchModKey);
        dup.Responses.ShouldBeEmpty();
        patch.EnumerateMajorRecords()
            .GroupBy(r => r.FormKey)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ShouldBeEmpty();
    }
}
