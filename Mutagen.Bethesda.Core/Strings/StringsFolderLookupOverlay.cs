using Mutagen.Bethesda.Plugins;
using Noggog;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using Mutagen.Bethesda.Environments.DI;
using Mutagen.Bethesda.Plugins.Utility;
using Mutagen.Bethesda.Strings.DI;
using StrongInject;

namespace Mutagen.Bethesda.Strings;

[RegisterModule(typeof(MutagenStrongInjectModule))]
internal partial class StringsFolderLookupOverlayFactoryContainer : IContainer<IStringsFolderLookupFactory>
{
    [Instance] private readonly IGameReleaseContext _release;
    [Instance] private readonly IFileSystem _fileSystem;
    [Instance] private readonly IDataDirectoryProvider _dataDirectory;
    [Instance] private readonly StringsReadParameters? _readParameters;

    public StringsFolderLookupOverlayFactoryContainer(
        GameRelease release,
        IFileSystem? fileSystem,
        DirectoryPath dataDirectory,
        StringsReadParameters? readParameters)
    {
        _readParameters = readParameters;
        _release = new GameReleaseInjection(release);
        _fileSystem = fileSystem.GetOrDefault();
        _dataDirectory = new DataDirectoryInjection(dataDirectory);
        if (_readParameters?.BsaFolderOverride != null)
        {
            _dataDirectory = new DataDirectoryInjection(_readParameters.BsaFolderOverride.Value);
        }
    }
}

public sealed class StringsFolderLookupOverlay : IStringsFolderLookup
{
    private readonly Lazy<DictionaryBundle> _dictionaries;

    public DirectoryPath DataPath { get; }
    public ModKey ModKey { get; }

    internal record LookupItem(StringsTableSource Source, Lazy<IStringsLookup> StringsLookup);

    internal class DictionaryBundle
    {
        private readonly Dictionary<Language, LookupItem> _strings = new();
        private readonly Dictionary<Language, LookupItem> _dlStrings = new();
        private readonly Dictionary<Language, LookupItem> _ilStrings = new();

        public bool Empty =>
            _strings.Count == 0
            && _dlStrings.Count == 0
            && _ilStrings.Count == 0;

        public Dictionary<Language, LookupItem> Get(StringsSource source)
        {
            return source switch
            {
                StringsSource.Normal => _strings,
                StringsSource.IL => _ilStrings,
                StringsSource.DL => _dlStrings,
                _ => throw new NotImplementedException(),
            };
        }
    }

    public bool Empty => _dictionaries.Value.Empty;

    internal StringsFolderLookupOverlay(Lazy<DictionaryBundle> instantiator, DirectoryPath dataPath, ModKey modKey)
    {
        _dictionaries = instantiator;
        DataPath = dataPath;
        ModKey = modKey;
    }

    
    // todo integrate IAssetProvider
    public static StringsFolderLookupOverlay TypicalFactory(
        GameRelease release, 
        ModKey modKey, 
        DirectoryPath dataPath,
        StringsReadParameters? instructions,
        IFileSystem? fileSystem = null)
    {
        _readFrom[modKey] = (release, dataPath, instructions, fileSystem);
        return new StringsFolderLookupOverlayFactoryContainer(release, fileSystem, dataPath, instructions).Resolve().Value
            .InternalFactory(modKey);
    }

    // Where each plugin's tables were last read from (TypicalFactory). A text read from a table keeps the ID and the
    // plugin, not the table (TranslatedString lets the lookup go once it holds every language), so StringsWriter opens
    // the tables again from here when the plugin is written back, to write them as they were read
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ModKey, (GameRelease Release, DirectoryPath DataPath, StringsReadParameters? Instructions, IFileSystem? FileSystem)> _readFrom = new();

