using Mutagen.Bethesda.Plugins;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Mutagen.Bethesda.Strings;

/// <summary>
/// A string that can be represented in multiple different languages.<br/>
/// Threadsafe.
/// </summary>
[DebuggerDisplay("{String}")]
public sealed class TranslatedString : ITranslatedString, IEquatable<TranslatedString>, IOptionalStringsKeyGetter
{
    /// <summary>
    /// Whether to only consider the DefaultLanguage in equality/hash comparisons.
    /// This is not good to change during a program's execution.  It should only be 
    /// modified early during initialization.
    /// </summary>
    public static bool DefaultLanguageComparisonOnly = true;

    private static Language _defaultLanguage;

    /// <summary>
    /// The default language to use as the main target language
    /// </summary>
    public static Language DefaultLanguage
    {
        get => _defaultLanguage;
        set
        {
            _defaultLanguage = value;
            _empty = new(value, string.Empty);
        }
    }

    /// <summary>
    /// Language the string is targeting, and will be set/return when accessed normally
    /// </summary>
    public Language TargetLanguage { get; }

    private string? _directString;
    private readonly object _lock = new();
    internal Dictionary<Language, string?>? _localization;

    // Alternate way of populating a Translated String
    // Will cause it to act in a lazy lookup fashion
    internal uint? _stringsKey;
    internal IStringsFolderLookup? StringsLookup;
    internal StringsSource StringsSource;
    private ModKey? _resolvedFromMod;

    /// <summary>
    /// The ID of this text in its plugin's .strings tables (null for a text without one: a new text, or one read from
    /// a plugin that isn't localized).<br/>
    /// Writing that same plugin keeps the ID, as long as the text under it is the same everywhere it is used.
    /// Setting it keeps <see cref="StringsKeySource"/> and <see cref="StringsKeyPlugin"/>; use
    /// <see cref="SetStringsKey"/> to give a text built some other way (e.g. from JSON) the ID it had in its plugin.
    /// </summary>
    public uint? StringsKey
    {
        get => _stringsKey;
        set
        {
            lock (_lock)
            {
                // A text still looked up from its tables is read in full first, so it keeps its text
                ResolveAllStringSources();
                _stringsKey = value;
            }
        }
    }

    /// <summary>
    /// Which of the plugin's .strings tables <see cref="StringsKey"/> belongs to
    /// </summary>
    public StringsSource StringsKeySource => StringsSource;

    /// <summary>
    /// The plugin whose .strings tables <see cref="StringsKey"/> belongs to (null: none)
    /// </summary>
    public ModKey? StringsKeyPlugin => StringsMod;

    /// <summary>
    /// Ties this text to entry <paramref name="key"/> of <paramref name="plugin"/>'s .strings tables of kind
    /// <paramref name="source"/>, as if it had been read from that plugin, so writing that plugin keeps the ID.
    /// The text itself is not changed. A null key removes the tie.
    /// </summary>
    public void SetStringsKey(uint? key, StringsSource source, ModKey plugin)
    {
        lock (_lock)
        {
            ResolveAllStringSources();
            _stringsKey = key;
            StringsSource = source;
            _resolvedFromMod = key == null ? default(ModKey?) : plugin;
        }
    }

    /// <summary>
    /// A text of a plugin's .strings tables: entry <paramref name="key"/> of its tables of kind
    /// <paramref name="source"/>, with its text in every language <paramref name="lookup"/> has, looked up now, as
    /// reading a localized plugin does. The text keeps the ID (<see cref="StringsKey"/>, <see cref="StringsKeySource"/>,
    /// <see cref="StringsKeyPlugin"/>), so writing that plugin keeps it. An <see cref="IStringsFolderLookup"/> of one's
    /// own can return it from <see cref="IStringsFolderLookup.CreateString"/>.
    /// </summary>
    /// <param name="key">The ID in the plugin's .strings tables</param>
    /// <param name="source">Which of the plugin's tables the ID belongs to</param>
    /// <param name="targetLanguage">The language the text is read and set in by default</param>
    /// <param name="lookup">Where the texts are looked up; null: the text has the ID but no text yet</param>
    /// <param name="plugin">The plugin whose tables the ID belongs to; null: the lookup's plugin when it is a
    /// <see cref="StringsFolderLookupOverlay"/></param>
    public static TranslatedString FromStringsKey(
        uint key,
        StringsSource source,
        Language targetLanguage,
        IStringsFolderLookup? lookup = null,
        ModKey? plugin = null)
    {
        var ret = new TranslatedString(targetLanguage)
        {
            StringsLookup = lookup,
            _stringsKey = key,
            StringsSource = source,
            _resolvedFromMod = plugin,
        };
        ret.ResolveAllStringSources();
        return ret;
    }

