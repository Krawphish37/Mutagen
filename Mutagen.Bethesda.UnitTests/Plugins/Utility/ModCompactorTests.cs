using System.Reflection;
using Autofac;
using Shouldly;
using Mutagen.Bethesda.Autofac;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Utility.DI;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog.Testing.Extensions;
using Xunit;
using SkyrimCell = Mutagen.Bethesda.Skyrim.Cell;
using SkyrimNpc = Mutagen.Bethesda.Skyrim.Npc;
using SkyrimPlacedObject = Mutagen.Bethesda.Skyrim.PlacedObject;
using StarfieldNpc = Mutagen.Bethesda.Starfield.Npc;

namespace Mutagen.Bethesda.UnitTests.Plugins.Utility;

public class ModCompactorTests
{
    public class Payload
    {
        public IModCompactor Sut { get; }

        public Payload()
        {
            var b = new ContainerBuilder();
            b.RegisterModule<MutagenModule>();
            Sut = b.Build().Resolve<IModCompactor>();
        }
    }
    
    [Theory, MutagenModAutoData]
    public void AlreadySmallMasterCompatible(
        SkyrimMod mod,
        SkyrimNpc n,
        SkyrimNpc n2,
        Payload sut)
    {
        mod.IsSmallMaster.ShouldBeFalse();
        mod.IsMediumMaster.ShouldBeFalse();
        var ret = sut.Sut.CompactToSmallMaster(mod);
        mod.IsSmallMaster.ShouldBeTrue();
        mod.IsMediumMaster.ShouldBeFalse();
        mod.Npcs.Records.Select(x => x.FormKey)
            .ShouldEqualEnumerable(n.FormKey, n2.FormKey);
        ret.ResultingStyle.ShouldBe(MasterStyle.Small);
        ret.RemappedFormKeys.ShouldBeEmpty();
    }
    
    [Theory, MutagenModAutoData]
    public void CompactRecordToSmallMaster(
        SkyrimMod mod,
        SkyrimNpc n,
        Payload sut)
    {
        var n2 = mod.Npcs.AddReturn(
            new SkyrimNpc(FormKey.Factory($"010000:{mod.ModKey.FileName}"), SkyrimRelease.SkyrimSE));
        var n3 = mod.Npcs.AddReturn(
            new SkyrimNpc(FormKey.Factory($"000030:{mod.ModKey.FileName}"), SkyrimRelease.SkyrimSE));
        var nextId = n.FormKey.ID + 1;
        mod.IsSmallMaster.ShouldBeFalse();
        mod.IsMediumMaster.ShouldBeFalse();
        var ret = sut.Sut.CompactToSmallMaster(mod);
        mod.IsSmallMaster.ShouldBeTrue();
        mod.IsMediumMaster.ShouldBeFalse();
        mod.Npcs.Records.Select(x => x.FormKey)
            .ShouldEqualEnumerable(n.FormKey, new FormKey(n.FormKey.ModKey, nextId), n3.FormKey);
        ret.ResultingStyle.ShouldBe(MasterStyle.Small);
        ret.RemappedFormKeys
            .ShouldEqualEnumerable(new KeyValuePair<FormKey, FormKey>(n2.FormKey, new FormKey(n.FormKey.ModKey, nextId)));
    }
    
