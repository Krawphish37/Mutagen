using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing;
using Xunit;
using SK = Mutagen.Bethesda.Skyrim;
using SF = Mutagen.Bethesda.Starfield;
using F4 = Mutagen.Bethesda.Fallout4;
using OB = Mutagen.Bethesda.Oblivion;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

#pragma warning disable CS0618 // Type or member is obsolete
public class RemoveNestedRecordsTests
{
    private static bool Contains(IMajorRecordGetterEnumerable mod, FormKey formKey)
    {
        return mod.EnumerateMajorRecords().Any(r => r.FormKey == formKey);
    }

    private class SkyrimWorld
    {
        public SK.SkyrimMod Mod = null!;
        public SK.Worldspace Worldspace = null!;
        public SK.Cell TopCell = null!;
        public SK.PlacedObject TopCellRef = null!;
        public SK.Cell ExteriorCell = null!;
        public SK.PlacedObject ExteriorRef = null!;
        public SK.PlacedObject OtherExteriorRef = null!;
        public SK.DialogTopic Topic = null!;
        public SK.DialogResponses Response = null!;
        public SK.DialogResponses OtherResponse = null!;
    }

    private static SkyrimWorld GetSkyrimWorld()
    {
        var release = SK.SkyrimRelease.SkyrimSE;
        var mod = new SK.SkyrimMod(TestConstants.PluginModKey, release);
        var ret = new SkyrimWorld()
        {
            Mod = mod,
            Worldspace = mod.Worldspaces.AddNew(),
            TopCell = new SK.Cell(mod.GetNextFormKey(), release),
            TopCellRef = new SK.PlacedObject(mod.GetNextFormKey(), release),
            ExteriorCell = new SK.Cell(mod.GetNextFormKey(), release),
            ExteriorRef = new SK.PlacedObject(mod.GetNextFormKey(), release),
            OtherExteriorRef = new SK.PlacedObject(mod.GetNextFormKey(), release),
            Topic = mod.DialogTopics.AddNew(),
            Response = new SK.DialogResponses(mod.GetNextFormKey(), release),
            OtherResponse = new SK.DialogResponses(mod.GetNextFormKey(), release),
        };
        ret.TopCell.Persistent.Add(ret.TopCellRef);
        ret.Worldspace.TopCell = ret.TopCell;
        ret.ExteriorCell.Temporary.Add(ret.ExteriorRef);
        ret.ExteriorCell.Temporary.Add(ret.OtherExteriorRef);
        ret.Worldspace.SubCells.Add(new SK.WorldspaceBlock()
        {
            Items =
            {
                new SK.WorldspaceSubBlock()
                {
                    Items = { ret.ExteriorCell }
                }
            }
        });
        ret.Topic.Responses.Add(ret.Response);
        ret.Topic.Responses.Add(ret.OtherResponse);
        return ret;
    }

    private class StarfieldWorld
    {
        public SF.StarfieldMod Mod = null!;
        public SF.Weapon Weapon = null!;
        public SF.Weapon OtherWeapon = null!;
        public SF.Global Global = null!;
        public SF.Cell InteriorCell = null!;
        public SF.PlacedObject InteriorRef = null!;
        public SF.PlacedObject OtherInteriorRef = null!;
        public SF.Worldspace Worldspace = null!;
        public SF.Cell TopCell = null!;
        public SF.PlacedNpc TopCellNpc = null!;
        public SF.Cell ExteriorCell = null!;
        public SF.PlacedObject ExteriorRef = null!;
        public SF.PlacedNpc ExteriorNpc = null!;
        public SF.Quest Quest = null!;
        public SF.Scene Scene = null!;
        public SF.DialogBranch Branch = null!;
        public SF.DialogTopic Topic = null!;
        public SF.DialogResponses Response = null!;
        public SF.DialogResponses OtherResponse = null!;
    }

