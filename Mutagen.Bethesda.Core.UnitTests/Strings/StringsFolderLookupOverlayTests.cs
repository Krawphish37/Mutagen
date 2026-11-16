using System.IO.Abstractions;
using Mutagen.Bethesda.Archives.DI;
using Mutagen.Bethesda.Environments.DI;
using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Strings.DI;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog;
using NSubstitute;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Strings;

public class StringsFolderLookupOverlayTests
{
    [Theory, MutagenModAutoData]
    internal void Typical(IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingPath,
        MutagenEncodingProvider encodingProvider,
        string normalStr,
        string ilStr,
        string dlStr,
        IGameReleaseContext gameRelease,
        IDataDirectoryProvider dataDirectoryProvider,
        IGetApplicableArchivePaths getApplicableArchivePaths)
    {
        var factory = new StringsFolderLookupFactory(gameRelease, dataDirectoryProvider, getApplicableArchivePaths, fileSystem, new StringsReadParameters()
        {
            StringsFolderOverride = existingPath
        });
        uint normalId, ilId, dlId;
        using (var writer = new StringsWriter(GameRelease.SkyrimSE, modKey, existingPath, encodingProvider, fileSystem))
        {
            normalId = writer.Register(normalStr, Language.English, StringsSource.Normal);
            ilId = writer.Register(ilStr, Language.English, StringsSource.IL);
            dlId = writer.Register(dlStr, Language.English, StringsSource.DL);
        }
        var overlay = factory.InternalFactory(modKey);
        
        overlay.TryLookup(StringsSource.Normal, Language.English, normalId, out var normalStrOut)
            .ShouldBeTrue();
        normalStrOut.ShouldBe(normalStr);
        overlay.TryLookup(StringsSource.IL, Language.English, ilId, out var ilStrOut)
            .ShouldBeTrue();
        ilStrOut.ShouldBe(ilStr);
        overlay.TryLookup(StringsSource.DL, Language.English, dlId, out var dlStrOut)
            .ShouldBeTrue();
        dlStrOut.ShouldBe(dlStr);
    }
    
    [Theory, MutagenModAutoData]
    internal void SuffixCollision(IFileSystem fileSystem,
        DirectoryPath existingPath,
        MutagenEncodingProvider encodingProvider,
        string str1,
        string str2,
        IGameReleaseContext gameRelease,
        IDataDirectoryProvider dataDirectoryProvider,
        IGetApplicableArchivePaths getApplicableArchivePaths)
    {
        var factory = new StringsFolderLookupFactory(gameRelease, dataDirectoryProvider, getApplicableArchivePaths, fileSystem, new StringsReadParameters()
        {
            StringsFolderOverride = existingPath
        });
        
        var modKey1 = ModKey.FromFileName("FileName.esm");
        uint id1;
        using (var writer = new StringsWriter(GameRelease.SkyrimSE, modKey1, existingPath, encodingProvider, fileSystem))
        {
            id1 = writer.Register(str1, Language.English, StringsSource.Normal);
        }
        
        var modKey2 = ModKey.FromFileName("FileNameWithSuffix.esm");
        uint id2;
        using (var writer = new StringsWriter(GameRelease.SkyrimSE, modKey2, existingPath, encodingProvider, fileSystem))
        {
            id2 = writer.Register(str2, Language.English, StringsSource.Normal);
        }
        
        var overlay = factory.Factory(modKey1);
        
        overlay.TryLookup(StringsSource.Normal, Language.English, id1, out var strOut)
            .ShouldBeTrue();
        strOut.ShouldBe(str1);
    }

