using System.IO.Abstractions;
using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins;

public class InteriorCellCountWritingTests
{
    #region Helpers

    private static Cell AddInteriorCell(StarfieldMod mod)
    {
        var cell = new Cell(mod)
        {
            Flags = Cell.Flag.IsInteriorCell
        };
        mod.Cells.Add(new CellBlock()
        {
            SubBlocks = new ExtendedList<CellSubBlock>()
            {
                new CellSubBlock()
                {
                    Cells = new ExtendedList<Cell>()
                    {
                        cell
                    }
                }
            }
        });
        return cell;
    }

    private static Cell AddWorldspaceCell(StarfieldMod mod)
    {
        var cell = new Cell(mod);
        var worldspace = mod.Worldspaces.AddReturn(new Worldspace(mod));
        worldspace.SubCells.Add(new WorldspaceBlock()
        {
            Items = new ExtendedList<WorldspaceSubBlock>()
            {
                new WorldspaceSubBlock()
                {
                    Items = new ExtendedList<Cell>()
                    {
                        cell
                    }
                }
            }
        });
        return cell;
    }

    private static int? WriteAndGetInteriorCellCount(
        IFileSystem fileSystem,
        ModPath path,
        IStarfieldModGetter mod,
        RecordCountOption recordCount = RecordCountOption.Iterate,
        params IModGetter[] masters)
    {
        mod.BeginWrite
            .ToPath(path)
            .WithLoadOrderFromHeaderMasters()
            .WithKnownMasters(masters)
            .WithFileSystem(fileSystem)
            .NoModKeySync()
            .WithRecordCount(recordCount)
            .Write();

        using var reimport = StarfieldMod.Create(StarfieldRelease.Starfield)
            .FromPath(path)
            .WithKnownMasters(masters)
            .WithFileSystem(fileSystem)
            .Construct();
        return reimport.ModHeader.InteriorCellCount;
    }

    #endregion

    [Theory, MutagenAutoData]
    public void CountsNewInteriorCells(
        IFileSystem fileSystem,
        ModPath existingModPath)
    {
        var mod = new StarfieldMod(TestConstants.PluginModKey, StarfieldRelease.Starfield);
        AddInteriorCell(mod);
        AddInteriorCell(mod);
        AddInteriorCell(mod);
        AddWorldspaceCell(mod);

        WriteAndGetInteriorCellCount(fileSystem, existingModPath, mod)
            .ShouldBe(3);
    }

    [Theory, MutagenAutoData]
    public void CountsInteriorCellOverrides(
        IFileSystem fileSystem,
        ModPath existingModPath)
    {
        var master = new StarfieldMod(TestConstants.PluginModKey2, StarfieldRelease.Starfield);
        var masterInteriorCell = AddInteriorCell(master);
        var masterWorldspaceCell = AddWorldspaceCell(master);
        var masterLinkCache = master.ToImmutableLinkCache();

        var mod = new StarfieldMod(TestConstants.PluginModKey, StarfieldRelease.Starfield);
        masterLinkCache.ResolveContext<ICell, ICellGetter>(masterInteriorCell.FormKey)
            .GetOrAddAsOverride(mod);
        masterLinkCache.ResolveContext<ICell, ICellGetter>(masterWorldspaceCell.FormKey)
            .GetOrAddAsOverride(mod);
        AddInteriorCell(mod);

        WriteAndGetInteriorCellCount(fileSystem, existingModPath, mod, masters: master)
            .ShouldBe(2);
    }

    [Theory, MutagenAutoData]
    public void CountsDeletedInteriorCells(
        IFileSystem fileSystem,
        ModPath existingModPath)
    {
        var mod = new StarfieldMod(TestConstants.PluginModKey, StarfieldRelease.Starfield);
        AddInteriorCell(mod);
        AddInteriorCell(mod).IsDeleted = true;

        WriteAndGetInteriorCellCount(fileSystem, existingModPath, mod)
            .ShouldBe(2);
    }

    [Theory, MutagenAutoData]
    public void ReplacesStaleInteriorCellCount(
        IFileSystem fileSystem,
        ModPath existingModPath)
    {
        var mod = new StarfieldMod(TestConstants.PluginModKey, StarfieldRelease.Starfield);
        mod.ModHeader.InteriorCellCount = 7;
        AddInteriorCell(mod);

        WriteAndGetInteriorCellCount(fileSystem, existingModPath, mod)
            .ShouldBe(1);
        mod.ModHeader.InteriorCellCount.ShouldBe(7);
    }

    [Theory, MutagenAutoData]
    public void ExistingInteriorCellCountZeroedWithoutCells(
        IFileSystem fileSystem,
        ModPath existingModPath)
    {
        var mod = new StarfieldMod(TestConstants.PluginModKey, StarfieldRelease.Starfield);
        mod.ModHeader.InteriorCellCount = 5;
        AddWorldspaceCell(mod);

        WriteAndGetInteriorCellCount(fileSystem, existingModPath, mod)
            .ShouldBe(0);
    }

    [Theory, MutagenAutoData]
    public void NoInteriorCellCountWithoutCells(
        IFileSystem fileSystem,
        ModPath existingModPath)
    {
        var mod = new StarfieldMod(TestConstants.PluginModKey, StarfieldRelease.Starfield);
        mod.Npcs.AddNew();
        AddWorldspaceCell(mod);

        WriteAndGetInteriorCellCount(fileSystem, existingModPath, mod)
            .ShouldBeNull();
    }

    [Theory, MutagenAutoData]
    public void RecordCountNoCheckKeepsInteriorCellCount(
        IFileSystem fileSystem,
        ModPath existingModPath)
    {
        var mod = new StarfieldMod(TestConstants.PluginModKey, StarfieldRelease.Starfield);
        mod.ModHeader.InteriorCellCount = 7;
        AddInteriorCell(mod);

        WriteAndGetInteriorCellCount(fileSystem, existingModPath, mod, RecordCountOption.NoCheck)
            .ShouldBe(7);
    }

    [Theory, MutagenAutoData]
    public void OverlayWriteRecountsInteriorCells(
        IFileSystem fileSystem,
        ModPath existingModPath)
    {
        var mod = new StarfieldMod(TestConstants.PluginModKey, StarfieldRelease.Starfield);
        mod.ModHeader.InteriorCellCount = 9;
        AddInteriorCell(mod);
        AddInteriorCell(mod);
        WriteAndGetInteriorCellCount(fileSystem, existingModPath, mod, RecordCountOption.NoCheck)
            .ShouldBe(9);

        using var overlay = StarfieldMod.Create(StarfieldRelease.Starfield)
            .FromPath(existingModPath)
            .WithKnownMasters(Array.Empty<IModMasterStyledGetter>())
            .WithFileSystem(fileSystem)
            .Construct();
        var rewrittenPath = new ModPath(existingModPath.ModKey, Path.Combine(Path.GetDirectoryName(existingModPath.Path)!, "Rewritten.esp"));
        WriteAndGetInteriorCellCount(fileSystem, rewrittenPath, overlay)
            .ShouldBe(2);
    }
}