    private static StarfieldWorld GetStarfieldWorld()
    {
        var release = SF.StarfieldRelease.Starfield;
        var mod = new SF.StarfieldMod(TestConstants.PluginModKey, release);
        var ret = new StarfieldWorld()
        {
            Mod = mod,
            Weapon = mod.Weapons.AddNew(),
            OtherWeapon = mod.Weapons.AddNew(),
            Global = mod.Globals.AddNew(),
            InteriorCell = new SF.Cell(mod.GetNextFormKey(), release),
            InteriorRef = new SF.PlacedObject(mod.GetNextFormKey(), release),
            OtherInteriorRef = new SF.PlacedObject(mod.GetNextFormKey(), release),
            Worldspace = mod.Worldspaces.AddNew(),
            TopCell = new SF.Cell(mod.GetNextFormKey(), release),
            TopCellNpc = new SF.PlacedNpc(mod.GetNextFormKey(), release),
            ExteriorCell = new SF.Cell(mod.GetNextFormKey(), release),
            ExteriorRef = new SF.PlacedObject(mod.GetNextFormKey(), release),
            ExteriorNpc = new SF.PlacedNpc(mod.GetNextFormKey(), release),
            Quest = mod.Quests.AddNew(),
            Scene = new SF.Scene(mod.GetNextFormKey(), release),
            Branch = new SF.DialogBranch(mod.GetNextFormKey(), release),
            Topic = new SF.DialogTopic(mod.GetNextFormKey(), release),
            Response = new SF.DialogResponses(mod.GetNextFormKey(), release),
            OtherResponse = new SF.DialogResponses(mod.GetNextFormKey(), release),
        };
        ret.InteriorCell.Temporary.Add(ret.InteriorRef);
        ret.InteriorCell.Temporary.Add(ret.OtherInteriorRef);
        mod.Cells.Records.Add(new SF.CellBlock()
        {
            BlockNumber = 1,
            SubBlocks =
            {
                new SF.CellSubBlock()
                {
                    BlockNumber = 1,
                    Cells = { ret.InteriorCell }
                }
            }
        });
        ret.TopCell.Persistent.Add(ret.TopCellNpc);
        ret.Worldspace.TopCell = ret.TopCell;
        ret.ExteriorCell.Temporary.Add(ret.ExteriorRef);
        ret.ExteriorCell.Temporary.Add(ret.ExteriorNpc);
        ret.Worldspace.SubCells.Add(new SF.WorldspaceBlock()
        {
            Items =
            {
                new SF.WorldspaceSubBlock()
                {
                    Items = { ret.ExteriorCell }
                }
            }
        });
        ret.Quest.Scenes.Add(ret.Scene);
        ret.Quest.DialogBranches.Add(ret.Branch);
        ret.Quest.DialogTopics.Add(ret.Topic);
        ret.Topic.Responses.Add(ret.Response);
        ret.Topic.Responses.Add(ret.OtherResponse);
        return ret;
    }

    [Fact]
    public void Skyrim_Typeless_WorldspaceChildren()
    {
        var w = GetSkyrimWorld();
        w.Mod.Remove(w.ExteriorRef.FormKey);
        Assert.False(Contains(w.Mod, w.ExteriorRef.FormKey));
        Assert.True(Contains(w.Mod, w.OtherExteriorRef.FormKey));
        Assert.True(Contains(w.Mod, w.ExteriorCell.FormKey));

        w.Mod.Remove(w.TopCellRef.FormKey);
        Assert.False(Contains(w.Mod, w.TopCellRef.FormKey));
        Assert.Same(w.TopCell, w.Worldspace.TopCell);

        w.Mod.Remove(w.ExteriorCell.FormKey);
        Assert.False(Contains(w.Mod, w.ExteriorCell.FormKey));
        Assert.False(Contains(w.Mod, w.OtherExteriorRef.FormKey));

        w.Mod.Remove(w.TopCell.FormKey);
        Assert.Null(w.Worldspace.TopCell);
        Assert.True(Contains(w.Mod, w.Worldspace.FormKey));
    }

