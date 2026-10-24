using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog;
using System.IO.Abstractions;
using Starfield = Mutagen.Bethesda.Starfield;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

public class BinaryWriteBuilderAutoSplitTests
{
    [Theory, MutagenModAutoData]
    public void WithAutoSplit_EnablesAutoSplitting(SkyrimMod mod, DirectoryPath existingOutputDirectory, IFileSystem fileSystem)
    {
        // Combine mod's ModKey and DirectoryPath to create a valid output path
        var outputPath = Path.Combine(existingOutputDirectory.Path, mod.ModKey.FileName);

        // Add a simple record so the mod isn't empty
        var misc = mod.MiscItems.AddNew();
        misc.EditorID = "TestItem";

        // Use the builder with auto-split enabled
        mod.BeginWrite
            .ToPath(outputPath, fileSystem)
            .WithNoLoadOrder()
            .WithAutoSplit()
            .Write();

        // File should be written
        fileSystem.File.Exists(outputPath).ShouldBeTrue();
    }

    [Theory, MutagenModAutoData]
    public void WithAutoSplit_SplitsModWithTooManyMasters(SkyrimMod mod, DirectoryPath existingOutputDirectory, IFileSystem fileSystem)
    {
        // Combine mod's ModKey and DirectoryPath to create a valid output path
        var outputPath = Path.Combine(existingOutputDirectory.Path, mod.ModKey.FileName);

        // Create a mod that would exceed master limits
        // Add form lists that reference many different mods
        var originalFormLists = new List<IFormListGetter>();
        for (int i = 0; i < 10; i++)
        {
            var formList = mod.FormLists.AddNew();
            // Set EditorID to FormKey for tracking after split
            formList.EditorID = formList.FormKey.ToString();

            // Add items from different master files (simulating many masters)
            for (uint j = 0; j < 30; j++)
            {
                var masterKey = new ModKey($"Master_{i}_{j}", ModType.Plugin);
                formList.Items.Add(new FormKey(masterKey, 0x800 + j));
            }

            originalFormLists.Add(formList);
        }

        // Write with auto-split
        mod.BeginWrite
            .ToPath(outputPath, fileSystem)
            .WithNoLoadOrder()
            .WithAutoSplit()
            .Write();

        // Verify split files were created
        // First file is the base path (no suffix), subsequent files have _2, _3, etc.
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(mod.ModKey.FileName);
        var extension = Path.GetExtension(mod.ModKey.FileName);
        var splitFile1Path = outputPath;  // First file is the base path
        var splitFile2Path = Path.Combine(existingOutputDirectory.Path, $"{fileNameWithoutExtension}_2{extension}");

        fileSystem.File.Exists(splitFile1Path).ShouldBeTrue();
        fileSystem.File.Exists(splitFile2Path).ShouldBeTrue();

        // Re-import the split mods and verify content
        var splitMod1 = SkyrimMod.CreateFromBinaryOverlay(
            splitFile1Path,
            (SkyrimRelease)mod.GameRelease,
            new BinaryReadParameters() { FileSystem = fileSystem });
        var splitMod2 = SkyrimMod.CreateFromBinaryOverlay(
            splitFile2Path,
            (SkyrimRelease)mod.GameRelease,
            new BinaryReadParameters() { FileSystem = fileSystem });

        // Collect all FormLists from both split mods
        var reimportedFormLists = new List<IFormListGetter>();
        reimportedFormLists.AddRange(splitMod1.FormLists);
        reimportedFormLists.AddRange(splitMod2.FormLists);

        // Verify we have the same number of FormLists
        reimportedFormLists.Count.ShouldBe(originalFormLists.Count);

        // Verify each FormList by EditorID (which is set to FormKey)
        var expectedEdids = originalFormLists.Select(f => f.EditorID).ToHashSet();
        var reimportedEdids = reimportedFormLists.Select(f => f.EditorID).ToHashSet();

        reimportedEdids.ShouldBe(expectedEdids);

        // Verify the content of each FormList matches
        foreach (var originalFormList in originalFormLists)
        {
            var reimportedFormList = reimportedFormLists.FirstOrDefault(f => f.EditorID == originalFormList.EditorID);
            reimportedFormList.ShouldNotBeNull();

            // Verify the items are the same
            reimportedFormList.Items.Count.ShouldBe(originalFormList.Items.Count);
            for (int i = 0; i < originalFormList.Items.Count; i++)
            {
                reimportedFormList.Items[i].FormKey.ShouldBe(originalFormList.Items[i].FormKey);
            }
        }
    }