    [Theory, MutagenModAutoData]
    internal void TryLookupWithSourcePath(IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingPath,
        MutagenEncodingProvider encodingProvider,
        string testString,
        IGameReleaseContext gameRelease,
        IDataDirectoryProvider dataDirectoryProvider,
        IGetApplicableArchivePaths getApplicableArchivePaths)
    {
        var factory = new StringsFolderLookupFactory(gameRelease, dataDirectoryProvider, getApplicableArchivePaths, fileSystem, new StringsReadParameters()
        {
            StringsFolderOverride = existingPath
        });
        uint stringId;
        using (var writer = new StringsWriter(GameRelease.SkyrimSE, modKey, existingPath, encodingProvider, fileSystem))
        {
            stringId = writer.Register(testString, Language.English, StringsSource.Normal);
        }
        var overlay = factory.InternalFactory(modKey);

        overlay.TryLookup(StringsSource.Normal, Language.English, stringId, out var str, out var sourcePath)
            .ShouldBeTrue();
        str.ShouldBe(testString);
        sourcePath.ShouldNotBeNullOrEmpty();
    }

    [Theory, MutagenModAutoData]
    internal void TryLookupNonExistentLanguage(IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingPath,
        MutagenEncodingProvider encodingProvider,
        string testString,
        IGameReleaseContext gameRelease,
        IDataDirectoryProvider dataDirectoryProvider,
        IGetApplicableArchivePaths getApplicableArchivePaths)
    {
        var factory = new StringsFolderLookupFactory(gameRelease, dataDirectoryProvider, getApplicableArchivePaths, fileSystem, new StringsReadParameters()
        {
            StringsFolderOverride = existingPath
        });
        uint stringId;
        using (var writer = new StringsWriter(GameRelease.SkyrimSE, modKey, existingPath, encodingProvider, fileSystem))
        {
            stringId = writer.Register(testString, Language.English, StringsSource.Normal);
        }
        var overlay = factory.InternalFactory(modKey);

        overlay.TryLookup(StringsSource.Normal, Language.French, stringId, out var str)
            .ShouldBeFalse();
        str.ShouldBeNull();

        overlay.TryLookup(StringsSource.Normal, Language.French, stringId, out str, out var sourcePath)
            .ShouldBeFalse();
        str.ShouldBeNull();
        sourcePath.ShouldBeNull();
    }

    [Theory, MutagenModAutoData]
    internal void TryLookupNonExistentKey(IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingPath,
        MutagenEncodingProvider encodingProvider,
        string testString,
        IGameReleaseContext gameRelease,
        IDataDirectoryProvider dataDirectoryProvider,
        IGetApplicableArchivePaths getApplicableArchivePaths)
    {
        var factory = new StringsFolderLookupFactory(gameRelease, dataDirectoryProvider, getApplicableArchivePaths, fileSystem, new StringsReadParameters()
        {
            StringsFolderOverride = existingPath
        });
        using (var writer = new StringsWriter(GameRelease.SkyrimSE, modKey, existingPath, encodingProvider, fileSystem))
        {
            writer.Register(testString, Language.English, StringsSource.Normal);
        }
        var overlay = factory.InternalFactory(modKey);

        overlay.TryLookup(StringsSource.Normal, Language.English, 999999u, out var str)
            .ShouldBeFalse();
        str.ShouldBeNull();

        overlay.TryLookup(StringsSource.Normal, Language.English, 999999u, out str, out var sourcePath)
            .ShouldBeFalse();
        str.ShouldBeNull();
        sourcePath.ShouldBeNull();
    }

    [Theory, MutagenModAutoData]
    internal void EmptyOverlay(IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingPath,
        IGameReleaseContext gameRelease,
        IDataDirectoryProvider dataDirectoryProvider,
        IGetApplicableArchivePaths getApplicableArchivePaths)
    {
        var factory = new StringsFolderLookupFactory(gameRelease, dataDirectoryProvider, getApplicableArchivePaths, fileSystem, new StringsReadParameters()
        {
            StringsFolderOverride = existingPath
        });
        var overlay = factory.InternalFactory(modKey);

        overlay.Empty.ShouldBeTrue();
        overlay.AvailableLanguages(StringsSource.Normal).ShouldBeEmpty();
        overlay.AvailableLanguages(StringsSource.IL).ShouldBeEmpty();
        overlay.AvailableLanguages(StringsSource.DL).ShouldBeEmpty();
    }