    [Fact]
    public void Skyrim_Typeless_TopicResponses()
    {
        var w = GetSkyrimWorld();
        w.Mod.Remove(w.Response.FormKey);
        Assert.Equal(new[] { w.OtherResponse }, w.Topic.Responses);
        Assert.True(Contains(w.Mod, w.Topic.FormKey));
    }

    [Fact]
    public void Skyrim_MajorRecordType_WorldspaceChildren()
    {
        var w = GetSkyrimWorld();
        w.Mod.Remove(w.ExteriorRef.FormKey, typeof(IMajorRecordGetter));
        Assert.False(Contains(w.Mod, w.ExteriorRef.FormKey));
        Assert.True(Contains(w.Mod, w.OtherExteriorRef.FormKey));
    }

    [Fact]
    public void Skyrim_AspectInterface_NestedCell()
    {
        var w = GetSkyrimWorld();
        w.Mod.Remove(w.ExteriorCell.FormKey, typeof(INamedGetter));
        Assert.False(Contains(w.Mod, w.ExteriorCell.FormKey));
        Assert.True(Contains(w.Mod, w.TopCell.FormKey));
        Assert.True(Contains(w.Mod, w.Worldspace.FormKey));
    }

    [Fact]
    public void Starfield_Typeless_WorldspaceChildren()
    {
        var w = GetStarfieldWorld();
        w.Mod.Remove(w.TopCellNpc.FormKey);
        Assert.False(Contains(w.Mod, w.TopCellNpc.FormKey));
        Assert.Same(w.TopCell, w.Worldspace.TopCell);

        w.Mod.Remove(w.ExteriorNpc.FormKey);
        Assert.False(Contains(w.Mod, w.ExteriorNpc.FormKey));
        Assert.True(Contains(w.Mod, w.ExteriorRef.FormKey));

        w.Mod.Remove(w.ExteriorCell.FormKey);
        Assert.False(Contains(w.Mod, w.ExteriorCell.FormKey));
        Assert.False(Contains(w.Mod, w.ExteriorRef.FormKey));
        Assert.True(Contains(w.Mod, w.Worldspace.FormKey));
    }

    [Fact]
    public void Starfield_Typeless_QuestChildren()
    {
        var w = GetStarfieldWorld();
        w.Mod.Remove(w.Response.FormKey);
        Assert.Equal(new[] { w.OtherResponse }, w.Topic.Responses);

        w.Mod.Remove(w.Scene.FormKey);
        Assert.Empty(w.Quest.Scenes);

        w.Mod.Remove(w.Branch.FormKey);
        Assert.Empty(w.Quest.DialogBranches);

        w.Mod.Remove(w.Topic.FormKey);
        Assert.Empty(w.Quest.DialogTopics);
        Assert.True(Contains(w.Mod, w.Quest.FormKey));
    }

    [Fact]
    public void Starfield_MajorRecordType_QuestChildren()
    {
        var w = GetStarfieldWorld();
        w.Mod.Remove(w.Response.FormKey, typeof(IMajorRecordGetter));
        Assert.Equal(new[] { w.OtherResponse }, w.Topic.Responses);
    }

    [Fact]
    public void Starfield_LinkInterface_References()
    {
        var w = GetStarfieldWorld();
        Assert.IsAssignableFrom<SF.IKeywordLinkedReferenceGetter>(w.InteriorRef);
        w.Mod.Remove(w.InteriorRef.FormKey, typeof(SF.IKeywordLinkedReferenceGetter));
        Assert.False(Contains(w.Mod, w.InteriorRef.FormKey));
        Assert.True(Contains(w.Mod, w.OtherInteriorRef.FormKey));

        w.Mod.Remove<SF.IKeywordLinkedReferenceGetter>(w.ExteriorRef.FormKey);
        Assert.False(Contains(w.Mod, w.ExteriorRef.FormKey));
        Assert.True(Contains(w.Mod, w.ExteriorNpc.FormKey));

        w.Mod.Remove(w.TopCellNpc.FormKey, typeof(SF.IKeywordLinkedReference));
        Assert.False(Contains(w.Mod, w.TopCellNpc.FormKey));
    }