    [Theory, MutagenModAutoData]
    public void WithAutoSplit_ThrowsWhenSingleRecordExceedsMasterLimit(
        SkyrimMod mod,
        DirectoryPath existingOutputDirectory,
        IFileSystem fileSystem)
    {
        var outputPath = Path.Combine(existingOutputDirectory.Path, mod.ModKey.FileName);

        // Create a single FormList that references more than 255 masters
        // This cannot be split because the record itself exceeds the limit
        var formList = mod.FormLists.AddNew();
        formList.EditorID = "MassiveFormList";

        // Add items from 300 different master files
        for (uint i = 0; i < 300; i++)
        {
            var masterKey = new ModKey($"Master_{i}", ModType.Plugin);
            formList.Items.Add(new FormKey(masterKey, 0x800));
        }

        // Auto-split should still throw because a single record exceeds the limit
        Should.Throw<Mutagen.Bethesda.Plugins.Exceptions.TooManyMastersException>(() =>
        {
            mod.BeginWrite
                .ToPath(outputPath, fileSystem)
                .WithNoLoadOrder()
                .WithAutoSplit()
                .Write();
        });
    }

    [Theory, MutagenModAutoData]
    public void WithAutoSplit_LowerFormKeyPlaceholderEdgeCase(
        ModKey modKey,
        DirectoryPath existingOutputDirectory,
        IFileSystem fileSystem)
    {
        var mod = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE, forceUseLowerFormIDRanges: true);
        var outputPath = Path.Combine(existingOutputDirectory.Path, mod.ModKey.FileName);

        // Create FormLists that reference masters to exceed the master limit
        // Create one FormList per master (256 masters) to efficiently trigger splitting
        for (int i = 0; i < 256; i++)
        {
            var formList = mod.FormLists.AddNew();
            formList.EditorID = $"FormList{i:D3}";

            // Each FormList references a unique master
            var masterKey = new ModKey($"Master{i:D3}.esp", ModType.Plugin);
            formList.Items.Add(new FormKey(masterKey, 0x800));
        }

        // Capture the original FormKeys before splitting
        var originalFormKeys = mod.FormLists.Select(f => f.FormKey).ToHashSet();

        // Verify that records were actually created in the mod (not external)
        foreach (var formKey in originalFormKeys)
        {
            formKey.ModKey.ShouldBe(mod.ModKey, "All FormLists should belong to the mod being split");
        }

        // Write with auto-split
        mod.BeginWrite
            .ToPath(outputPath, fileSystem)
            .WithNoLoadOrder()
            .WithForcedLowerFormIdRangeUsage(true)
            .WithAutoSplit()
            .Write();

        // Verify split files were created
        // First file is the base path (no suffix), subsequent files have _2, _3, etc.
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(mod.ModKey.FileName);
        var extension = Path.GetExtension(mod.ModKey.FileName);
        var splitFile1Path = outputPath;  // First file is the base path
        var splitFile2Path = Path.Combine(existingOutputDirectory.Path, $"{fileNameWithoutExtension}_2{extension}");

        fileSystem.File.Exists(splitFile1Path).ShouldBeTrue();
        fileSystem.File.Exists(splitFile2Path).ShouldBeTrue();