    /// <summary>
    /// The tables a plugin's strings were last read from in this process, opened again; null when none were, or they
    /// can't be opened now. What StringsWriter holds a plugin's strings against: a table is written back as it was
    /// read only when every string of it is still the table's own.
    /// </summary>
    internal static StringsFolderLookupOverlay? ReopenLastRead(ModKey modKey)
    {
        if (!_readFrom.TryGetValue(modKey, out var from)) return null;
        try
        {
            return new StringsFolderLookupOverlayFactoryContainer(from.Release, from.FileSystem, from.DataPath, from.Instructions).Resolve().Value
                .InternalFactory(modKey) as StringsFolderLookupOverlay;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Where each of a plugin's strings tables (a source in a language) is read from, without making a lookup or opening
    /// any table (C58/W10): the same rule, and the same list, as <see cref="EnumerateTables"/> of the lookup
    /// <see cref="TypicalFactory"/> makes. Loose files of the Strings folder win; for a source and language no loose file
    /// has, the first archive that holds it among those that apply to the plugin (named after it, or listed in the
    /// game's ini), in Mutagen's archive order. Only the folder listing and the archives' file lists (a BA2's name table)
    /// are read. Sources come in the order Normal, DL, IL.
    /// </summary>
    /// <param name="release">The game release</param>
    /// <param name="modKey">The plugin</param>
    /// <param name="dataPath">The data folder (its Strings folder and archives)</param>
    /// <param name="instructions">Strings folder or archive folder overrides, as for <see cref="TypicalFactory"/></param>
    /// <param name="fileSystem">The file system</param>
    public static IReadOnlyList<StringsTableSource> ResolveSources(
        GameRelease release,
        ModKey modKey,
        DirectoryPath dataPath,
        StringsReadParameters? instructions = null,
        IFileSystem? fileSystem = null)
    {
        return new StringsSourcesResolver(release, dataPath, instructions, fileSystem).ResolveSources(modKey);
    }

    /// <summary>
    /// <see cref="ResolveSources(GameRelease, ModKey, DirectoryPath, StringsReadParameters?, IFileSystem?)"/> for many
    /// plugins at once (C74): the Strings folder is listed once and each archive's strings listing is read once for all of
    /// them, where a call per plugin reads the archives the ini lists again for every plugin. The same lists as a call per
    /// plugin.
    /// </summary>
    /// <remarks>A host that resolves again and again keeps a <see cref="StringsSourcesResolver"/> instead: its archive
    /// listings last between calls (C77)</remarks>
    /// <param name="release">The game release</param>
    /// <param name="modKeys">The plugins (a load order's, say); one listed twice is resolved once</param>
    /// <param name="dataPath">The data folder (its Strings folder and archives)</param>
    /// <param name="instructions">Strings folder or archive folder overrides, as for <see cref="TypicalFactory"/></param>
    /// <param name="fileSystem">The file system</param>
    /// <returns>Each plugin's tables</returns>
    public static IReadOnlyDictionary<ModKey, IReadOnlyList<StringsTableSource>> ResolveSources(
        GameRelease release,
        IEnumerable<ModKey> modKeys,
        DirectoryPath dataPath,
        StringsReadParameters? instructions = null,
        IFileSystem? fileSystem = null)
    {
        return new StringsSourcesResolver(release, dataPath, instructions, fileSystem).ResolveSources(modKeys);
    }

    /// <inheritdoc />
    public bool TryLookup(StringsSource source, Language language, uint key, [MaybeNullWhen(false)] out string str)
    {
        var dict = Get(source);
        if (!dict.TryGetValue(language, out var lookup))
        {
            str = default;
            return false;
        }
        return lookup.StringsLookup.Value.TryLookup(key, out str);
    }

    public bool TryLookup(
        StringsSource source,
        Language language,
        uint key,
        [MaybeNullWhen(false)] out string str,
        [MaybeNullWhen(false)] out string sourcePath)
    {
        var dict = Get(source);
        if (!dict.TryGetValue(language, out var lookup))
        {
            str = default;
            sourcePath = default;
            return false;
        }
        if (!lookup.StringsLookup.Value.TryLookup(key, out str))
        {
            sourcePath = default;
            return false;
        }

        sourcePath = lookup.Source.SourcePath;
        return true;
    }

    internal Dictionary<Language, LookupItem> Get(StringsSource source) => _dictionaries.Value.Get(source);

    /// <summary>The table of one language as it was read, or null when the plugin has none (StringsWriter)</summary>
    internal StringsLookupOverlay? TryGetTable(StringsSource source, Language language)
    {
        return Get(source).TryGetValue(language, out var lookup) ? lookup.StringsLookup.Value as StringsLookupOverlay : null;
    }

    /// <summary>
    /// Lists every strings table the plugin has: one per source (.strings, .dlstrings, .ilstrings) and language,
    /// with the file or archive it is read from. Sources come in the order Normal, DL, IL. Loose files win over
    /// archives, as for lookups. No strings file is opened.
    /// </summary>
    public IEnumerable<StringsTableSource> EnumerateTables()
    {
        foreach (var source in new[] { StringsSource.Normal, StringsSource.DL, StringsSource.IL })
        {
            foreach (var item in Get(source).Values)
            {
                yield return item.Source;
            }
        }
    }

    /// <summary>
    /// Where one strings table (a source in a language) is read from: a loose file, or an entry inside an archive.
    /// Loose files win over archives, as for lookups. No strings file is opened.
    /// </summary>
    /// <param name="source">Strings source (.strings, .dlstrings or .ilstrings)</param>
    /// <param name="language">Language</param>
    /// <param name="table">The table's source, when the plugin has the table</param>
    /// <returns>False when the plugin has no such table</returns>
    public bool TryGetTableSource(StringsSource source, Language language, [MaybeNullWhen(false)] out StringsTableSource table)
    {
        if (!Get(source).TryGetValue(language, out var lookup))
        {
            table = default;
            return false;
        }
        table = lookup.Source;
        return true;
    }

    /// <summary>
    /// Every string of one table (a source in a language): each ID with its text, in the file's order. Read from the
    /// same file with the same encoding as <see cref="TryLookup(StringsSource, Language, uint, out string)"/>, so every
    /// listed ID gives the same text through a lookup. Empty when the plugin has no such table. The table is opened
    /// on first use and shared with lookups.
    /// </summary>
    /// <param name="source">Strings source (.strings, .dlstrings or .ilstrings)</param>
    /// <param name="language">Language</param>
    public IEnumerable<KeyValuePair<uint, string>> EnumerateStrings(StringsSource source, Language language)
    {
        if (!Get(source).TryGetValue(language, out var lookup)) yield break;
        foreach (var item in lookup.StringsLookup.Value)
        {
            yield return item;
        }
    }

    /// <summary>
    /// How many strings one table (a source in a language) holds. Opens the table, as a lookup would.
    /// </summary>
    /// <param name="source">Strings source (.strings, .dlstrings or .ilstrings)</param>
    /// <param name="language">Language</param>
    /// <returns>The count, or 0 when the plugin has no such table</returns>
    public int GetStringsCount(StringsSource source, Language language)
    {
        if (!Get(source).TryGetValue(language, out var lookup)) return 0;
        return lookup.StringsLookup.Value switch
        {
            StringsLookupOverlay overlay => overlay.Count,
            var other => other.Count(),
        };
    }

    public TranslatedString CreateString(StringsSource source, uint key, Language defaultLanguage)
    {
        return TranslatedString.FromStringsKey(key, source, defaultLanguage, this);
    }

    public IReadOnlyCollection<Language> AvailableLanguages(StringsSource source)
    {
        return Get(source).Keys;
    }
}