    [Fact]
    public void Starfield_LinkInterface_OnCell()
    {
        var w = GetStarfieldWorld();
        w.InteriorCell.Remove(w.InteriorRef.FormKey, typeof(SF.IKeywordLinkedReferenceGetter));
        Assert.Equal(new[] { w.OtherInteriorRef }, w.InteriorCell.Temporary);
    }

    [Fact]
    public void Starfield_LinkInterface_OtherTypeUntouched()
    {
        var w = GetStarfieldWorld();
        Assert.False(w.Weapon is SF.IKeywordLinkedReferenceGetter);
        w.Mod.Remove(w.Weapon.FormKey, typeof(SF.IKeywordLinkedReferenceGetter));
        Assert.True(Contains(w.Mod, w.Weapon.FormKey));
    }

    [Fact]
    public void Starfield_AspectInterface()
    {
        var w = GetStarfieldWorld();
        w.Mod.Remove(w.Weapon.FormKey, typeof(INamedGetter));
        Assert.Equal(new[] { w.OtherWeapon }, w.Mod.Weapons);

        w.Mod.Remove(w.ExteriorCell.FormKey, typeof(INamed));
        Assert.False(Contains(w.Mod, w.ExteriorCell.FormKey));
        Assert.True(Contains(w.Mod, w.TopCell.FormKey));
    }

    [Fact]
    public void Starfield_AspectInterface_OtherTypeUntouched()
    {
        var w = GetStarfieldWorld();
        Assert.False(w.Global is INamedGetter);
        w.Mod.Remove(w.Global.FormKey, typeof(INamedGetter));
        Assert.True(Contains(w.Mod, w.Global.FormKey));
    }

    [Fact]
    public void Skyrim_Typed_TopCell()
    {
        var w = GetSkyrimWorld();
        w.Mod.Remove(w.TopCell.FormKey, typeof(SK.ICellGetter));
        Assert.Null(w.Worldspace.TopCell);
        Assert.False(Contains(w.Mod, w.TopCell.FormKey));
        Assert.True(Contains(w.Mod, w.ExteriorCell.FormKey));
    }

    [Fact]
    public void Skyrim_Typed_TopCell_OnWorldspace()
    {
        var w = GetSkyrimWorld();
        w.Worldspace.Remove(w.TopCell.FormKey, typeof(SK.ICellGetter));
        Assert.Null(w.Worldspace.TopCell);
        Assert.True(Contains(w.Mod, w.ExteriorCell.FormKey));
    }

    [Fact]
    public void Skyrim_Typed_Landscape()
    {
        var w = GetSkyrimWorld();
        var landscape = new SK.Landscape(w.Mod.GetNextFormKey(), SK.SkyrimRelease.SkyrimSE);
        w.ExteriorCell.Landscape = landscape;
        w.Mod.Remove(landscape.FormKey, typeof(SK.ILandscapeGetter));
        Assert.Null(w.ExteriorCell.Landscape);
        Assert.True(Contains(w.Mod, w.ExteriorCell.FormKey));
    }

    [Fact]
    public void Starfield_Typed_TopCell()
    {
        var w = GetStarfieldWorld();
        w.Mod.Remove(w.TopCell.FormKey, typeof(SF.ICellGetter));
        Assert.Null(w.Worldspace.TopCell);
        Assert.False(Contains(w.Mod, w.TopCell.FormKey));
        Assert.True(Contains(w.Mod, w.ExteriorCell.FormKey));
    }

    [Fact]
    public void Starfield_Group_Typeless()
    {
        var w = GetStarfieldWorld();
        ((IMajorRecordEnumerable)w.Mod.Worldspaces).Remove(w.ExteriorNpc.FormKey);
        Assert.False(Contains(w.Mod, w.ExteriorNpc.FormKey));
        Assert.True(Contains(w.Mod, w.ExteriorRef.FormKey));

        ((IMajorRecordEnumerable)w.Mod.Quests).Remove(new[] { w.Response.FormKey });
        Assert.Equal(new[] { w.OtherResponse }, w.Topic.Responses);

        ((IMajorRecordEnumerable)w.Mod.Weapons).Remove(w.Weapon.FormKey);
        Assert.Equal(new[] { w.OtherWeapon }, w.Mod.Weapons);
    }