    [Theory, MutagenModAutoData]
    internal void AvailableLanguages(IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingPath,
        MutagenEncodingProvider encodingProvider,
        string englishStr,
        string frenchStr,
        string germanStr,
        IGameReleaseContext gameRelease,
        IDataDirectoryProvider dataDirectoryProvider,
        IGetApplicableArchivePaths getApplicableArchivePaths)
    {
        var factory = new StringsFolderLookupFactory(gameRelease, dataDirectoryProvider, getApplicableArchivePaths, fileSystem, new StringsReadParameters()
        {
            StringsFolderOverride = existingPath
        });

        using (var writer = new StringsWriter(GameRelease.SkyrimSE, modKey, existingPath, encodingProvider, fileSystem))
        {
            writer.Register(englishStr, Language.English, StringsSource.Normal);
            writer.Register(frenchStr, Language.French, StringsSource.Normal);
            writer.Register(germanStr, Language.German, StringsSource.IL);
        }

        var languageFormat = GameConstants.Get(GameRelease.SkyrimSE).StringsLanguageFormat!.Value;
        fileSystem.File.Delete(Path.Combine(existingPath.Path, StringsUtility.GetFileName(languageFormat, modKey, Language.English, StringsSource.IL)));
        fileSystem.File.Delete(Path.Combine(existingPath.Path, StringsUtility.GetFileName(languageFormat, modKey, Language.French, StringsSource.IL)));
        fileSystem.File.Delete(Path.Combine(existingPath.Path, StringsUtility.GetFileName(languageFormat, modKey, Language.German, StringsSource.Normal)));
        fileSystem.File.Delete(Path.Combine(existingPath.Path, StringsUtility.GetFileName(languageFormat, modKey, Language.English, StringsSource.DL)));
        fileSystem.File.Delete(Path.Combine(existingPath.Path, StringsUtility.GetFileName(languageFormat, modKey, Language.French, StringsSource.DL)));
        fileSystem.File.Delete(Path.Combine(existingPath.Path, StringsUtility.GetFileName(languageFormat, modKey, Language.German, StringsSource.DL)));

        var overlay = factory.InternalFactory(modKey);

        overlay.Empty.ShouldBeFalse();

        var normalLanguages = overlay.AvailableLanguages(StringsSource.Normal);
        normalLanguages.ShouldContain(Language.English);
        normalLanguages.ShouldContain(Language.French);
        
        var ilLanguages = overlay.AvailableLanguages(StringsSource.IL);
        ilLanguages.ShouldContain(Language.German);

        overlay.AvailableLanguages(StringsSource.DL).ShouldBeEmpty();
    }

    [Theory, MutagenModAutoData]
    internal void CreateString(IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingPath,
        MutagenEncodingProvider encodingProvider,
        string englishStr,
        string frenchStr,
        IGameReleaseContext gameRelease,
        IDataDirectoryProvider dataDirectoryProvider,
        IGetApplicableArchivePaths getApplicableArchivePaths)
    {
        var factory = new StringsFolderLookupFactory(gameRelease, dataDirectoryProvider, getApplicableArchivePaths, fileSystem, new StringsReadParameters()
        {
            StringsFolderOverride = existingPath
        });
        uint stringId;
        using (var writer = new StringsWriter(GameRelease.SkyrimSE, modKey, existingPath, encodingProvider, fileSystem))
        {
            stringId = writer.Register(StringsSource.Normal,
                new KeyValuePair<Language, string>(Language.English, englishStr),
                new KeyValuePair<Language, string>(Language.French, frenchStr));
        }
        var overlay = factory.InternalFactory(modKey);
    
        var translatedString = overlay.CreateString(StringsSource.Normal, stringId, Language.English);
    
        translatedString.ShouldNotBeNull();
        translatedString.TargetLanguage.ShouldBe(Language.English);
        translatedString.StringsKey.ShouldBe(stringId);
        translatedString.String.ShouldBe(englishStr);
    }
    
