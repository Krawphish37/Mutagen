using System.IO.Abstractions;
using Noggog;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Archives.DI;
using Mutagen.Bethesda.Archives.Exceptions;
using Mutagen.Bethesda.Environments.DI;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Meta;

namespace Mutagen.Bethesda.Strings.DI;

public interface IStringsFolderLookupFactory
{
    IStringsFolderLookup Factory(ModKey modKey);
    internal StringsFolderLookupOverlay InternalFactory(ModKey modKey);
    internal IReadOnlyList<StringsTableSource> ResolveSources(ModKey modKey);
}

public class StringsFolderLookupFactory : IStringsFolderLookupFactory
{
    private readonly IGameReleaseContext _gameRelease;
    private readonly IDataDirectoryProvider _dataDirectoryProvider;
    private readonly IGetApplicableArchivePaths _getApplicableArchivePaths;
    private readonly IFileSystem _fileSystem;
    private readonly StringsReadParameters? _readParameters;

    public StringsFolderLookupFactory(
        IGameReleaseContext gameRelease,
        IDataDirectoryProvider dataDirectoryProvider,
        IGetApplicableArchivePaths getApplicableArchivePaths,
        IFileSystem fileSystem,
        StringsReadParameters? readParameters)
    {
        _gameRelease = gameRelease;
        _dataDirectoryProvider = dataDirectoryProvider;
        _getApplicableArchivePaths = getApplicableArchivePaths;
        _fileSystem = fileSystem;
        _readParameters = readParameters;
    }

    public IStringsFolderLookup Factory(ModKey modKey)
    {
        return InternalFactory(modKey);
    }