    [Fact]
    public void Starfield_Group_MajorRecordType()
    {
        var w = GetStarfieldWorld();
        w.Mod.Weapons.Remove(w.Weapon.FormKey, typeof(IMajorRecordGetter));
        Assert.Equal(new[] { w.OtherWeapon }, w.Mod.Weapons);

        w.Mod.Quests.Remove(w.Scene.FormKey, typeof(IMajorRecordGetter));
        Assert.Empty(w.Quest.Scenes);
        Assert.True(Contains(w.Mod, w.Quest.FormKey));
    }

    [Fact]
    public void Skyrim_ListedLinkInterface_Cells()
    {
        var w = GetSkyrimWorld();
        Assert.IsAssignableFrom<SK.IComplexLocationGetter>(w.TopCell);
        w.Mod.Remove(w.TopCell.FormKey, typeof(SK.IComplexLocationGetter));
        Assert.Null(w.Worldspace.TopCell);
        Assert.False(Contains(w.Mod, w.TopCellRef.FormKey));
        Assert.True(Contains(w.Mod, w.ExteriorCell.FormKey));

        w.Mod.Remove(w.ExteriorCell.FormKey, typeof(SK.IComplexLocation));
        Assert.False(Contains(w.Mod, w.ExteriorCell.FormKey));
        Assert.False(Contains(w.Mod, w.ExteriorRef.FormKey));
        Assert.True(Contains(w.Mod, w.Worldspace.FormKey));
    }

    [Fact]
    public void Skyrim_ListedLinkInterface_Cells_OnWorldspace()
    {
        var w = GetSkyrimWorld();
        w.Worldspace.Remove(w.ExteriorCell.FormKey, typeof(SK.IComplexLocationGetter));
        Assert.False(Contains(w.Mod, w.ExteriorCell.FormKey));
        w.Worldspace.Remove(w.TopCell.FormKey, typeof(SK.IComplexLocationGetter));
        Assert.Null(w.Worldspace.TopCell);
    }

    [Fact]
    public void Starfield_ListedLinkInterface_InteriorCell()
    {
        var w = GetStarfieldWorld();
        Assert.IsAssignableFrom<SF.ICellOrObjectGetter>(w.InteriorCell);
        w.Mod.Remove(w.InteriorCell.FormKey, typeof(SF.ICellOrObjectGetter));
        Assert.False(Contains(w.Mod, w.InteriorCell.FormKey));
        Assert.False(Contains(w.Mod, w.InteriorRef.FormKey));
        Assert.Empty(w.Mod.Cells.Records.SelectMany(b => b.SubBlocks));
        Assert.True(Contains(w.Mod, w.ExteriorCell.FormKey));
        Assert.True(Contains(w.Mod, w.TopCell.FormKey));
    }

    [Fact]
    public void Starfield_ListedLinkInterface_TopCell()
    {
        foreach (var type in new[] { typeof(SF.ICellOrObjectGetter), typeof(SF.IComplexLocationGetter), typeof(SF.ISpaceCellSpawnGetter) })
        {
            var w = GetStarfieldWorld();
            w.Mod.Remove(w.TopCell.FormKey, type);
            Assert.Null(w.Worldspace.TopCell);
            Assert.False(Contains(w.Mod, w.TopCellNpc.FormKey));
            Assert.True(Contains(w.Mod, w.ExteriorCell.FormKey));
            Assert.True(Contains(w.Mod, w.Worldspace.FormKey));
        }
    }