    [Theory, MutagenModAutoData]
    internal void MultipleLanguagesAndSources(IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingPath,
        MutagenEncodingProvider encodingProvider,
        string englishNormal,
        string frenchNormal,
        string englishIL,
        string englishDL,
        IGameReleaseContext gameRelease,
        IDataDirectoryProvider dataDirectoryProvider,
        IGetApplicableArchivePaths getApplicableArchivePaths)
    {
        var factory = new StringsFolderLookupFactory(gameRelease, dataDirectoryProvider, getApplicableArchivePaths, fileSystem, new StringsReadParameters()
        {
            StringsFolderOverride = existingPath
        });
        uint normalId, ilId, dlId;
        using (var writer = new StringsWriter(GameRelease.SkyrimSE, modKey, existingPath, encodingProvider, fileSystem))
        {
            normalId = writer.Register(englishNormal, Language.English, StringsSource.Normal);
            writer.Register(frenchNormal, Language.French, StringsSource.Normal);
            ilId = writer.Register(englishIL, Language.English, StringsSource.IL);
            dlId = writer.Register(englishDL, Language.English, StringsSource.DL);
        }

        fileSystem.File.Delete(Path.Combine(existingPath.Path, StringsUtility.GetFileName(GameConstants.Get(GameRelease.SkyrimSE).StringsLanguageFormat!.Value, modKey, Language.French, StringsSource.IL)));
        fileSystem.File.Delete(Path.Combine(existingPath.Path, StringsUtility.GetFileName(GameConstants.Get(GameRelease.SkyrimSE).StringsLanguageFormat!.Value, modKey, Language.French, StringsSource.DL)));

        var overlay = factory.InternalFactory(modKey);
    
        overlay.TryLookup(StringsSource.Normal, Language.English, normalId, out var str)
            .ShouldBeTrue();
        str.ShouldBe(englishNormal);
    
    
        overlay.TryLookup(StringsSource.IL, Language.English, ilId, out str)
            .ShouldBeTrue();
        str.ShouldBe(englishIL);
    
        overlay.TryLookup(StringsSource.DL, Language.English, dlId, out str)
            .ShouldBeTrue();
        str.ShouldBe(englishDL);
    
        var normalLanguages = overlay.AvailableLanguages(StringsSource.Normal);
        normalLanguages.ShouldContain(Language.English);
    
        var ilLanguages = overlay.AvailableLanguages(StringsSource.IL);
        ilLanguages.Count.ShouldBe(1);
        ilLanguages.ShouldContain(Language.English);
    
        var dlLanguages = overlay.AvailableLanguages(StringsSource.DL);
        dlLanguages.Count.ShouldBe(1);
        dlLanguages.ShouldContain(Language.English);
    }
    
    [Theory, MutagenModAutoData]
    internal void LooseStringsPriorityOverBSA_CaseSensitivityFix(IFileSystem fileSystem,
        DirectoryPath existingPath,
        MutagenEncodingProvider encodingProvider,
        string looseStringContent,
        IGameReleaseContext gameRelease,
        IDataDirectoryProvider dataDirectoryProvider)
    {
        var modKey = ModKey.FromFileName("SKYRIM.esm");
    
        uint looseStringId;
        using (var writer = new StringsWriter(GameRelease.SkyrimSE, modKey, existingPath, encodingProvider, fileSystem))
        {
            looseStringId = writer.Register(looseStringContent, Language.English, StringsSource.Normal);
        }
    
        var mockGetApplicableArchivePaths = Substitute.For<IGetApplicableArchivePaths>();
        mockGetApplicableArchivePaths.Get(modKey).Returns([]);
    
        var factory = new StringsFolderLookupFactory(gameRelease, dataDirectoryProvider, mockGetApplicableArchivePaths, fileSystem, new StringsReadParameters()
        {
            StringsFolderOverride = existingPath
        });
    
        var overlay = factory.InternalFactory(modKey);
    
        overlay.TryLookup(StringsSource.Normal, Language.English, looseStringId, out var retrievedString, out var sourcePath)
            .ShouldBeTrue();
    
        retrievedString.ShouldBe(looseStringContent);
        sourcePath.ShouldNotBeNull();
        sourcePath.ShouldContain(existingPath.Path);
        sourcePath.ShouldContain("SKYRIM_english.strings");
    }
    
