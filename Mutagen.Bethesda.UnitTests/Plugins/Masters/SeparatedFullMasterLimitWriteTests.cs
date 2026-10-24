using System.IO.Abstractions;
using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Masters;

/// <summary>
/// Starfield Full FormIDs can only use top bytes 0x00-0xFC, since 0xFD and 0xFE mark Medium and Small FormIDs.
/// A mod with more Full masters than that must not be written with FormIDs that read back as something else.
/// </summary>
public class SeparatedFullMasterLimitWriteTests
{
    private static readonly ModKey OriginatingKey = ModKey.FromFileName("Originating.esm");

    private static ModKey FullKey(int i) => ModKey.FromFileName($"Full{i:000}.esm");

    private static StarfieldMod NewMod(MasterStyle style)
    {
        var mod = new StarfieldMod(OriginatingKey, StarfieldRelease.Starfield);
        mod.ModHeader.Flags |= style switch
        {
            MasterStyle.Small => StarfieldModHeader.HeaderFlag.Light,
            MasterStyle.Medium => StarfieldModHeader.HeaderFlag.Medium,
            _ => 0
        };
        return mod;
    }

    private static LoadOrder<IModFlagsGetter> LoadOrder(int fullMasters, MasterStyle style)
    {
        var lo = new LoadOrder<IModFlagsGetter>();
        for (int i = 0; i < fullMasters; i++)
        {
            lo.Add(MastersTestUtil.GetFlags(FullKey(i), MasterStyle.Full));
        }
        lo.Add(MastersTestUtil.GetFlags(OriginatingKey, style));
        return lo;
    }

    private static (StarfieldMod Mod, FormList List, LoadOrder<IModFlagsGetter> LoadOrder) Build(int fullMasters)
    {
        var mod = NewMod(MasterStyle.Full);
        var list = mod.FormLists.AddNew(new FormKey(OriginatingKey, 0x012345));
        for (int i = 0; i < fullMasters; i++)
        {
            list.Items.Add(new FormKey(FullKey(i), 0x800).ToLink<IStarfieldMajorRecordGetter>());
        }
        list.Items.Add(list.ToLink<IStarfieldMajorRecordGetter>());
        return (mod, list, LoadOrder(fullMasters, MasterStyle.Full));
    }

    /// <summary>
    /// Only overrides, one per master, so no FormID needs an index for the mod itself
    /// </summary>
    private static (StarfieldMod Mod, LoadOrder<IModFlagsGetter> LoadOrder) BuildOverridesOnly(int fullMasters, MasterStyle style)
    {
        var mod = NewMod(style);
        for (int i = 0; i < fullMasters; i++)
        {
            mod.Keywords.Add(new Keyword(new FormKey(FullKey(i), 0x800), StarfieldRelease.Starfield)
            {
                EditorID = $"Keyword{i}"
            });
        }
        return (mod, LoadOrder(fullMasters, style));
    }

    private static void Write(StarfieldMod mod, LoadOrder<IModFlagsGetter> lo, string modPath, IFileSystem fileSystem, bool singleThread)
    {
        var builder = mod.BeginWrite
            .ToPath(modPath)
            .WithLoadOrder(lo)
            .WithFileSystem(fileSystem);
        if (singleThread)
        {
            builder.SingleThread().Write();
        }
        else
        {
            builder.Write();
        }
    }

    [Theory, MutagenAutoData]
    public void MostFullMastersThatFitRoundTrip(
        IFileSystem fileSystem,
        DirectoryPath existingDir)
    {
        var (mod, list, lo) = Build(252);
        var modPath = Path.Combine(existingDir, OriginatingKey.FileName);
        Write(mod, lo, modPath, fileSystem, singleThread: false);

        using var reimport = StarfieldMod.CreateFromBinaryOverlay(modPath, StarfieldRelease.Starfield, new BinaryReadParameters()
        {
            FileSystem = fileSystem,
            MasterFlagsLookup = lo
        });
        var reimportList = reimport.FormLists.ShouldHaveSingleItem();
        reimportList.FormKey.ShouldBe(list.FormKey);
        reimportList.Items.Select(x => x.FormKey).ShouldBe(list.Items.Select(x => x.FormKey));
    }

    [Theory, MutagenAutoData]
    public void FullMastersReachingMediumMarkerThrow(
        IFileSystem fileSystem,
        DirectoryPath existingDir)
    {
        foreach (var count in new[] { 253, 254 })
        {
            foreach (var singleThread in new[] { false, true })
            {
                var (mod, _, lo) = Build(count);
                var modPath = Path.Combine(existingDir, OriginatingKey.FileName);
                Should.Throw<TooManyMastersException>(() => Write(mod, lo, modPath, fileSystem, singleThread));
            }
        }
    }

    [Theory, MutagenAutoData]
    public void OverridesOnlyWithFullMastersAtLimitRoundTrip(
        IFileSystem fileSystem,
        DirectoryPath existingDir)
    {
        foreach (var style in new[] { MasterStyle.Full, MasterStyle.Medium, MasterStyle.Small })
        {
            var (mod, lo) = BuildOverridesOnly(253, style);
            var modPath = Path.Combine(existingDir, OriginatingKey.FileName);
            Write(mod, lo, modPath, fileSystem, singleThread: false);

            using var reimport = StarfieldMod.CreateFromBinaryOverlay(modPath, StarfieldRelease.Starfield, new BinaryReadParameters()
            {
                FileSystem = fileSystem,
                MasterFlagsLookup = lo
            });
            reimport.ModHeader.MasterReferences.Count.ShouldBe(253);
            reimport.Keywords.Select(x => x.FormKey).ShouldBe(mod.Keywords.Select(x => x.FormKey), ignoreOrder: true);
        }
    }

    [Theory, MutagenAutoData]
    public void OverridesOnlyLinkingOwnFormKeyAtLimitThrow(
        IFileSystem fileSystem,
        DirectoryPath existingDir)
    {
        foreach (var style in new[] { MasterStyle.Full, MasterStyle.Small })
        {
            var (mod, lo) = BuildOverridesOnly(253, style);
            var list = new FormList(new FormKey(FullKey(0), 0x900), StarfieldRelease.Starfield);
            list.Items.Add(new FormKey(OriginatingKey, 0x012345).ToLink<IStarfieldMajorRecordGetter>());
            mod.FormLists.Add(list);
            var modPath = Path.Combine(existingDir, OriginatingKey.FileName);
            Should.Throw<TooManyMastersException>(() => Write(mod, lo, modPath, fileSystem, singleThread: false));
        }
    }
}