    [Fact]
    public void Starfield_ListedLinkInterface_ExteriorCell()
    {
        foreach (var type in new[] { typeof(SF.ICellOrObjectGetter), typeof(SF.IComplexLocationGetter) })
        {
            var w = GetStarfieldWorld();
            w.Mod.Remove(w.ExteriorCell.FormKey, type);
            Assert.False(Contains(w.Mod, w.ExteriorCell.FormKey));
            Assert.False(Contains(w.Mod, w.ExteriorRef.FormKey));
            Assert.Same(w.TopCell, w.Worldspace.TopCell);
            Assert.True(Contains(w.Mod, w.Worldspace.FormKey));

            w = GetStarfieldWorld();
            w.Worldspace.Remove(w.ExteriorCell.FormKey, type);
            Assert.False(Contains(w.Mod, w.ExteriorCell.FormKey));
            Assert.Same(w.TopCell, w.Worldspace.TopCell);
        }
    }

    [Fact]
    public void Starfield_ListedLinkInterface_ReferenceOfOtherTypeUntouched()
    {
        var w = GetStarfieldWorld();
        var npc = new SF.PlacedNpc(w.Mod.GetNextFormKey(), SF.StarfieldRelease.Starfield);
        Assert.False(npc is SF.ICellOrObjectGetter);
        w.InteriorCell.Temporary.Add(npc);
        w.InteriorCell.Remove(new[] { npc.FormKey, w.InteriorRef.FormKey }, typeof(SF.ICellOrObjectGetter));
        Assert.Equal(new SF.IPlaced[] { w.OtherInteriorRef, npc }, w.InteriorCell.Temporary);

        w.Mod.Remove(w.ExteriorNpc.FormKey, typeof(SF.ICellOrObjectGetter));
        Assert.True(Contains(w.Mod, w.ExteriorNpc.FormKey));
    }

    [Fact]
    public void Fallout4_ListedLinkInterface_Cells()
    {
        var release = F4.Fallout4Release.Fallout4;
        var mod = new F4.Fallout4Mod(TestConstants.PluginModKey, release);
        var worldspace = mod.Worldspaces.AddNew();
        var topCell = new F4.Cell(mod.GetNextFormKey(), release);
        worldspace.TopCell = topCell;
        var exteriorCell = new F4.Cell(mod.GetNextFormKey(), release);
        var landscape = new F4.Landscape(mod.GetNextFormKey(), release);
        exteriorCell.Landscape = landscape;
        worldspace.SubCells.Add(new F4.WorldspaceBlock() { Items = { new F4.WorldspaceSubBlock() { Items = { exteriorCell } } } });

        Assert.IsAssignableFrom<F4.IPreCutMapEntryReferenceGetter>(landscape);
        mod.Remove(landscape.FormKey, typeof(F4.IPreCutMapEntryReferenceGetter));
        Assert.Null(exteriorCell.Landscape);

        mod.Remove(exteriorCell.FormKey, typeof(F4.IComplexLocationGetter));
        Assert.False(Contains(mod, exteriorCell.FormKey));
        mod.Remove(topCell.FormKey, typeof(F4.IComplexLocationGetter));
        Assert.Null(worldspace.TopCell);
        Assert.True(Contains(mod, worldspace.FormKey));
    }

    [Fact]
    public void Oblivion_ListedLinkInterface_Landscape()
    {
        var release = OB.OblivionRelease.Oblivion;
        var mod = new OB.OblivionMod(TestConstants.PluginModKey, release);
        var worldspace = mod.Worldspaces.AddNew();
        var exteriorCell = new OB.Cell(mod.GetNextFormKey(), release);
        var landscape = new OB.Landscape(mod.GetNextFormKey(), release);
        exteriorCell.Landscape = landscape;
        worldspace.SubCells.Add(new OB.WorldspaceBlock() { Items = { new OB.WorldspaceSubBlock() { Items = { exteriorCell } } } });

        Assert.IsAssignableFrom<OB.IPlacedGetter>(landscape);
        mod.Remove(landscape.FormKey, typeof(OB.IPlacedGetter));
        Assert.Null(exteriorCell.Landscape);
        Assert.True(Contains(mod, exteriorCell.FormKey));

        exteriorCell.Landscape = landscape;
        exteriorCell.Remove(landscape.FormKey, typeof(OB.IPlaced));
        Assert.Null(exteriorCell.Landscape);
    }
}
#pragma warning restore CS0618