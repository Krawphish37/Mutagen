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
        return new StringsFolderLookupOverlayFactoryContainer(release, fileSystem, dataPath, instructions).Resolve().Value
            .InternalFactory(modKey);
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