using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Analysis;
using Mutagen.Bethesda.Plugins.Binary;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

// C197: MajorRecordReader.FromPath always opened a localized plugin's strings files (SFSearch, to keep the string IDs
// only, pointed it at folders that don't exist). FromPath(path, release, param, stringsLookup): the caller's own lookup,
// or a StringsKeysOnlyLookup for the IDs only with no file opened. Without one, nothing changes.
public class MajorRecordReaderStringsTests
{
    private sealed class OwnLookup : IStringsFolderLookup
    {
        public int Lookups;
        public IReadOnlyCollection<Language> AvailableLanguages(StringsSource source) => new[] { Language.English };

        public bool TryLookup(StringsSource source, Language language, uint key, [MaybeNullWhen(false)] out string str)
        {
            Lookups++;
            str = $"own {source} {key}";
            return true;
        }

        public TranslatedString CreateString(StringsSource source, uint key, Language targetLanguage)
            => TranslatedString.FromStringsKey(key, source, targetLanguage, this, ModKey.FromFileName("Localized.esm"));
    }

    private static async Task<(ModPath Path, long WeaponAt, StarfieldMod Mod)> WriteLocalized(IFileSystem fileSystem, DirectoryPath dir)
    {
        var mod = new StarfieldMod(new ModKey("Localized", ModType.Master), StarfieldRelease.Starfield) { UsingLocalization = true };
        var weapon = mod.Weapons.AddNew("TestWeapon");
        weapon.Name = "Test Weapon";
        weapon.Description = "A long description";
        var path = new ModPath(mod.ModKey, Path.Combine(dir, mod.ModKey.FileName));
        await mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().WithFileSystem(fileSystem).WriteAsync();
        var locations = RecordLocator.GetLocations(path, GameRelease.Starfield, loadOrder: null, fileSystem: fileSystem);
        return (path, locations[weapon.FormKey].Min, mod);
    }

    [Theory, MutagenAutoData]
    public async Task Default_ReadsTheStringsFiles(IFileSystem fileSystem, DirectoryPath existingTempDir)
    {
        var (path, at, _) = await WriteLocalized(fileSystem, existingTempDir);
        using var reader = MajorRecordReader.FromPath(path, GameRelease.Starfield, new BinaryReadParameters { FileSystem = fileSystem });
        var weapon = reader.ReadAt(at).ShouldBeOfType<Weapon>();
        weapon.Name!.String.ShouldBe("Test Weapon");
        weapon.Description!.String.ShouldBe("A long description");
        weapon.Name.StringsKey.ShouldNotBeNull();
        weapon.Name.StringsKeyPlugin.ShouldBe(path.ModKey);

        // the same through the overload with no lookup
        using var same = MajorRecordReader.FromPath(path, GameRelease.Starfield, new BinaryReadParameters { FileSystem = fileSystem }, stringsLookup: null);
        same.ReadAt(at).ShouldBeOfType<Weapon>().Name!.String.ShouldBe("Test Weapon");
    }

    [Theory, MutagenAutoData]
    public async Task KeysOnly_KeepsTheIdsAndOpensNoStringsFile(IFileSystem fileSystem, DirectoryPath existingTempDir)
    {
        var (path, at, _) = await WriteLocalized(fileSystem, existingTempDir);
        using var full = MajorRecordReader.FromPath(path, GameRelease.Starfield, new BinaryReadParameters { FileSystem = fileSystem });
        var expected = full.ReadAt(at).ShouldBeOfType<Weapon>();

        // no strings file is opened: the folder is gone
        fileSystem.Directory.Delete(Path.Combine(existingTempDir, "Strings"), recursive: true);
        using var reader = MajorRecordReader.FromPath(path, GameRelease.Starfield, new BinaryReadParameters { FileSystem = fileSystem },
            new StringsKeysOnlyLookup(path.ModKey));
        var weapon = reader.ReadAt(at).ShouldBeOfType<Weapon>();
        foreach (var (got, want) in new[] { (weapon.Name!, expected.Name!), (weapon.Description!, expected.Description!) })
        {
            got.String.ShouldBeNull();
            got.StringsKey.ShouldBe(want.StringsKey);
            got.StringsKeySource.ShouldBe(want.StringsKeySource);
            got.StringsKeyPlugin.ShouldBe(path.ModKey);
        }
        weapon.Description!.StringsKeySource.ShouldBe(StringsSource.DL);
    }

    [Theory, MutagenAutoData]
    public async Task OwnLookup_GivesTheTexts(IFileSystem fileSystem, DirectoryPath existingTempDir)
    {
        var (path, at, _) = await WriteLocalized(fileSystem, existingTempDir);
        var own = new OwnLookup();
        using var reader = MajorRecordReader.FromPath(path, GameRelease.Starfield, new BinaryReadParameters { FileSystem = fileSystem }, own);
        var weapon = reader.ReadAt(at).ShouldBeOfType<Weapon>();
        weapon.Name!.String.ShouldBe($"own Normal {weapon.Name.StringsKey}");
        weapon.Description!.String.ShouldBe($"own DL {weapon.Description.StringsKey}");
        own.Lookups.ShouldBeGreaterThan(0);
    }

    [Theory, MutagenAutoData]
    public async Task NotLocalized_IgnoresTheLookup(IFileSystem fileSystem, DirectoryPath existingTempDir)
    {
        var mod = new StarfieldMod(new ModKey("Plain", ModType.Master), StarfieldRelease.Starfield);
        var weapon = mod.Weapons.AddNew("TestWeapon");
        weapon.Name = "Plain Weapon";
        var path = new ModPath(mod.ModKey, Path.Combine(existingTempDir, mod.ModKey.FileName));
        await mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().WithFileSystem(fileSystem).WriteAsync();
        var at = RecordLocator.GetLocations(path, GameRelease.Starfield, loadOrder: null, fileSystem: fileSystem)[weapon.FormKey].Min;
        using var reader = MajorRecordReader.FromPath(path, GameRelease.Starfield, new BinaryReadParameters { FileSystem = fileSystem },
            new StringsKeysOnlyLookup(path.ModKey));
        reader.ReadAt(at).ShouldBeOfType<Weapon>().Name!.String.ShouldBe("Plain Weapon");
    }
}
