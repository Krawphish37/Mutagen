using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog;
using Shouldly;

namespace Mutagen.Bethesda.UnitTests.Plugins.Builders;

// C64: a plugin whose master has masters of its own that the plugin doesn't list (in Starfield, for example a mod on
// Watchtower, kinggathcreations_spaceship.esm, which masters the medium SFBGS003/SFBGS006 and small SFBGS004 that the
// mod doesn't list). Reading the plugin with a data folder loads each master to build a link cache, and those masters
// were read with a master flags lookup made of the plugin's masters only, so a master's own medium/small masters were
// missing and the read failed with MissingModException even though every file was there.
public class ReadBuilderMastersOfMastersTests
{
    private static readonly ModKey Base = ModKey.FromFileName("Base.esm");
    private static readonly ModKey Medium = ModKey.FromFileName("Medium.esm");
    private static readonly ModKey Small = ModKey.FromFileName("Small.esm");
    private static readonly ModKey Middle = ModKey.FromFileName("Middle.esm");
    private static readonly ModKey Plugin = ModKey.FromFileName("Plugin.esm");

    private static (string PluginPath, FormKey MiddleWeapon, FormKey MediumWeapon) Build(IFileSystem fileSystem, DirectoryPath dataDir)
    {
        var baseMod = new StarfieldMod(Base, StarfieldRelease.Starfield);
        var baseWeapon = baseMod.Weapons.AddNew();
        baseMod.WriteToBinary(Path.Combine(dataDir, Base.FileName), new BinaryWriteParameters { FileSystem = fileSystem });

        var medium = new StarfieldMod(Medium, StarfieldRelease.Starfield);
        medium.ModHeader.Flags |= StarfieldModHeader.HeaderFlag.Medium;
        medium.Weapons.GetOrAddAsOverride(baseWeapon);
        var mediumWeapon = medium.Weapons.AddNew();
        medium.BeginWrite.ToPath(Path.Combine(dataDir, Medium.FileName)).WithLoadOrder(baseMod).WithFileSystem(fileSystem).Write();

        var small = new StarfieldMod(Small, StarfieldRelease.Starfield);
        small.ModHeader.Flags |= StarfieldModHeader.HeaderFlag.Light;
        var smallWeapon = small.Weapons.AddNew();
        small.BeginWrite.ToPath(Path.Combine(dataDir, Small.FileName)).WithLoadOrder(baseMod).WithFileSystem(fileSystem).Write();

        // Like Watchtower: a full master with medium and small masters of its own
        var middle = new StarfieldMod(Middle, StarfieldRelease.Starfield);
        middle.Weapons.GetOrAddAsOverride(baseWeapon);
        middle.Weapons.GetOrAddAsOverride(mediumWeapon);
        middle.Weapons.GetOrAddAsOverride(smallWeapon);
        var middleWeapon = middle.Weapons.AddNew();
        middle.BeginWrite.ToPath(Path.Combine(dataDir, Middle.FileName)).WithLoadOrder(baseMod, medium, small).WithFileSystem(fileSystem).Write();

        // The plugin lists Base and Middle only
        var plugin = new StarfieldMod(Plugin, StarfieldRelease.Starfield);
        plugin.Weapons.GetOrAddAsOverride(middleWeapon);
        plugin.Weapons.GetOrAddAsOverride(baseWeapon);
        var pluginPath = Path.Combine(dataDir, Plugin.FileName);
        plugin.BeginWrite.ToPath(pluginPath).WithLoadOrder(baseMod, middle).WithFileSystem(fileSystem).Write();
        return (pluginPath, middleWeapon.FormKey, mediumWeapon.FormKey);
    }

    [Theory, MutagenAutoData]
    public void HeaderMasters(IFileSystem fileSystem, DirectoryPath existingDataDir)
    {
        var (path, middleWeapon, _) = Build(fileSystem, existingDataDir);
        var reimport = StarfieldMod.Create(StarfieldRelease.Starfield)
            .FromPath(path)
            .WithLoadOrderFromHeaderMasters()
            .WithDataFolder(existingDataDir)
            .WithFileSystem(fileSystem)
            .Mutable()
            .Construct();
        reimport.MasterReferences.Select(x => x.Master).ShouldBe(new[] { Base, Middle });
        reimport.Weapons.Select(x => x.FormKey).ShouldContain(middleWeapon);
    }

    [Theory, MutagenAutoData]
    public void HeaderMasters_Overlay(IFileSystem fileSystem, DirectoryPath existingDataDir)
    {
        var (path, middleWeapon, _) = Build(fileSystem, existingDataDir);
        using var reimport = StarfieldMod.Create(StarfieldRelease.Starfield)
            .FromPath(path)
            .WithLoadOrderFromHeaderMasters()
            .WithDataFolder(existingDataDir)
            .WithFileSystem(fileSystem)
            .Construct();
        reimport.Weapons.Select(x => x.FormKey).ShouldContain(middleWeapon);
    }

    [Theory, MutagenAutoData]
    public void ExplicitLoadOrder(IFileSystem fileSystem, DirectoryPath existingDataDir)
    {
        var (path, middleWeapon, _) = Build(fileSystem, existingDataDir);
        var reimport = StarfieldMod.Create(StarfieldRelease.Starfield)
            .FromPath(path)
            .WithLoadOrder(Base, Middle)
            .WithDataFolder(existingDataDir)
            .WithFileSystem(fileSystem)
            .Mutable()
            .Construct();
        reimport.Weapons.Select(x => x.FormKey).ShouldContain(middleWeapon);
    }
}