        // Re-import both split mods
        var splitMod1 = SkyrimMod.CreateFromBinaryOverlay(
            splitFile1Path,
            (SkyrimRelease)mod.GameRelease,
            new BinaryReadParameters() { FileSystem = fileSystem });
        var splitMod2 = SkyrimMod.CreateFromBinaryOverlay(
            splitFile2Path,
            (SkyrimRelease)mod.GameRelease,
            new BinaryReadParameters() { FileSystem = fileSystem });

        // Collect all FormKeys from both split mods
        var allFormKeys = new HashSet<FormKey>();
        var splitMod1FormKeys = new List<FormKey>();
        var splitMod2FormKeys = new List<FormKey>();

        foreach (var formList in splitMod1.FormLists)
        {
            splitMod1FormKeys.Add(formList.FormKey);
            // Verify no collision when adding
            allFormKeys.Add(formList.FormKey).ShouldBeTrue(
                $"FormKey collision detected: {formList.FormKey} appears in both split files");
        }
        foreach (var formList in splitMod2.FormLists)
        {
            splitMod2FormKeys.Add(formList.FormKey);
            // Verify no collision when adding
            allFormKeys.Add(formList.FormKey).ShouldBeTrue(
                $"FormKey collision detected: {formList.FormKey} appears in both split files");
        }

        // Debug: Print out the FormKeys to see what's happening
        System.Console.WriteLine($"Split file 1 FormKeys: {string.Join(", ", splitMod1FormKeys)}");
        System.Console.WriteLine($"Split file 2 FormKeys: {string.Join(", ", splitMod2FormKeys)}");

        // Verify all FormLists are accounted for
        allFormKeys.Count.ShouldBe(originalFormKeys.Count,
            "Total number of FormLists across split files should match original");

        // VERIFY CORRECT BEHAVIOR: Split files should have non-overlapping FormID ranges
        // Extract just the FormID (not the full FormKey) from each file
        var splitMod1FormIDs = splitMod1FormKeys.Select(fk => fk.ID).ToHashSet();
        var splitMod2FormIDs = splitMod2FormKeys.Select(fk => fk.ID).ToHashSet();

        // Find overlapping FormIDs
        var overlappingFormIDs = splitMod1FormIDs.Intersect(splitMod2FormIDs).ToList();

        // Debug output to help diagnose issues
        System.Console.WriteLine($"Split file 1 has {splitMod1FormIDs.Count} unique FormIDs");
        System.Console.WriteLine($"Split file 2 has {splitMod2FormIDs.Count} unique FormIDs");
        if (overlappingFormIDs.Any())
        {
            System.Console.WriteLine($"ERROR: {overlappingFormIDs.Count} FormIDs overlap between split files!");
            System.Console.WriteLine($"Overlapping FormIDs: {string.Join(", ", overlappingFormIDs.Select(id => $"0x{id:X6}"))}");
        }