    [Theory, MutagenModAutoData]
    internal void LooseStringsOnly_ShouldWork(IFileSystem fileSystem,
        DirectoryPath existingPath,
        MutagenEncodingProvider encodingProvider,
        string looseStringContent,
        IGameReleaseContext gameRelease,
        IDataDirectoryProvider dataDirectoryProvider)
    {
        var modKey = ModKey.FromFileName("Skyrim.esm");
    
        var mockGetApplicableArchivePaths = Substitute.For<IGetApplicableArchivePaths>();
        mockGetApplicableArchivePaths.Get(modKey).Returns([]);
    
        var factory = new StringsFolderLookupFactory(gameRelease, dataDirectoryProvider, mockGetApplicableArchivePaths, fileSystem, new StringsReadParameters()
        {
            StringsFolderOverride = existingPath
        });
    
        uint looseStringId;
    
        using (var writer = new StringsWriter(GameRelease.SkyrimSE, modKey, existingPath, encodingProvider, fileSystem))
        {
            looseStringId = writer.Register(looseStringContent, Language.English, StringsSource.Normal);
        }
    
        var overlay = factory.InternalFactory(modKey);
    
        overlay.TryLookup(StringsSource.Normal, Language.English, looseStringId, out var retrievedString, out var sourcePath)
            .ShouldBeTrue();
    
        retrievedString.ShouldBe(looseStringContent);
        sourcePath.ShouldContain(existingPath.Path);
        sourcePath.ShouldContain("skyrim_english.strings");
    }

    [Fact]
    public void LowerCaseFileNames()
    {
        using var dir = TestPathing.GetTempFolder(nameof(StringsFolderLookupOverlayTests));
        var fileSystem = new FileSystem();
        var modKey = ModKey.FromFileName("MyMod.esp");
        uint id;
        using (var writer = new StringsWriter(GameRelease.SkyrimSE, modKey, dir.Dir, new MutagenEncodingProvider(), fileSystem))
        {
            id = writer.Register("Hello", Language.English, StringsSource.Normal);
        }
        foreach (var written in Directory.EnumerateFiles(dir.Dir.Path))
        {
            File.Move(written, Path.Combine(dir.Dir.Path, Path.GetFileName(written).ToLowerInvariant()));
        }
        var gameRelease = Substitute.For<IGameReleaseContext>();
        gameRelease.Release.Returns(GameRelease.SkyrimSE);
        var factory = new StringsFolderLookupFactory(
            gameRelease,
            Substitute.For<IDataDirectoryProvider>(),
            Substitute.For<IGetApplicableArchivePaths>(),
            fileSystem,
            new StringsReadParameters { StringsFolderOverride = dir.Dir });

        factory.InternalFactory(modKey).TryLookup(StringsSource.Normal, Language.English, id, out var str).ShouldBeTrue();
        str.ShouldBe("Hello");
    }

    private static StringsFolderLookupFactory EnumerationFactory(IFileSystem fileSystem, DirectoryPath stringsFolder)
    {
        var gameRelease = Substitute.For<IGameReleaseContext>();
        gameRelease.Release.Returns(GameRelease.SkyrimSE);
        var archives = Substitute.For<IGetApplicableArchivePaths>();
        archives.Get(Arg.Any<ModKey>()).Returns([]);
        return new StringsFolderLookupFactory(
            gameRelease,
            Substitute.For<IDataDirectoryProvider>(),
            archives,
            fileSystem,
            new StringsReadParameters { StringsFolderOverride = stringsFolder });
    }