    /// <summary>
    /// The plugin whose string tables StringsKey belongs to
    /// </summary>
    internal ModKey? StringsMod => StringsLookup switch
    {
        StringsFolderLookupOverlay overlay => overlay.ModKey,
        StringsKeysOnlyLookup keysOnly => keysOnly.ModKey,
        _ => _resolvedFromMod,
    };

    internal bool UsingLocalizationDictionary => _localization != null || StringsLookup != null;

    /// <inheritdoc />
    public string? String
    {
        get
        {
            lock (_lock)
            {
                if (!UsingLocalizationDictionary) return _directString;
                if (TryLookup(TargetLanguage, out var str))
                {
                    return str;
                }
                return null;
            }
        }
        set => Set(TargetLanguage, value);
    }

    public int NumLanguages
    {
        get
        {
            ResolveAllStringSources();
            return _localization?.Count ?? 1;
        }
    }

    private static TranslatedString _empty = new(Language.English, string.Empty);
    public static ITranslatedStringGetter Empty => _empty;

    /// <summary>
    /// Creates a translated string with empty string set for the default language
    /// </summary>
    /// <param name="language">Optional target language override</param>
    public TranslatedString(Language language)
    {
        TargetLanguage = language;
    }

    /// <summary>
    /// Creates a translated string with a value for the default language
    /// </summary>
    /// <param name="targetLanguage">Target language override</param>
    /// <param name="directString">String to register for the default language</param>
    public TranslatedString(Language targetLanguage, string? directString)
    {
        _directString = directString;
        TargetLanguage = targetLanguage;
    }

    /// <summary>
    /// Creates a translated string with a number of strings for languages.
    /// If no string is provided for the default language, string.Empty will be assigned.
    /// </summary>
    /// <param name="targetLanguage">Target language override</param>
    /// <param name="strs">Language string pairs to register</param>
    public TranslatedString(Language targetLanguage, IEnumerable<KeyValuePair<Language, string>> strs)
    {
        _localization = new Dictionary<Language, string?>();
        foreach (var str in strs)
        {
            _localization[str.Key] = str.Value;
        }
        TargetLanguage = targetLanguage;
    }

    /// <summary>
    /// Creates a translated string with a number of strings for languages.
    /// If no string is provided for the default language, string.Empty will be assigned.
    /// </summary>
    /// <param name="targetLanguage">Target language override</param>
    /// <param name="strs">Language string pairs to register</param>
    public TranslatedString(Language targetLanguage, params KeyValuePair<Language, string>[] strs)
        : this(targetLanguage, (IEnumerable<KeyValuePair<Language, string>>)strs)
    {
    }

    /// <inheritdoc />
    public bool TryLookup(Language language, [MaybeNullWhen(false)] out string str)
    {
        lock (_lock)
        {
            if (TargetLanguage == language
                && !UsingLocalizationDictionary)
            {
                str = _directString;
                return str != null;
            }

            if (_localization != null
                && _localization.TryGetValue(language, out str))
            {
                return str != null;
            }
            else if (StringsLookup != null)
            {
                if (_localization == null)
                {
                    _localization = new Dictionary<Language, string?>();
                }
                if (StringsLookup.TryLookup(StringsSource, language, StringsKey!.Value, out str))
                {
                    _localization[language] = str;
                    return true;
                }
                else
                {
                    _localization[language] = null;
                    return false;
                }
            }
        }

        str = default;
        return false;
    }

    /// <summary>
    /// Sets the string for a specific language
    /// </summary>
    /// <param name="language">Language to register the string under</param>
    /// <param name="str">String to register</param>
    public void Set(Language language, string? str)
    {
        lock (_lock)
        {
            if (_localization == null)
            {
                if (language == TargetLanguage
                    && !UsingLocalizationDictionary)
                {
                    _directString = str;
                    return;
                }

                _localization = CreateLocalization();
            }
            _localization[language] = str;
        }
    }

    /// <inheritdoc />
    public void RemoveNonDefault(Language language)
    {
        if (language == TargetLanguage) return;
        lock (_lock)
        {
            if (_localization == null)
            {
                _localization = CreateLocalization();
            }
            _localization[language] = null;
        }
    }