    [Theory, MutagenModAutoData]
    public void FailToCompactRecordToSmallMaster(
        SkyrimMod mod,
        SkyrimNpc n,
        Payload sut)
    {
        for (int i = 0; i < 4096; i++)
        {
            mod.Npcs.AddNew();
        }
        mod.IsSmallMaster.ShouldBeFalse();
        mod.IsMediumMaster.ShouldBeFalse();
        Assert.Throws<TargetInvocationException>(() =>
        {
            sut.Sut.CompactToSmallMaster(mod);
        });
    }
    
    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void AlreadyMediumMasterCompatible(
        StarfieldMod mod,
        StarfieldNpc n,
        StarfieldNpc n2,
        Payload sut)
    {
        mod.IsSmallMaster.ShouldBeFalse();
        mod.IsMediumMaster.ShouldBeFalse();
        var ret = sut.Sut.CompactToMediumMaster(mod);
        mod.IsSmallMaster.ShouldBeFalse();
        mod.IsMediumMaster.ShouldBeTrue();
        mod.Npcs.Records.Select(x => x.FormKey)
            .ShouldEqualEnumerable(n.FormKey, n2.FormKey);
        ret.ResultingStyle.ShouldBe(MasterStyle.Medium);
        ret.RemappedFormKeys.ShouldBeEmpty();
    }
    
    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void CompactRecordToMediumMaster(
        StarfieldMod mod,
        StarfieldNpc n,
        Payload sut)
    {
        var n2 = mod.Npcs.AddReturn(
            new Starfield.Npc(FormKey.Factory($"080000:{mod.ModKey.FileName}"), StarfieldRelease.Starfield));
        var n3 = mod.Npcs.AddReturn(
            new Starfield.Npc(FormKey.Factory($"000030:{mod.ModKey.FileName}"), StarfieldRelease.Starfield));
        var nextId = n.FormKey.ID + 1;
        mod.IsSmallMaster.ShouldBeFalse();
        mod.IsMediumMaster.ShouldBeFalse();
        var ret = sut.Sut.CompactToMediumMaster(mod);
        mod.IsSmallMaster.ShouldBeFalse();
        mod.IsMediumMaster.ShouldBeTrue();
        mod.Npcs.Records.Select(x => x.FormKey)
            .ShouldEqualEnumerable(n.FormKey, new FormKey(n.FormKey.ModKey, nextId), n3.FormKey);
        ret.ResultingStyle.ShouldBe(MasterStyle.Medium);
        ret.RemappedFormKeys
            .ShouldEqualEnumerable(new KeyValuePair<FormKey, FormKey>(n2.FormKey, new FormKey(n.FormKey.ModKey, nextId)));
    }
    
    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void FailToCompactRecordToMediumMaster(
        StarfieldMod mod,
        StarfieldNpc n,
        Payload sut)
    {
        for (int i = 0; i < 65536; i++)
        {
            mod.Npcs.AddNew();
        }
        mod.IsSmallMaster.ShouldBeFalse();
        mod.IsMediumMaster.ShouldBeFalse();
        Assert.Throws<TargetInvocationException>(() =>
        {
            sut.Sut.CompactToMediumMaster(mod);
        });
    }
    
    [Theory, MutagenModAutoData]
    public void AlreadyFullMasterCompatible(
        SkyrimMod mod,
        SkyrimNpc n,
        Payload sut)
    {
        var n2 = mod.Npcs.AddNew(FormKey.Factory($"000810:{mod.ModKey.FileName}"));
        mod.IsSmallMaster.ShouldBeFalse();
        mod.IsMediumMaster.ShouldBeFalse();
        var ret = sut.Sut.CompactToFullMaster(mod);
        mod.IsSmallMaster.ShouldBeFalse();
        mod.IsMediumMaster.ShouldBeFalse();
        mod.Npcs.Records.Select(x => x.FormKey)
            .ShouldEqualEnumerable(n.FormKey, n2.FormKey);
        ret.ResultingStyle.ShouldBe(MasterStyle.Full);
        ret.RemappedFormKeys.ShouldBeEmpty();
    }
    
    [Theory, MutagenModAutoData]
    public void CompactRecordToFullMasterWithoutLowRange(
        SkyrimMod mod,
        SkyrimNpc n,
        Payload sut)
    {
        var n2 = mod.Npcs.AddNew(FormKey.Factory($"000010:{mod.ModKey.FileName}"));
        var nextId = n.FormKey.ID + 1;
        mod.IsSmallMaster.ShouldBeFalse();
        mod.IsMediumMaster.ShouldBeFalse();
        var ret = sut.Sut.CompactToFullMaster(mod);
        mod.IsSmallMaster.ShouldBeFalse();
        mod.IsMediumMaster.ShouldBeFalse();
        mod.Npcs.Records.Select(x => x.FormKey)
            .ShouldEqualEnumerable(n.FormKey, new FormKey(n.FormKey.ModKey, nextId));
        ret.ResultingStyle.ShouldBe(MasterStyle.Full);
        ret.RemappedFormKeys
            .ShouldEqualEnumerable(new KeyValuePair<FormKey, FormKey>(n2.FormKey, new FormKey(n.FormKey.ModKey, nextId)));
    }
    
