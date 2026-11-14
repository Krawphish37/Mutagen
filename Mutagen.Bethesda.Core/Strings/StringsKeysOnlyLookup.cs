using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins;

namespace Mutagen.Bethesda.Strings;

/// <summary>
/// A strings lookup with no texts: a localized plugin read through it gives each translated field its string ID only
/// (<see cref="TranslatedString.StringsKey"/>, <see cref="TranslatedString.StringsKeySource"/> and
/// <see cref="TranslatedString.StringsKeyPlugin"/>), and no text. No strings file or archive is opened.<br/>
/// For a reader that only needs the IDs, or that looks the texts up itself (C197).
/// </summary>
public sealed class StringsKeysOnlyLookup : IStringsFolderLookup
{
    /// <summary>
    /// The plugin whose .strings tables the IDs belong to
    /// </summary>
    public ModKey ModKey { get; }

    /// <param name="modKey">The plugin whose .strings tables the IDs belong to</param>
    public StringsKeysOnlyLookup(ModKey modKey)
    {
        ModKey = modKey;
    }

    /// <summary>
    /// None: there are no texts
    /// </summary>
    public IReadOnlyCollection<Language> AvailableLanguages(StringsSource source) => Array.Empty<Language>();

    /// <summary>
    /// Always false: there are no texts
    /// </summary>
    public bool TryLookup(StringsSource source, Language language, uint key, [MaybeNullWhen(false)] out string str)
    {
        str = null;
        return false;
    }

    /// <summary>
    /// A text with the ID and no text
    /// </summary>
    public TranslatedString CreateString(StringsSource source, uint key, Language targetLanguage)
    {
        return TranslatedString.FromStringsKey(key, source, targetLanguage, lookup: null, plugin: ModKey);
    }
}