    public StringsFolderLookupOverlay InternalFactory(ModKey modKey)
    {
        var encodings = _readParameters?.EncodingProvider ?? new MutagenEncodingProvider();
        return new StringsFolderLookupOverlay(new Lazy<StringsFolderLookupOverlay.DictionaryBundle>(
                isThreadSafe: true,
                valueFactory: () =>
                {
                    var bundle = new StringsFolderLookupOverlay.DictionaryBundle();
                    // One reader per archive, made when one of its tables is first read
                    var archives = new Dictionary<string, Lazy<IArchiveReader>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var source in ResolveSources(modKey))
                    {
                        Lazy<IStringsLookup> lookup;
                        if (source.ArchivePath is not { } archivePath)
                        {
                            lookup = new Lazy<IStringsLookup>(() =>
                                {
                                    return new StringsLookupOverlay(source.SourcePath, source.Source, encodings.GetEncoding(_gameRelease.Release, source.Language), fileSystem: _fileSystem);
                                },
                                LazyThreadSafetyMode.ExecutionAndPublication);
                        }
                        else
                        {
                            if (!archives.TryGetValue(archivePath, out var archive))
                            {
                                archives[archivePath] = archive = new Lazy<IArchiveReader>(
                                    () => Archive.CreateReader(_gameRelease.Release, archivePath, _fileSystem),
                                    LazyThreadSafetyMode.ExecutionAndPublication);
                            }
                            var entryPath = source.ArchiveEntryPath!;
                            lookup = new Lazy<IStringsLookup>(() =>
                            {
                                try
                                {
                                    if (!archive.Value.TryGetFile(entryPath, out var file))
                                    {
                                        throw new FileNotFoundException($"{archivePath} no longer holds {entryPath}");
                                    }
                                    return new StringsLookupOverlay(file.GetMemorySlice(), source.Source, encodings.GetEncoding(_gameRelease.Release, source.Language));
                                }
                                catch (Exception ex)
                                {
                                    ArchiveException.EnrichWithFileAccessedAndThrow("String file from BSA failed to parse", ex, entryPath);
                                    throw;
                                }
                            }, LazyThreadSafetyMode.ExecutionAndPublication);
                        }
                        bundle.Get(source.Source)[source.Language] = new StringsFolderLookupOverlay.LookupItem(source, lookup);
                    }
                    return bundle;
                }),
            dataPath: _dataDirectoryProvider.Path,
            modKey: modKey);
    }

    /// <summary>
    /// Where each of a plugin's strings tables (a source in a language) is read from, the rule every lookup made here
    /// uses (C58/W10): the loose files of the Strings folder first, then, for a source and language no loose file has,
    /// the first archive that holds it among the archives that apply to the plugin (those named after it and those the
    /// game's ini lists, in Mutagen's archive order). An archive's file is in its Strings folder when its path starts
    /// with "strings". Only the folder listing and the archives' file lists (a BA2's name table) are read: no strings
    /// file is opened. Sources come in the order Normal, DL, IL, as <see cref="StringsFolderLookupOverlay.EnumerateTables"/>
    /// lists them.
    /// </summary>
    public IReadOnlyList<StringsTableSource> ResolveSources(ModKey modKey)
    {
        var languageFormat = GameConstants.Get(_gameRelease.Release).StringsLanguageFormat ?? throw new ArgumentException($"Tried to get language format for an unsupported game: {_gameRelease.Release}", nameof(_gameRelease.Release));
        var stringsFolderPath = _readParameters?.StringsFolderOverride;
        if (stringsFolderPath == null)
        {
            stringsFolderPath = Path.Combine(_dataDirectoryProvider.Path, "Strings");
        }

        var normal = new Dictionary<Language, StringsTableSource>();
        var dl = new Dictionary<Language, StringsTableSource>();
        var il = new Dictionary<Language, StringsTableSource>();
        Dictionary<Language, StringsTableSource> Get(StringsSource source) => source switch
        {
            StringsSource.Normal => normal,
            StringsSource.IL => il,
            StringsSource.DL => dl,
            _ => throw new NotImplementedException(),
        };

        if (_fileSystem.Directory.Exists(stringsFolderPath.Value))
        {
            var stringsFiles = _fileSystem.Directory
                .EnumerateFilesIgnoringCase(stringsFolderPath.Value, $"{modKey.Name}*{StringsUtility.StringsFileExtension}");
            foreach (var file in stringsFiles)
            {
                if (!StringsUtility.TryRetrieveInfoFromString(
                        languageFormat,
                        file.Name.String,
                        out var type,
                        out var lang,
                        out var modName)
                    || !modKey.Name.AsSpan().Equals(modName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                Get(type)[lang] = new StringsTableSource(type, lang, file.Path, ArchivePath: null, ArchiveEntryPath: null);
            }
        }
        foreach (var bsaFile in _getApplicableArchivePaths.Get(modKey))
        {
            try
            {
                var bsaReader = Archive.CreateReader(_gameRelease.Release, bsaFile, _fileSystem);
                try
                {
                    foreach (var itemPath in bsaReader.EnumerateFolderFilePaths("strings"))
                    {
                        var itemFileName = Path.GetFileName(IFileSystemExt.CleanDirectorySeparators(itemPath));
                        if (!StringsUtility.TryRetrieveInfoFromString(
                                languageFormat,
                                itemFileName,
                                out var type,
                                out var lang,
                                out var modName))
                        {
                            continue;
                        }
                        if (!MemoryExtensions.Equals(modKey.Name, modName, StringComparison.OrdinalIgnoreCase)) continue;
                        var dict = Get(type);
                        if (dict.ContainsKey(lang)) continue;
                        dict[lang] = new StringsTableSource(type, lang, Path.Combine(bsaFile, itemPath), ArchivePath: bsaFile.Path, ArchiveEntryPath: itemPath);
                    }
                }
                catch (Exception ex)
                {
                    ArchiveException.EnrichWithFolderAccessedAndThrow("BSA folder failed to parse for string file", ex, "strings");
                    throw;
                }
            }
            catch (Exception ex)
            {
                ArchiveException.EnrichWithArchivePathAndThrow("BSA failed to parse for string file", ex, bsaFile);
                throw;
            }
        }
        return normal.Values.Concat(dl.Values).Concat(il.Values).ToArray();
    }
}