    private static string EnumerationFilePath(DirectoryPath folder, ModKey modKey, Language language, StringsSource source)
    {
        return Path.Combine(folder.Path, StringsUtility.GetFileName(GameConstants.Get(GameRelease.SkyrimSE).StringsLanguageFormat!.Value, modKey, language, source));
    }

    [Theory, MutagenModAutoData]
    internal void EnumerateStrings_ListsEveryStringOfEachTable(
        IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingPath,
        MutagenEncodingProvider encodingProvider)
    {
        var expected = new Dictionary<(StringsSource Source, Language Language), Dictionary<uint, string>>();
        void Add(StringsWriter writer, string str, Language language, StringsSource source)
        {
            var id = writer.Register(str, language, source);
            expected.GetOrAdd((source, language))[id] = str;
        }
        using (var writer = new StringsWriter(GameRelease.SkyrimSE, modKey, existingPath, encodingProvider, fileSystem))
        {
            Add(writer, "Iron Sword", Language.English, StringsSource.Normal);
            Add(writer, "Steel Sword", Language.English, StringsSource.Normal);
            Add(writer, "Daedric Sword", Language.English, StringsSource.Normal);
            Add(writer, "A long description.", Language.English, StringsSource.DL);
            Add(writer, "Hello there.", Language.English, StringsSource.IL);
            Add(writer, "Hello again.", Language.English, StringsSource.IL);
            Add(writer, "Épée de fer à l'œil", Language.French, StringsSource.Normal);
        }
        var overlay = EnumerationFactory(fileSystem, existingPath).InternalFactory(modKey);

        foreach (var source in new[] { StringsSource.Normal, StringsSource.DL, StringsSource.IL })
        {
            foreach (var language in new[] { Language.English, Language.French })
            {
                var want = expected.TryGetValue((source, language), out var w) ? w : new Dictionary<uint, string>();
                var listed = overlay.EnumerateStrings(source, language).ToArray();
                listed.Select(x => x.Key).ShouldBeUnique();
                listed.ToDictionary(x => x.Key, x => x.Value).ShouldBe(want, ignoreOrder: true);
                overlay.GetStringsCount(source, language).ShouldBe(want.Count);
                foreach (var item in listed)
                {
                    overlay.TryLookup(source, language, item.Key, out var looked).ShouldBeTrue();
                    looked.ShouldBe(item.Value);
                }
            }
        }
    }

    [Theory, MutagenModAutoData]
    internal void EnumerateStrings_MissingTable(
        IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingPath,
        MutagenEncodingProvider encodingProvider)
    {
        using (var writer = new StringsWriter(GameRelease.SkyrimSE, modKey, existingPath, encodingProvider, fileSystem))
        {
            writer.Register("Iron Sword", Language.English, StringsSource.Normal);
        }
        fileSystem.File.Delete(EnumerationFilePath(existingPath, modKey, Language.English, StringsSource.DL));
        var overlay = EnumerationFactory(fileSystem, existingPath).InternalFactory(modKey);

        overlay.EnumerateStrings(StringsSource.DL, Language.English).ShouldBeEmpty();
        overlay.EnumerateStrings(StringsSource.Normal, Language.German).ShouldBeEmpty();
        overlay.GetStringsCount(StringsSource.DL, Language.English).ShouldBe(0);
        overlay.TryGetTableSource(StringsSource.DL, Language.English, out _).ShouldBeFalse();
        overlay.EnumerateTables().Select(x => (x.Source, x.Language))
            .ShouldBe(new[] { (StringsSource.Normal, Language.English), (StringsSource.IL, Language.English) });
    }