    [Theory, MutagenModAutoData]
    public void CompactToWithFallbackSmall(
        SkyrimMod mod,
        SkyrimNpc n,
        Payload sut)
    {
        var n2 = mod.Npcs.AddNew(FormKey.Factory($"010000:{mod.ModKey.FileName}"));
        var n3 = mod.Npcs.AddReturn(
            new SkyrimNpc(FormKey.Factory($"000030:{mod.ModKey.FileName}"), SkyrimRelease.SkyrimSE));
        var nextId = n.FormKey.ID + 1;
        mod.IsSmallMaster.ShouldBeFalse();
        mod.IsMediumMaster.ShouldBeFalse();
        var ret = sut.Sut.CompactToWithFallback(mod, MasterStyle.Small);
        mod.IsSmallMaster.ShouldBeTrue();
        mod.IsMediumMaster.ShouldBeFalse();
        mod.Npcs.Records.Select(x => x.FormKey)
            .ShouldEqualEnumerable(n.FormKey, new FormKey(n.FormKey.ModKey, nextId), n3.FormKey);
        ret.ResultingStyle.ShouldBe(MasterStyle.Small);
        ret.RemappedFormKeys
            .ShouldEqualEnumerable(new KeyValuePair<FormKey, FormKey>(n2.FormKey, new FormKey(n.FormKey.ModKey, nextId)));
    }
    
    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void CompactToWithFallbackSmallFallbackToMedium(
        StarfieldMod mod,
        StarfieldNpc n,
        Payload sut)
    {
        var n2 = mod.Npcs.AddNew(FormKey.Factory($"010000:{mod.ModKey.FileName}"));
        var n3 = mod.Npcs.AddReturn(
            new StarfieldNpc(FormKey.Factory($"000030:{mod.ModKey.FileName}"), StarfieldRelease.Starfield));
        for (int i = 0; i < 4096; i++)
        {
            mod.Npcs.AddNew();
        }
        mod.IsSmallMaster.ShouldBeFalse();
        mod.IsMediumMaster.ShouldBeFalse();
        var ret = sut.Sut.CompactToWithFallback(mod, MasterStyle.Small);
        mod.IsSmallMaster.ShouldBeFalse();
        mod.IsMediumMaster.ShouldBeTrue();
        mod.Npcs.Records.Count().ShouldBe(4099);
        ret.ResultingStyle.ShouldBe(MasterStyle.Medium);
        ret.RemappedFormKeys
            .ShouldEqualEnumerable(new KeyValuePair<FormKey, FormKey>(n2.FormKey, new FormKey(n.FormKey.ModKey, 0x1801)));
    }
    
    [Theory, MutagenModAutoData]
    public void CompactToWithFallbackSmallFallbackToFull(
        SkyrimMod mod,
        SkyrimNpc n,
        Payload sut)
    {
        var n2 = mod.Npcs.AddNew(FormKey.Factory($"010000:{mod.ModKey.FileName}"));
        var n3 = mod.Npcs.AddReturn(
            new SkyrimNpc(FormKey.Factory($"000030:{mod.ModKey.FileName}"), SkyrimRelease.SkyrimSE));
        for (int i = 0; i < 4096; i++)
        {
            mod.Npcs.AddNew();
        }
        mod.IsSmallMaster.ShouldBeFalse();
        mod.IsMediumMaster.ShouldBeFalse();
        var ret = sut.Sut.CompactToWithFallback(mod, MasterStyle.Small);
        mod.IsSmallMaster.ShouldBeFalse();
        mod.IsMediumMaster.ShouldBeFalse();
        mod.Npcs.Records.Count().ShouldBe(4099);
        ret.ResultingStyle.ShouldBe(MasterStyle.Full);
        ret.RemappedFormKeys
            .ShouldEqualEnumerable(new KeyValuePair<FormKey, FormKey>(n3.FormKey, new FormKey(n.FormKey.ModKey, 0x1801)));
    }

