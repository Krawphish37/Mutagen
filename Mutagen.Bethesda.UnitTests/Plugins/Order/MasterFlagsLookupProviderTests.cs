using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Shouldly;
using Mutagen.Bethesda.Environments.DI;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Order.DI;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Noggog;
using Noggog.Testing.IO;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Order;

public class MasterFlagsLookupProviderTests
{
    private static readonly string DataFolder = $"{PathingUtil.DrivePrefix}DataFolder";
    private static readonly ModKey MasterKey = ModKey.FromFileName("Master.esm");
    private static readonly ModKey SmallMasterKey = ModKey.FromFileName("SmallMaster.esm");
    private static readonly ModKey ChildKey = ModKey.FromFileName("Child.esp");
    private static readonly ModKey MissingKey = ModKey.FromFileName("NotInstalled.esm");

    private class Setup
    {
        public required IFileSystem FileSystem { get; init; }
        public required FormKey MasterNpc { get; init; }
        public required FormKey SmallMasterNpc { get; init; }
        public required FormKey ChildNpc { get; init; }
    }

    private static Setup WriteMods(bool includeMissingAsMaster = false)
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory(DataFolder);

        var master = new StarfieldMod(MasterKey, StarfieldRelease.Starfield);
        var masterNpc = master.Npcs.AddNew();
        var smallMaster = new StarfieldMod(SmallMasterKey, StarfieldRelease.Starfield);
        smallMaster.ModHeader.Flags |= StarfieldModHeader.HeaderFlag.Light;
        var smallMasterNpc = smallMaster.Npcs.AddNew();
        var missing = new StarfieldMod(MissingKey, StarfieldRelease.Starfield);
        var missingNpc = missing.Npcs.AddNew();

        var child = new StarfieldMod(ChildKey, StarfieldRelease.Starfield);
        var childNpc = child.Npcs.AddNew();
        childNpc.InheritsSoundsFrom.SetTo(masterNpc);
        child.Npcs.GetOrAddAsOverride(smallMasterNpc);
        var masters = new List<IModFlagsGetter> { master, smallMaster };
        if (includeMissingAsMaster)
        {
            child.Npcs.GetOrAddAsOverride(missingNpc);
            masters.Add(missing);
        }

        foreach (var mod in new[] { master, smallMaster })
        {
            mod.BeginWrite
                .ToPath(Path.Combine(DataFolder, mod.ModKey.FileName))
                .WithNoLoadOrder()
                .WithFileSystem(fs)
                .Write();
        }
        child.BeginWrite
            .ToPath(Path.Combine(DataFolder, ChildKey.FileName))
            .WithLoadOrder(new LoadOrder<IModFlagsGetter>(masters))
            .WithFileSystem(fs)
            .Write();

        return new Setup
        {
            FileSystem = fs,
            MasterNpc = masterNpc.FormKey,
            SmallMasterNpc = smallMasterNpc.FormKey,
            ChildNpc = childNpc.FormKey,
        };
    }

    private static MasterFlagsLookupProvider GetProvider(IFileSystem fs)
    {
        return new MasterFlagsLookupProvider(
            new GameReleaseInjection(GameRelease.Starfield),
            fs,
            new DataDirectoryInjection(DataFolder));
    }

    [Fact]
    public void Typical()
    {
        var setup = WriteMods();
        var lookup = GetProvider(setup.FileSystem).Get(new ILoadOrderListingGetter[]
        {
            new LoadOrderListing(MasterKey, true),
            new LoadOrderListing(SmallMasterKey, true),
            new LoadOrderListing(ChildKey, true),
        });
        lookup.ShouldNotBeNull();
        lookup.Items.Select(x => (x.ModKey, x.MasterStyle)).ShouldBe(new[]
        {
            (MasterKey, MasterStyle.Full),
            (SmallMasterKey, MasterStyle.Small),
            (ChildKey, MasterStyle.Full),
        }, ignoreOrder: true);
    }

    [Fact]
    public void MissingListingIsSkipped()
    {
        var setup = WriteMods();
        var lookup = GetProvider(setup.FileSystem).Get(new ILoadOrderListingGetter[]
        {
            new LoadOrderListing(MasterKey, true),
            new LoadOrderListing(MissingKey, true),
            new LoadOrderListing(SmallMasterKey, true),
            new LoadOrderListing(ChildKey, true),
        });
        lookup.ShouldNotBeNull();
        lookup.Items.Select(x => (x.ModKey, x.MasterStyle)).ShouldBe(new[]
        {
            (MasterKey, MasterStyle.Full),
            (SmallMasterKey, MasterStyle.Small),
            (ChildKey, MasterStyle.Full),
        }, ignoreOrder: true);
        lookup.TryGetValue(MissingKey, out _).ShouldBeFalse();
    }

    [Fact]
    public void ImportWithMissingListingLeavesItUnloaded()
    {
        var setup = WriteMods();
        using var lo = LoadOrder.Import<IStarfieldModGetter>(
            DataFolder,
            new[] { MasterKey, MissingKey, SmallMasterKey, ChildKey },
            GameRelease.Starfield,
            setup.FileSystem);

        lo.Count.ShouldBe(4);
        lo[MissingKey].Mod.ShouldBeNull();
        lo[MasterKey].Mod.ShouldNotBeNull();
        lo[SmallMasterKey].Mod.ShouldNotBeNull();
        var child = lo[ChildKey].Mod;
        child.ShouldNotBeNull();
        child.Npcs.Select(x => x.FormKey)
            .ShouldBe(new[] { setup.SmallMasterNpc, setup.ChildNpc }, ignoreOrder: true);
        child.Npcs[setup.ChildNpc].InheritsSoundsFrom.FormKey.ShouldBe(setup.MasterNpc);
    }

    [Fact]
    public void ImportWithMissingMasterThrowsMissingMod()
    {
        var setup = WriteMods(includeMissingAsMaster: true);
        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            using var lo = LoadOrder.Import<IStarfieldModGetter>(
                DataFolder,
                new[] { MasterKey, MissingKey, SmallMasterKey, ChildKey },
                GameRelease.Starfield,
                setup.FileSystem);
        });
        while (ex.InnerException != null)
        {
            ex = ex.InnerException;
        }
        var missingEx = ex.ShouldBeOfType<MissingModException>();
        missingEx.ModPath.ModKey.ShouldBe(MissingKey);
    }
}