    [Theory, MutagenModAutoData]
    internal void EnumerateTables_LooseFiles(
        IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingPath,
        MutagenEncodingProvider encodingProvider)
    {
        uint id;
        using (var writer = new StringsWriter(GameRelease.SkyrimSE, modKey, existingPath, encodingProvider, fileSystem))
        {
            id = writer.Register("Iron Sword", Language.English, StringsSource.Normal);
            writer.Register("Épée de fer", Language.French, StringsSource.Normal);
        }
        var overlay = EnumerationFactory(fileSystem, existingPath).InternalFactory(modKey);

        var tables = overlay.EnumerateTables().ToArray();
        tables.Length.ShouldBe(6);
        tables.Select(x => x.Source).Distinct().ShouldBe(new[] { StringsSource.Normal, StringsSource.DL, StringsSource.IL });
        foreach (var table in tables)
        {
            table.FromArchive.ShouldBeFalse();
            table.ArchivePath.ShouldBeNull();
            table.ArchiveEntryPath.ShouldBeNull();
            Path.GetFileName(table.SourcePath).ShouldBe(Path.GetFileName(EnumerationFilePath(existingPath, modKey, table.Language, table.Source)), StringCompareShould.IgnoreCase);
            overlay.TryGetTableSource(table.Source, table.Language, out var same).ShouldBeTrue();
            same.ShouldBe(table);
        }

        overlay.TryGetTableSource(StringsSource.Normal, Language.English, out var english).ShouldBeTrue();
        overlay.TryLookup(StringsSource.Normal, Language.English, id, out _, out var lookupPath).ShouldBeTrue();
        english.SourcePath.ShouldBe(lookupPath);
    }

    [Theory, MutagenModAutoData]
    internal void EnumerateTables_DoesNotOpenStringsFiles(
        IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingPath,
        MutagenEncodingProvider encodingProvider)
    {
        using (var writer = new StringsWriter(GameRelease.SkyrimSE, modKey, existingPath, encodingProvider, fileSystem))
        {
            writer.Register("Iron Sword", Language.English, StringsSource.Normal);
        }
        var overlay = EnumerationFactory(fileSystem, existingPath).InternalFactory(modKey);

        overlay.EnumerateTables().Count().ShouldBe(3);
        overlay.TryGetTableSource(StringsSource.Normal, Language.English, out var table).ShouldBeTrue();
        var enumeration = overlay.EnumerateStrings(StringsSource.Normal, Language.English);
        // Nothing has read the file yet, so removing it now shows on first use
        fileSystem.File.Delete(table.SourcePath);
        Should.Throw<FileNotFoundException>(() => enumeration.ToArray());
    }

    /// <summary>
    /// C58/W10: where each table comes from, resolved without making a lookup or opening a table: the same list the
    /// lookup's EnumerateTables gives, from the same rule
    /// </summary>
    [Theory, MutagenModAutoData]
    internal void ResolveSources_IsWhatTheLookupReads(
        IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingPath,
        MutagenEncodingProvider encodingProvider)
    {
        uint id;
        using (var writer = new StringsWriter(GameRelease.SkyrimSE, modKey, existingPath, encodingProvider, fileSystem))
        {
            id = writer.Register("Iron Sword", Language.English, StringsSource.Normal);
            writer.Register("Épée de fer", Language.French, StringsSource.Normal);
        }
        var factory = EnumerationFactory(fileSystem, existingPath);
        var resolved = factory.ResolveSources(modKey);
        resolved.Count.ShouldBe(6);
        resolved.ShouldBe(factory.InternalFactory(modKey).EnumerateTables().ToArray());
        // Resolving opens no table: a file removed afterwards shows only when the lookup first reads it
        var english = resolved.Single(x => x.Source == StringsSource.Normal && x.Language == Language.English);
        english.FromArchive.ShouldBeFalse();
        var lookup = factory.InternalFactory(modKey);
        lookup.TryLookup(StringsSource.Normal, Language.English, id, out var text).ShouldBeTrue();
        text.ShouldBe("Iron Sword");
    }

    [Theory, MutagenModAutoData]
    internal void ResolveSources_Static_NoStrings(
        IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingPath)
    {
        StringsFolderLookupOverlay.ResolveSources(GameRelease.SkyrimSE, modKey, existingPath, fileSystem: fileSystem)
            .ShouldBeEmpty();
    }
}
