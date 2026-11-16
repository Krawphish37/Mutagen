using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.HandReadLayoutFile;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Mutagen.Bethesda.UnitTests.Strings;

/// <summary>
/// C58/W10: where each official plugin's strings tables come from (SFSearch's strings stamp works it out), resolved by
/// StringsFolderLookupOverlay.ResolveSources without opening a table, and the same as the lookups read
/// </summary>
public class StringsSourcesRealDataTests
{
    private readonly ITestOutputHelper _output;

    public StringsSourcesRealDataTests(ITestOutputHelper output) => _output = output;

    [Fact]
    [Trait("Category", "RealData")]
    public void RealData_OfficialPlugins_ResolveSourcesIsWhatTheLookupsRead()
    {
        var data = Environment.GetEnvironmentVariable("MUTAGEN_STARFIELD_DATA");
        if (string.IsNullOrEmpty(data)) return; // needs the game's Data folder
        int localized = 0, tables = 0, fromArchives = 0;
        foreach (var name in HandReadLayoutFileTests.OfficialPlugins)
        {
            var modKey = ModKey.FromFileName(name);
            var resolved = StringsFolderLookupOverlay.ResolveSources(GameRelease.Starfield, modKey, data);
            var lookup = StringsFolderLookupOverlay.TypicalFactory(GameRelease.Starfield, modKey, data, null);
            resolved.ShouldBe(lookup.EnumerateTables().ToArray(), name);
            var isLocalized = (ModHeaderFrame.FromPath(Path.Combine(data, name), GameRelease.Starfield).Flags & 0x80) != 0;
            if (!isLocalized) continue;
            localized++;

            tables += resolved.Count;
            fromArchives += resolved.Count(x => x.FromArchive);
            // The table each source names is the one the lookup reads: its first string looks up the same
            var english = resolved.FirstOrDefault(x => x.Source == StringsSource.Normal && x.Language == Language.English);
            if (english == null) continue;
            var first = lookup.EnumerateStrings(StringsSource.Normal, Language.English).First();
            lookup.TryLookup(StringsSource.Normal, Language.English, first.Key, out var text, out var sourcePath).ShouldBeTrue(name);
            text.ShouldBe(first.Value);
            sourcePath.ShouldBe(english.SourcePath);
        }
        _output.WriteLine($"{localized} localized plugins, {tables} tables, {fromArchives} of them in archives");
        localized.ShouldBeGreaterThan(0);
        tables.ShouldBeGreaterThan(0);
    }
}