        // ASSERTION: Split files MUST have non-overlapping FormID ranges
        // This prevents issues when:
        // 1. Merging split files back together
        // 2. Loading both files in a load order simultaneously
        // 3. Extending one of the split files with more records
        overlappingFormIDs.ShouldBeEmpty(
            "Split files must have non-overlapping FormID ranges. " +
            $"Found {overlappingFormIDs.Count} overlapping FormIDs: {string.Join(", ", overlappingFormIDs.Select(id => $"0x{id:X6}"))}");
    }

    #region Starfield Split Masters

    /// <summary>
    /// Creates a Starfield mod whose created FormLists split into one file each.
    /// List k links 200 unique external masters plus list k-1, so split file k
    /// must list split file k-1 as a master.
    /// </summary>
    /// <returns>The mod, and per list its FormID and its external links</returns>
    private static (Starfield.StarfieldMod Mod, List<(uint ID, List<FormKey> Items)> Lists, List<ModKey> ExternalMasters) CreateStarfieldSplitChain(
        ModKey modKey,
        int fileCount)
    {
        var mod = new Starfield.StarfieldMod(modKey, Starfield.StarfieldRelease.Starfield);
        var lists = new List<(uint ID, List<FormKey> Items)>();
        var externalMasters = new List<ModKey>();
        Starfield.FormList? previous = null;
        for (int k = 0; k < fileCount; k++)
        {
            var list = mod.FormLists.AddNew($"List{k}");
            var externalItems = new List<FormKey>();
            for (int i = 0; i < 200; i++)
            {
                var masterKey = ModKey.FromFileName($"Master{k}_{i:D3}.esm");
                externalMasters.Add(masterKey);
                externalItems.Add(new FormKey(masterKey, 0x800));
                list.Items.Add(new FormKey(masterKey, 0x800));
            }
            if (previous != null)
            {
                list.Items.Add(previous.FormKey);
            }
            lists.Add((list.FormKey.ID, externalItems));
            previous = list;
        }
        return (mod, lists, externalMasters);
    }

    private static string StarfieldSplitPath(DirectoryPath dir, ModKey modKey, int index)
    {
        var fileName = index == 0
            ? modKey.FileName.String
            : $"{modKey.Name}_{index + 1}{Path.GetExtension(modKey.FileName.String)}";
        return Path.Combine(dir.Path, fileName);
    }

    private static void AssertStarfieldSplitChain(
        DirectoryPath dir,
        ModKey modKey,
        IReadOnlyList<(uint ID, List<FormKey> Items)> lists,
        IEnumerable<IModMasterStyledGetter> readLoadOrder,
        IFileSystem fileSystem)
    {
        var readLookup = readLoadOrder.ToList();
        var splitKeys = StarfieldSplitKeys(dir, modKey, lists.Count).ToList();
        for (int k = 0; k < lists.Count; k++)
        {
            var path = StarfieldSplitPath(dir, modKey, k);
            fileSystem.File.Exists(path).ShouldBeTrue();
            using var split = Starfield.StarfieldMod.CreateFromBinaryOverlay(
                path,
                Starfield.StarfieldRelease.Starfield,
                new BinaryReadParameters
                {
                    FileSystem = fileSystem,
                    MasterFlagsLookup = new Mutagen.Bethesda.Plugins.Order.LoadOrder<IModMasterStyledGetter>(readLookup)
                });
            // Records created in a split file belong to that split file
            var list = split.FormLists.ShouldHaveSingleItem();
            list.FormKey.ShouldBe(new FormKey(splitKeys[k], lists[k].ID));
            var expectedItems = lists[k].Items.ToList();
            if (k > 0)
            {
                // The link to the previous list points into the previous split file
                expectedItems.Add(new FormKey(splitKeys[k - 1], lists[k - 1].ID));
                split.MasterReferences.Select(x => x.Master).ShouldContain(splitKeys[k - 1]);
            }
            list.Items.Select(x => x.FormKey).ShouldBe(expectedItems);
        }
        fileSystem.File.Exists(StarfieldSplitPath(dir, modKey, lists.Count)).ShouldBeFalse();
    }

    private static IEnumerable<ModKey> StarfieldSplitKeys(DirectoryPath dir, ModKey modKey, int fileCount)
    {
        return Enumerable.Range(0, fileCount)
            .Select(i => ModKey.FromFileName(Path.GetFileName(StarfieldSplitPath(dir, modKey, i))));
    }

    [Theory, MutagenModAutoData]
    public void WithAutoSplit_Starfield_LoadOrderWithoutOutputMod_LaterSplitMastersEarlierSplit(
        DirectoryPath existingOutputDirectory,
        IFileSystem fileSystem)
    {
        var modKey = ModKey.FromFileName("Input.esm");
        var (mod, lists, externalMasters) = CreateStarfieldSplitChain(modKey, 2);

        // Writing a new patch: the load order holds the masters but not the mod being written
        IEnumerable<IModMasterStyledGetter> loadOrder = externalMasters
            .Select(x => new KeyedMasterStyle(x, MasterStyle.Full))
            .ToArray();

        mod.BeginWrite
            .ToPath(StarfieldSplitPath(existingOutputDirectory, modKey, 0), fileSystem)
            .WithLoadOrder(loadOrder)
            .WithAutoSplit()
            .Write();

        AssertStarfieldSplitChain(
            existingOutputDirectory, modKey, lists,
            loadOrder.Concat(StarfieldSplitKeys(existingOutputDirectory, modKey, 2)
                .Select(x => new KeyedMasterStyle(x, MasterStyle.Full))),
            fileSystem);
    }

    [Theory, MutagenModAutoData]
    public void WithAutoSplit_Starfield_ThreeSplits_LoadOrderWithOnlyOutputMod(
        DirectoryPath existingOutputDirectory,
        IFileSystem fileSystem)
    {
        var modKey = ModKey.FromFileName("Input.esm");
        var (mod, lists, externalMasters) = CreateStarfieldSplitChain(modKey, 3);

        // The load order can list the mod being written, but not the split files the writer invents
        IEnumerable<IModMasterStyledGetter> loadOrder = externalMasters
            .Append(modKey)
            .Select(x => new KeyedMasterStyle(x, MasterStyle.Full))
            .ToArray();

        mod.BeginWrite
            .ToPath(StarfieldSplitPath(existingOutputDirectory, modKey, 0), fileSystem)
            .WithLoadOrder(loadOrder)
            .WithAutoSplit()
            .Write();

        AssertStarfieldSplitChain(
            existingOutputDirectory, modKey, lists,
            externalMasters.Concat(StarfieldSplitKeys(existingOutputDirectory, modKey, 3))
                .Select(x => new KeyedMasterStyle(x, MasterStyle.Full)),
            fileSystem);
    }

    [Theory, MutagenModAutoData]
    public void WithAutoSplit_Starfield_KnownMastersOnly_LaterSplitMastersEarlierSplit(
        DirectoryPath existingOutputDirectory,
        IFileSystem fileSystem)
    {
        var modKey = ModKey.FromFileName("Input.esm");
        var (mod, lists, externalMasters) = CreateStarfieldSplitChain(modKey, 2);

        var knownMasters = externalMasters
            .Select(x => new KeyedMasterStyle(x, MasterStyle.Full))
            .ToArray();

        mod.BeginWrite
            .ToPath(StarfieldSplitPath(existingOutputDirectory, modKey, 0), fileSystem)
            .WithNoLoadOrder()
            .WithKnownMasters(knownMasters)
            .WithAutoSplit()
            .Write();

        AssertStarfieldSplitChain(
            existingOutputDirectory, modKey, lists,
            knownMasters.Concat(StarfieldSplitKeys(existingOutputDirectory, modKey, 2)
                .Select(x => new KeyedMasterStyle(x, MasterStyle.Full))),
            fileSystem);
    }

    [Theory, MutagenModAutoData]
    public void WithAutoSplit_Starfield_StaleOutputModStyleInLoadOrder_UsesWrittenStyle(
        DirectoryPath existingOutputDirectory,
        IFileSystem fileSystem)
    {
        var modKey = ModKey.FromFileName("Input.esm");
        var (mod, lists, externalMasters) = CreateStarfieldSplitChain(modKey, 2);

        // The load order still describes an older Input.esm that was a small master,
        // but the split files being written are full masters
        IEnumerable<IModMasterStyledGetter> loadOrder = externalMasters
            .Select(x => new KeyedMasterStyle(x, MasterStyle.Full))
            .Append(new KeyedMasterStyle(modKey, MasterStyle.Small))
            .ToArray();

        mod.BeginWrite
            .ToPath(StarfieldSplitPath(existingOutputDirectory, modKey, 0), fileSystem)
            .WithLoadOrder(loadOrder)
            .WithAutoSplit()
            .Write();

        AssertStarfieldSplitChain(
            existingOutputDirectory, modKey, lists,
            externalMasters.Concat(StarfieldSplitKeys(existingOutputDirectory, modKey, 2))
                .Select(x => new KeyedMasterStyle(x, MasterStyle.Full)),
            fileSystem);
    }

    #endregion
}