    private Dictionary<Language, string?> CreateLocalization()
    {
        var ret = new Dictionary<Language, string?>();

        // Swap direct string to the internal setup where it's stored in the dictionary
        if (_directString != null)
        {
            ret[TargetLanguage] = _directString;
            _directString = null;
        }
        return ret;
    }

    /// <inheritdoc />
    public void ClearNonDefault()
    {
        lock (_lock)
        {
            if (_localization == null) return;
            if (!_localization.TryGetValue(TargetLanguage, out _directString))
            {
                _directString = null;
            }
            _localization = null;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        ClearNonDefault();
        _directString = null;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<KeyValuePair<Language, string>> GetEnumerator()
    {
        lock (_lock)
        {
            ResolveAllStringSources();
            if (_localization == null)
            {
                if (_directString != null)
                {
                    yield return new KeyValuePair<Language, string>(TargetLanguage, _directString);
                }
            }
            else
            {
                foreach (var item in _localization)
                {
                    if (item.Value == null) continue;
                    yield return new KeyValuePair<Language, string>(item.Key, item.Value);
                }
            }
        }
    }

    internal void ResolveAllStringSources()
    {
        lock (_lock)
        {
            if (StringsLookup == null) return;
            if (_localization == null)
            {
                _localization = new Dictionary<Language, string?>();
            }
            foreach (var lang in StringsLookup.AvailableLanguages(StringsSource))
            {
                if (_localization.ContainsKey(lang)) continue;
                if (StringsLookup.TryLookup(StringsSource, lang, StringsKey!.Value, out var str))
                {
                    _localization[lang] = str;
                }
                else
                {
                    _localization.Remove(lang);
                }
            }
            _resolvedFromMod = StringsMod;
            StringsLookup = null;
        }
    }

    [return: NotNullIfNotNull(parameterName: "str")]
    public static implicit operator TranslatedString?(string? str)
    {
        return str == null
            ? default(TranslatedString?) 
            : new TranslatedString(DefaultLanguage, str);
    }

    public static implicit operator string?(TranslatedString? str)
    {
        return str?.String;
    }

    public override string ToString()
    {
        return String ?? string.Empty;
    }

    public TranslatedString DeepCopy()
    {
        if (_directString == null)
        {
            return new TranslatedString(
                targetLanguage: TargetLanguage,
                strs: this.ToArray());
        }
        else
        {
            return new TranslatedString(
                targetLanguage: TargetLanguage,
                directString: _directString);
        }
    }

    public static readonly IEqualityComparer<ITranslatedStringGetter> OnlyDefaultComparer = new TranslatedStringOnlyDefaultComparer();

    public static readonly IEqualityComparer<ITranslatedStringGetter> AllLanguageComparer = new TranslatedStringComparer();

    public override bool Equals(object? obj)
    {
        if (obj is not TranslatedString str) return false;
        return Equals(str);
    }

    public override int GetHashCode()
    {
        return (DefaultLanguageComparisonOnly ? OnlyDefaultComparer : AllLanguageComparer).GetHashCode(this);
    }

    public bool Equals(TranslatedString? other)
    {
        return (DefaultLanguageComparisonOnly ? OnlyDefaultComparer : AllLanguageComparer).Equals(this, other);
    }
}

internal sealed class TranslatedStringOnlyDefaultComparer : IEqualityComparer<ITranslatedStringGetter>
{
    public bool Equals(ITranslatedStringGetter? x, ITranslatedStringGetter? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x?.TargetLanguage != y?.TargetLanguage) return false;
        return string.Equals(x?.String, y?.String);
    }

    public int GetHashCode(ITranslatedStringGetter obj)
    {
        var ret = new HashCode();
        ret.Add(obj.String);
        ret.Add(obj.TargetLanguage);
        return ret.ToHashCode();
    }
}

internal sealed class TranslatedStringComparer : IEqualityComparer<ITranslatedStringGetter>
{
    public bool Equals(ITranslatedStringGetter? x, ITranslatedStringGetter? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x == null && y == null) return true;
        if (x == null || y == null) return false;
        if (x.NumLanguages != y.NumLanguages) return false;
        return x.OrderBy(kv => kv.Key).SequenceEqual(y.OrderBy(kv => kv.Key));
    }

    public int GetHashCode(ITranslatedStringGetter obj)
    {
        var code = new HashCode();
        foreach (var kv in obj)
        {
            code.Add(kv);
        }
        return code.ToHashCode();
    }
}