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
    internal IReadOnlyDictionary<ModKey, IReadOnlyList<StringsTableSource>> ResolveSources(IEnumerable<ModKey> modKeys);
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
    /// lists them.<br/>
    /// Each archive's strings listing is read once per factory and kept while the archive's size and time stay the same
    /// (C74), so later calls, and the lookups this factory makes, don't read it again.
    /// </summary>
    public IReadOnlyList<StringsTableSource> ResolveSources(ModKey modKey)
    {
        var languageFormat = LanguageFormat();
        return Resolve(modKey, LooseFilesOf(languageFormat, modKey), _getApplicableArchivePaths.Get(modKey), languageFormat, new Dictionary<string, Dictionary<string, List<Table>>>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <see cref="ResolveSources(ModKey)"/> for many plugins at once (C74): the Strings folder and the data folder's
    /// archives are listed once, and each archive's strings listing is read once for all of them (a load order's plugins
    /// share the archives the ini lists). The same lists as a call per plugin, in the order the plugins are given.
    /// </summary>
    /// <param name="modKeys">The plugins; one listed twice is resolved once</param>
    /// <returns>Each plugin's tables, as <see cref="ResolveSources(ModKey)"/> gives them</returns>
    public IReadOnlyDictionary<ModKey, IReadOnlyList<StringsTableSource>> ResolveSources(IEnumerable<ModKey> modKeys)
    {
        var languageFormat = LanguageFormat();
        var loose = LooseFiles(languageFormat, modKey: null);
        // The data folder's archives listed once for all of them
        var mods = modKeys.Distinct().ToArray();
        var archives = _getApplicableArchivePaths.Get(mods);
        var ret = new Dictionary<ModKey, IReadOnlyList<StringsTableSource>>();
        // Each archive is looked at (its size and time) once for all the plugins
        var seen = new Dictionary<string, Dictionary<string, List<Table>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var modKey in mods)
        {
            ret[modKey] = Resolve(
                modKey,
                loose.TryGetValue(modKey.Name, out var files) ? files : [],
                archives.TryGetValue(modKey, out var applicable) ? applicable : _getApplicableArchivePaths.Get(modKey),
                languageFormat,
                seen);
        }
        return ret;
    }

    private StringsLanguageFormat LanguageFormat() =>
        GameConstants.Get(_gameRelease.Release).StringsLanguageFormat
        ?? throw new ArgumentException($"Tried to get language format for an unsupported game: {_gameRelease.Release}", nameof(_gameRelease.Release));

    private readonly record struct Table(string Path, StringsSource Source, Language Language);

    /// <summary>
    /// The strings files of the Strings folder, by the plugin name they are for (OrdinalIgnoreCase), in the folder's
    /// order; only those of one plugin (listed by a name pattern) when <paramref name="modKey"/> is given
    /// </summary>
    private Dictionary<string, List<Table>> LooseFiles(StringsLanguageFormat languageFormat, ModKey? modKey)
    {
        var ret = new Dictionary<string, List<Table>>(StringComparer.OrdinalIgnoreCase);
        var stringsFolderPath = _readParameters?.StringsFolderOverride;
        if (stringsFolderPath == null)
        {
            stringsFolderPath = Path.Combine(_dataDirectoryProvider.Path, "Strings");
        }
        if (!_fileSystem.Directory.Exists(stringsFolderPath.Value)) return ret;
        var pattern = modKey is { } one ? $"{one.Name}*{StringsUtility.StringsFileExtension}" : $"*{StringsUtility.StringsFileExtension}";
        foreach (var file in _fileSystem.Directory.EnumerateFilesIgnoringCase(stringsFolderPath.Value, pattern))
        {
            if (!StringsUtility.TryRetrieveInfoFromString(
                    languageFormat,
                    file.Name.String,
                    out var type,
                    out var lang,
                    out var modName))
            {
                continue;
            }
            var name = modName.ToString();
            if (modKey is { } only && !only.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!ret.TryGetValue(name, out var list)) ret[name] = list = new List<Table>();
            list.Add(new Table(file.Path, type, lang));
        }
        return ret;
    }

    private IReadOnlyList<Table> LooseFilesOf(StringsLanguageFormat languageFormat, ModKey modKey) =>
        LooseFiles(languageFormat, (ModKey?)modKey).TryGetValue(modKey.Name, out var files) ? files : [];

    /// <param name="seen">The archives' listings already looked at in this call</param>
    private IReadOnlyList<StringsTableSource> Resolve(
        ModKey modKey,
        IReadOnlyList<Table> looseFiles,
        IEnumerable<FilePath> archives,
        StringsLanguageFormat languageFormat,
        Dictionary<string, Dictionary<string, List<Table>>> seen)
    {
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

        foreach (var file in looseFiles)
        {
            Get(file.Source)[file.Language] = new StringsTableSource(file.Source, file.Language, file.Path, ArchivePath: null, ArchiveEntryPath: null);
        }
        foreach (var bsaFile in archives)
        {
            if (!seen.TryGetValue(bsaFile.Path, out var listing))
            {
                seen[bsaFile.Path] = listing = ArchiveStrings(bsaFile, languageFormat);
            }
            if (!listing.TryGetValue(modKey.Name, out var entries)) continue;
            foreach (var entry in entries)
            {
                var dict = Get(entry.Source);
                if (dict.ContainsKey(entry.Language)) continue;
                dict[entry.Language] = new StringsTableSource(entry.Source, entry.Language, Path.Combine(bsaFile, entry.Path), ArchivePath: bsaFile.Path, ArchiveEntryPath: entry.Path);
            }
        }
        return normal.Values.Concat(dl.Values).Concat(il.Values).ToArray();
    }

    private sealed record ArchiveListing(long Length, DateTime Written, Dictionary<string, List<Table>> ByMod);

    // Each archive's strings files by the plugin name they are for (Table.Path: the entry's path in the archive), kept
    // while the archive's size and last write time stay the same
    private readonly Dictionary<string, ArchiveListing> _archiveListings = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, List<Table>> ArchiveStrings(FilePath bsaFile, StringsLanguageFormat languageFormat)
    {
        var info = _fileSystem.FileInfo.New(bsaFile.Path);
        var (length, written) = (info.Length, info.LastWriteTimeUtc);
        lock (_archiveListings)
        {
            if (_archiveListings.TryGetValue(bsaFile.Path, out var kept) && kept.Length == length && kept.Written == written)
            {
                return kept.ByMod;
            }
        }
        var byMod = new Dictionary<string, List<Table>>(StringComparer.OrdinalIgnoreCase);
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
                    var name = modName.ToString();
                    if (!byMod.TryGetValue(name, out var list)) byMod[name] = list = new List<Table>();
                    list.Add(new Table(itemPath, type, lang));
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
        lock (_archiveListings)
        {
            _archiveListings[bsaFile.Path] = new ArchiveListing(length, written, byMod);
        }
        return byMod;
    }
}
