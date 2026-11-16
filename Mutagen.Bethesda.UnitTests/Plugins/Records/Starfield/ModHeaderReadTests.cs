using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.HandReadLayoutFile;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

/// <summary>
/// C58/W17: a plugin's header (TES4) read alone, typed, without opening the plugin (SFSearch made a one-record overlay
/// of the header's bytes for it)
/// </summary>
public class ModHeaderReadTests
{
    [Theory, MutagenAutoData]
    public async Task CreateFromPath_EqualsTheFullImportsHeader(IFileSystem fileSystem, DirectoryPath existingTempDir)
    {
        var mod = new StarfieldMod(new ModKey("HeaderOnly", ModType.Master), StarfieldRelease.Starfield);
        mod.ModHeader.Author = "Someone";
        mod.ModHeader.Description = "A plugin";
        mod.Keywords.AddNew("TestKeyword");
        var path = new ModPath(mod.ModKey, Path.Combine(existingTempDir, mod.ModKey.FileName));
        await mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().WithFileSystem(fileSystem).WriteAsync();

        var header = StarfieldModHeader.CreateFromPath(path, StarfieldRelease.Starfield, new BinaryReadParameters { FileSystem = fileSystem });
        var imported = StarfieldMod.Create(StarfieldRelease.Starfield).FromPath(path).WithNoLoadOrder().WithFileSystem(fileSystem).Construct();
        header.Equals(imported.ModHeader).ShouldBeTrue();
        header.Author.ShouldBe("Someone");
        header.Stats.NumRecords.ShouldBe(imported.ModHeader.Stats.NumRecords);
    }

    [Theory, MutagenAutoData]
    public void CreateFromPath_NotAPlugin(IFileSystem fileSystem, DirectoryPath existingTempDir)
    {
        var path = Path.Combine(existingTempDir, "NotAPlugin.esm");
        fileSystem.File.WriteAllBytes(path, new byte[64]);
        Should.Throw<Exception>(() => StarfieldModHeader.CreateFromPath(path, StarfieldRelease.Starfield, new BinaryReadParameters { FileSystem = fileSystem }));
    }

    /// <summary>
    /// Every official plugin's header, read alone, equals the header of its overlay (what SFSearch's header card
    /// showed through its one-record overlay)
    /// </summary>
    [Fact]
    [Trait("Category", "RealData")]
    public void RealData_OfficialPlugins_EqualTheOverlaysHeader()
    {
        var data = Environment.GetEnvironmentVariable("MUTAGEN_STARFIELD_DATA");
        if (string.IsNullOrEmpty(data)) return; // needs the game's Data folder
        var styles = new Cache<IModMasterStyledGetter, ModKey>(x => x.ModKey);
        foreach (var name in HandReadLayoutFileTests.OfficialPlugins)
        {
            var modKey = ModKey.FromFileName(name);
            styles.Set(new KeyedMasterStyle(modKey, ModHeaderFrame.FromPath(Path.Combine(data, name), GameRelease.Starfield).MasterStyle));
        }
        var param = new BinaryReadParameters { MasterFlagsLookup = styles };
        foreach (var name in HandReadLayoutFileTests.OfficialPlugins)
        {
            var path = new ModPath(ModKey.FromFileName(name), Path.Combine(data, name));
            var header = StarfieldModHeader.CreateFromPath(path, StarfieldRelease.Starfield, param);
            using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, param);
            header.Equals(overlay.ModHeader).ShouldBeTrue(name);
            header.MasterReferences.Select(m => m.Master).ShouldBe(overlay.ModHeader.MasterReferences.Select(m => m.Master), name);
        }
    }
}
