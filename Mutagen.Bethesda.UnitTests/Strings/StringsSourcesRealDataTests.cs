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

    /// <summary>
    /// C74: every plugin of the Data folder resolved in one batch gives the same tables as a call per plugin, and the
    /// batch reads each archive's listing once (SFSearch's own copy: 0.4 s for its load order; the per-plugin call 5.5 s)
    /// </summary>
    [Fact]
    [Trait("Category", "RealData")]
    public void RealData_DataFolder_BatchIsThePerPluginResolution()
    {
        var data = Environment.GetEnvironmentVariable("MUTAGEN_STARFIELD_DATA");
        if (string.IsNullOrEmpty(data)) return; // needs the game's Data folder
        var plugins = Directory.EnumerateFiles(data)
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".esm" or ".esp" or ".esl")
            .Select(f => ModKey.FromFileName(Path.GetFileName(f)))
            .ToArray();

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var batch = StringsFolderLookupOverlay.ResolveSources(GameRelease.Starfield, plugins, data);
        var batchTime = watch.Elapsed;

        watch.Restart();
        var single = plugins.ToDictionary(p => p, p => StringsFolderLookupOverlay.ResolveSources(GameRelease.Starfield, p, data));
        var singleTime = watch.Elapsed;

        batch.Count.ShouldBe(plugins.Length);
        foreach (var plugin in plugins)
        {
            batch[plugin].ShouldBe(single[plugin], plugin.FileName);
        }
        var tables = batch.Values.Sum(x => x.Count);
        _output.WriteLine($"{plugins.Length} plugins, {tables} tables ({batch.Values.Count(x => x.Count > 0)} plugins with tables): " +
                          $"batch {batchTime.TotalSeconds:0.00} s, a call per plugin {singleTime.TotalSeconds:0.00} s");
        tables.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// C77: a resolver kept between calls gives the batch's answer (39 plugins with tables, 1,053 tables) and reads no
    /// name table on a second call
    /// </summary>
    [Fact]
    [Trait("Category", "RealData")]
    public void RealData_DataFolder_KeptResolver()
    {
        var data = Environment.GetEnvironmentVariable("MUTAGEN_STARFIELD_DATA");
        if (string.IsNullOrEmpty(data)) return; // needs the game's Data folder
        var plugins = Directory.EnumerateFiles(data)
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".esm" or ".esp" or ".esl")
            .Select(f => ModKey.FromFileName(Path.GetFileName(f)))
            .ToArray();
        var resolver = new StringsSourcesResolver(GameRelease.Starfield, data);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var first = resolver.ResolveSources(plugins);
        var firstTime = watch.Elapsed;
        watch.Restart();
        var second = resolver.ResolveSources(plugins);
        var secondTime = watch.Elapsed;
        watch.Restart();
        var single = plugins.ToDictionary(p => p, p => resolver.ResolveSources(p));
        var singleTime = watch.Elapsed;

        var reference = StringsFolderLookupOverlay.ResolveSources(GameRelease.Starfield, plugins, data);
        foreach (var plugin in plugins)
        {
            first[plugin].ShouldBe(reference[plugin], plugin.FileName);
            second[plugin].ShouldBe(reference[plugin], plugin.FileName);
            single[plugin].ShouldBe(reference[plugin], plugin.FileName);
        }
        first.Values.Count(x => x.Count > 0).ShouldBe(39);
        first.Values.Sum(x => x.Count).ShouldBe(1053);
        _output.WriteLine($"{plugins.Length} plugins: first batch {firstTime.TotalSeconds:0.000} s, second batch {secondTime.TotalSeconds:0.000} s, " +
                          $"a call per plugin on the kept resolver {singleTime.TotalSeconds:0.000} s ({singleTime.TotalMilliseconds / plugins.Length:0.0} ms each)");
    }

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
