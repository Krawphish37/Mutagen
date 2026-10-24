using System.IO.Abstractions;
using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Analysis.DI;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Mutagen.Bethesda.UnitTests.Plugins.Masters;
using Noggog;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Analysis;

/// <summary>
/// With separated masters (Starfield), a mod can only hold 252 Full masters plus itself, as Full FormIDs end
/// below the 0xFD Medium marker.  Splitting has to keep each mod fragment within that, not just the total limit.
/// </summary>
public class SeparatedMastersSplitTests
{
    private static readonly ModKey InputKey = ModKey.FromFileName("Input.esm");

    private static ModKey Key(MasterStyle style, int i) => ModKey.FromFileName($"{style}{i:000}.esm");

    /// <summary>
    /// One new FormList per group of masters, each linking to a record in every master of its group
    /// </summary>
    private static (StarfieldMod Mod, LoadOrder<IModFlagsGetter> LoadOrder) Build(
        int lists, int fullPerList, int smallPerList = 0)
    {
        var mod = new StarfieldMod(InputKey, StarfieldRelease.Starfield);
        var lo = new LoadOrder<IModFlagsGetter>();
        int full = 0, small = 0;
        for (int l = 0; l < lists; l++)
        {
            var list = mod.FormLists.AddNew($"List{l}");
            for (int j = 0; j < fullPerList; j++, full++)
            {
                var key = Key(MasterStyle.Full, full);
                lo.Add(MastersTestUtil.GetFlags(key, MasterStyle.Full));
                list.Items.Add(new FormKey(key, 0x800).ToLink<IStarfieldMajorRecordGetter>());
            }
            for (int j = 0; j < smallPerList; j++, small++)
            {
                var key = Key(MasterStyle.Small, small);
                lo.Add(MastersTestUtil.GetFlags(key, MasterStyle.Small));
                list.Items.Add(new FormKey(key, 0x800).ToLink<IStarfieldMajorRecordGetter>());
            }
        }
        lo.Add(MastersTestUtil.GetFlags(InputKey, MasterStyle.Full));
        return (mod, lo);
    }

    private static int FullMasters(IModGetter mod)
    {
        return mod.EnumerateFormLinks()
            .Select(x => x.FormKey.ModKey)
            .Where(x => x != mod.ModKey)
            .Distinct()
            .Count(x => x.Name.StartsWith(nameof(MasterStyle.Full)));
    }

    [Fact]
    public void SplitKeepsFullMastersWithinFullIndices()
    {
        // 11 x 23 = 253 Full masters: within the 254 total, but one more than fits next to the mod's own index
        var (mod, lo) = Build(lists: 11, fullPerList: 23);

        var split = new MultiModFileSplitter().Split<IStarfieldMod, IStarfieldModGetter>(mod, 254, lo);

        split.Count.ShouldBe(2);
        foreach (var fragment in split)
        {
            FullMasters(fragment).ShouldBeLessThanOrEqualTo(252);
        }
        split.SelectMany(x => x.FormLists).Select(x => x.EditorID)
            .ShouldBe(mod.FormLists.Select(x => x.EditorID), ignoreOrder: true);
    }

    [Fact]
    public void SplitWithoutLookupOnlyCountsTotal()
    {
        var (mod, _) = Build(lists: 11, fullPerList: 23);

        new MultiModFileSplitter().Split<IStarfieldMod, IStarfieldModGetter>(mod, 254)
            .Count.ShouldBe(1);
    }

    [Fact]
    public void SmallMastersDoNotCountTowardFullIndices()
    {
        // 10 x (20 Full + 5 Small) = 200 Full and 50 Small masters, 250 in all: fits one mod
        var (mod, lo) = Build(lists: 10, fullPerList: 20, smallPerList: 5);

        new MultiModFileSplitter().Split<IStarfieldMod, IStarfieldModGetter>(mod, 254, lo)
            .Count.ShouldBe(1);
    }

    [Theory, MutagenAutoData]
    public void AutoSplitWritesFullMasterOverflow(
        IFileSystem fileSystem,
        DirectoryPath existingDir)
    {
        var (mod, lo) = Build(lists: 11, fullPerList: 23);
        var path = Path.Combine(existingDir, InputKey.FileName);

        new AutoSplitModWriter(new MultiModFileSplitter()).Write<IStarfieldMod, IStarfieldModGetter>(
            mod,
            path,
            BinaryWriteParameters.Default with
            {
                FileSystem = fileSystem,
                MasterFlagsLookup = lo,
            });

        var secondPath = Path.Combine(existingDir, "Input_2.esm");
        fileSystem.File.Exists(secondPath).ShouldBeTrue();

        var reimportedLists = new List<IFormListGetter>();
        foreach (var fragmentPath in new[] { path, secondPath })
        {
            using var fragment = StarfieldMod.CreateFromBinaryOverlay(fragmentPath, StarfieldRelease.Starfield, new BinaryReadParameters()
            {
                FileSystem = fileSystem,
                MasterFlagsLookup = lo
            });
            fragment.ModHeader.MasterReferences.Count.ShouldBeLessThanOrEqualTo(252);
            reimportedLists.AddRange(fragment.FormLists.Select(x => (IFormListGetter)x.DeepCopy()));
        }

        reimportedLists.Count.ShouldBe(mod.FormLists.Count);
        foreach (var original in mod.FormLists)
        {
            var reimported = reimportedLists.Single(x => x.EditorID == original.EditorID);
            reimported.Items.Select(x => x.FormKey).ShouldBe(original.Items.Select(x => x.FormKey));
        }
    }

    [Theory, MutagenAutoData]
    public void WriteBuilderAutoSplitWritesFullMasterOverflow(
        IFileSystem fileSystem,
        DirectoryPath existingDir)
    {
        var (mod, lo) = Build(lists: 11, fullPerList: 23);
        var path = Path.Combine(existingDir, InputKey.FileName);

        mod.BeginWrite
            .ToPath(path)
            .WithLoadOrder(lo)
            .WithFileSystem(fileSystem)
            .WithAutoSplit()
            .Write();

        var secondPath = Path.Combine(existingDir, "Input_2.esm");
        fileSystem.File.Exists(secondPath).ShouldBeTrue();
        var lists = 0;
        foreach (var fragmentPath in new[] { path, secondPath })
        {
            using var fragment = StarfieldMod.CreateFromBinaryOverlay(fragmentPath, StarfieldRelease.Starfield, new BinaryReadParameters()
            {
                FileSystem = fileSystem,
                MasterFlagsLookup = lo
            });
            fragment.ModHeader.MasterReferences.Count.ShouldBeLessThanOrEqualTo(252);
            foreach (var list in fragment.FormLists)
            {
                list.FormKey.ModKey.ShouldBe(fragment.ModKey);
                lists++;
            }
        }
        lists.ShouldBe(mod.FormLists.Count);
    }

    [Fact]
    public void SingleRecordWithTooManyFullMastersThrows()
    {
        // One list linking 253 Full masters cannot be split up
        var (mod, lo) = Build(lists: 1, fullPerList: 253);

        Should.Throw<TooManyMastersException>(() =>
            new MultiModFileSplitter().Split<IStarfieldMod, IStarfieldModGetter>(mod, 254, lo));
    }
}