using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Strings;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Strings;

// C187: a text tied to a strings ID could only be built by Mutagen's own strings lookup (internal fields); SFSearch set
// StringsKey by reflection from its own IStringsFolderLookup. TranslatedString.FromStringsKey is the public way.
public class TranslatedStringFromStringsKeyTests
{
    private static readonly ModKey Plugin = ModKey.FromFileName("Texts.esm");

    /// <summary>A strings lookup of one's own: two languages of the DL tables</summary>
    private sealed class OwnLookup : IStringsFolderLookup
    {
        public IReadOnlyCollection<Language> AvailableLanguages(StringsSource source) => new[] { Language.English, Language.French };

        public bool TryLookup(StringsSource source, Language language, uint key, [MaybeNullWhen(false)] out string str)
        {
            str = (source, language, key) switch
            {
                (StringsSource.DL, Language.English, 0x12) => "A long text",
                (StringsSource.DL, Language.French, 0x12) => "Un long texte",
                _ => null,
            };
            return str != null;
        }

        public TranslatedString CreateString(StringsSource source, uint key, Language targetLanguage)
            => TranslatedString.FromStringsKey(key, source, targetLanguage, this, Plugin);
    }

    [Fact]
    public void FromOwnLookup_HasTheTextsAndTheId()
    {
        var text = new OwnLookup().CreateString(StringsSource.DL, 0x12, Language.French);
        text.String.ShouldBe("Un long texte");
        text.TryLookup(Language.English, out var english).ShouldBeTrue();
        english.ShouldBe("A long text");
        text.NumLanguages.ShouldBe(2);
        text.StringsKey.ShouldBe(0x12u);
        text.StringsKeySource.ShouldBe(StringsSource.DL);
        text.StringsKeyPlugin.ShouldBe(Plugin);
        text.ShouldBe(new TranslatedString(Language.French,
            new KeyValuePair<Language, string>(Language.English, "A long text"),
            new KeyValuePair<Language, string>(Language.French, "Un long texte")));
    }

    [Fact]
    public void IdNotInTheTables_KeepsTheId()
    {
        // SFSearch's key-only lookup: the ID of a translated field, no text
        var text = new OwnLookup().CreateString(StringsSource.IL, 0x99, Language.English);
        text.String.ShouldBeNull();
        text.StringsKey.ShouldBe(0x99u);
        text.StringsKeySource.ShouldBe(StringsSource.IL);
        text.StringsKeyPlugin.ShouldBe(Plugin);
    }

    [Fact]
    public void NoLookup_JustTheId()
    {
        var text = TranslatedString.FromStringsKey(7, StringsSource.Normal, Language.German, plugin: Plugin);
        text.String.ShouldBeNull();
        text.TargetLanguage.ShouldBe(Language.German);
        text.StringsKey.ShouldBe(7u);
        text.StringsKeySource.ShouldBe(StringsSource.Normal);
        text.StringsKeyPlugin.ShouldBe(Plugin);
        text.String = "Set later";
        text.String.ShouldBe("Set later");
        text.StringsKey.ShouldBe(7u);
    }
}