    private static SkyrimCell AddInteriorCell(SkyrimMod mod, string formKey)
    {
        var cell = new SkyrimCell(FormKey.Factory($"{formKey}:{mod.ModKey.FileName}"), SkyrimRelease.SkyrimSE);
        mod.Cells.Records.Add(new Skyrim.CellBlock()
        {
            BlockNumber = 0,
            GroupType = Skyrim.GroupTypeEnum.InteriorCellBlock,
            SubBlocks =
            {
                new Skyrim.CellSubBlock()
                {
                    BlockNumber = 0,
                    GroupType = Skyrim.GroupTypeEnum.InteriorCellSubBlock,
                    Cells = { cell }
                }
            }
        });
        return cell;
    }

    private static SkyrimPlacedObject NewPlacedObject(SkyrimMod mod, string formKey)
    {
        return new SkyrimPlacedObject(FormKey.Factory($"{formKey}:{mod.ModKey.FileName}"), SkyrimRelease.SkyrimSE);
    }

    private static void AllInRange(IModGetter mod)
    {
        foreach (var rec in mod.EnumerateMajorRecords())
        {
            rec.FormKey.ID.ShouldBeLessThan(0x1000u);
        }
    }

    [Theory, MutagenModAutoData]
    public void CompactCellKeepsReferences(
        SkyrimMod mod,
        Payload sut)
    {
        var cell = AddInteriorCell(mod, "010000");
        var temp = NewPlacedObject(mod, "000900");
        var persistent = NewPlacedObject(mod, "000901");
        cell.Temporary.Add(temp);
        cell.Persistent.Add(persistent);
        var ret = sut.Sut.CompactToSmallMaster(mod);
        ret.RemappedFormKeys.Keys.ShouldEqualEnumerable(cell.FormKey);
        var newCell = mod.Cells.Records.SelectMany(b => b.SubBlocks).SelectMany(s => s.Cells).ShouldHaveSingleItem();
        newCell.FormKey.ShouldBe(ret.RemappedFormKeys[cell.FormKey]);
        newCell.Temporary.Select(x => x.FormKey).ShouldEqualEnumerable(temp.FormKey);
        newCell.Persistent.Select(x => x.FormKey).ShouldEqualEnumerable(persistent.FormKey);
        mod.EnumerateMajorRecords().Count().ShouldBe(3);
        AllInRange(mod);
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void CompactStarfieldCellAndReferences(
        StarfieldMod mod,
        Payload sut)
    {
        var cell = new Starfield.Cell(FormKey.Factory($"010000:{mod.ModKey.FileName}"), StarfieldRelease.Starfield);
        var temp = new Starfield.PlacedObject(FormKey.Factory($"010001:{mod.ModKey.FileName}"), StarfieldRelease.Starfield);
        var persistent = new Starfield.PlacedObject(FormKey.Factory($"000900:{mod.ModKey.FileName}"), StarfieldRelease.Starfield);
        cell.Temporary.Add(temp);
        cell.Persistent.Add(persistent);
        mod.Cells.Records.Add(new Starfield.CellBlock()
        {
            BlockNumber = 0,
            GroupType = Starfield.GroupTypeEnum.InteriorCellBlock,
            SubBlocks =
            {
                new Starfield.CellSubBlock()
                {
                    BlockNumber = 0,
                    GroupType = Starfield.GroupTypeEnum.InteriorCellSubBlock,
                    Cells = { cell }
                }
            }
        });
        var ret = sut.Sut.CompactToSmallMaster(mod);
        var newCell = mod.Cells.Records.SelectMany(b => b.SubBlocks).SelectMany(s => s.Cells).ShouldHaveSingleItem();
        newCell.FormKey.ShouldBe(ret.RemappedFormKeys[cell.FormKey]);
        newCell.Temporary.Select(x => x.FormKey).ShouldEqualEnumerable(ret.RemappedFormKeys[temp.FormKey]);
        newCell.Persistent.Select(x => x.FormKey).ShouldEqualEnumerable(persistent.FormKey);
        mod.EnumerateMajorRecords().Count().ShouldBe(3);
        AllInRange(mod);
    }

    [Theory, MutagenModAutoData]
    public void CompactCellAndReference(
        SkyrimMod mod,
        Payload sut)
    {
        var cell = AddInteriorCell(mod, "010000");
        var placed = NewPlacedObject(mod, "010001");
        cell.Temporary.Add(placed);
        var ret = sut.Sut.CompactToSmallMaster(mod);
        ret.RemappedFormKeys.Keys.ShouldBe(new[] { cell.FormKey, placed.FormKey }, ignoreOrder: true);
        var newCell = mod.Cells.Records.SelectMany(b => b.SubBlocks).SelectMany(s => s.Cells).ShouldHaveSingleItem();
        newCell.FormKey.ShouldBe(ret.RemappedFormKeys[cell.FormKey]);
        newCell.Temporary.Select(x => x.FormKey).ShouldEqualEnumerable(ret.RemappedFormKeys[placed.FormKey]);
        mod.EnumerateMajorRecords().Count().ShouldBe(2);
        AllInRange(mod);
    }

    [Theory, MutagenModAutoData]
    public void CompactReferenceWithLowerIdThanCell(
        SkyrimMod mod,
        Payload sut)
    {
        var cell = AddInteriorCell(mod, "010005");
        var placed = NewPlacedObject(mod, "010001");
        cell.Temporary.Add(placed);
        var ret = sut.Sut.CompactToSmallMaster(mod);
        var newCell = mod.Cells.Records.SelectMany(b => b.SubBlocks).SelectMany(s => s.Cells).ShouldHaveSingleItem();
        newCell.FormKey.ShouldBe(ret.RemappedFormKeys[cell.FormKey]);
        newCell.Temporary.Select(x => x.FormKey).ShouldEqualEnumerable(ret.RemappedFormKeys[placed.FormKey]);
        mod.EnumerateMajorRecords().Count().ShouldBe(2);
        AllInRange(mod);
    }

    [Theory, MutagenModAutoData]
    public void CompactDialogTopicAndResponses(
        SkyrimMod mod,
        Payload sut)
    {
        var topic = mod.DialogTopics.AddReturn(
            new Skyrim.DialogTopic(FormKey.Factory($"010000:{mod.ModKey.FileName}"), SkyrimRelease.SkyrimSE));
        var responses = new Skyrim.DialogResponses(FormKey.Factory($"010001:{mod.ModKey.FileName}"), SkyrimRelease.SkyrimSE);
        topic.Responses.Add(responses);
        var ret = sut.Sut.CompactToSmallMaster(mod);
        var newTopic = mod.DialogTopics.Records.ShouldHaveSingleItem();
        newTopic.FormKey.ShouldBe(ret.RemappedFormKeys[topic.FormKey]);
        newTopic.Responses.Select(x => x.FormKey).ShouldContain(ret.RemappedFormKeys[responses.FormKey]);
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void CompactQuestAndScene(
        StarfieldMod mod,
        Payload sut)
    {
        var quest = mod.Quests.AddReturn(
            new Starfield.Quest(FormKey.Factory($"010000:{mod.ModKey.FileName}"), StarfieldRelease.Starfield));
        var scene = new Starfield.Scene(FormKey.Factory($"010001:{mod.ModKey.FileName}"), StarfieldRelease.Starfield);
        quest.Scenes.Add(scene);
        var ret = sut.Sut.CompactToSmallMaster(mod);
        var newQuest = mod.Quests.Records.ShouldHaveSingleItem();
        newQuest.FormKey.ShouldBe(ret.RemappedFormKeys[quest.FormKey]);
        newQuest.Scenes.Select(x => x.FormKey).ShouldContain(ret.RemappedFormKeys[scene.FormKey]);
    }
}