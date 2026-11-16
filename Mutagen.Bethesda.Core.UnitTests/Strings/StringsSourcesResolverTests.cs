using System.IO.Abstractions.TestingHelpers;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.UnitTests.Archives;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Strings;

/// <summary>
/// C77 (SFSearch S185): a resolver a host keeps reuses each archive's strings listing between calls, and looks again at
/// what can change: an archive's size or write time, an archive added or removed, the Strings folder's file list
/// </summary>
public class StringsSourcesResolverTests
{
    private const string Data = @"C:\Data";
    private static readonly ModKey MyMod = ModKey.FromFileName("MyMod.esm");
    private static readonly string Main = Path.Combine(Data, "MyMod - Main.ba2");
    private static readonly DateTime Written = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private static MockFileSystem FileSystem()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(Data);
        return fs;
    }

    private static void WriteArchive(MockFileSystem fs, string path, DateTime written, params string[] entries)
    {
        fs.File.WriteAllBytes(path, Ba2Tests.BuildGeneralBa2(entries.Select(e => (e, new byte[] { 1, 2, 3, 4 })).ToArray()).Bytes);
        fs.File.SetLastWriteTimeUtc(path, written);
    }

    private static (StringsSource Source, Language Language, string? Archive)[] Tables(StringsSourcesResolver resolver) =>
        resolver.ResolveSources(new[] { MyMod })[MyMod].Select(t => (t.Source, t.Language, t.ArchivePath)).ToArray();

    [Fact]
    public void Reuse_ListingIsReadOnceAcrossCalls()
    {
        var fs = FileSystem();
        WriteArchive(fs, Main, Written, "strings/mymod_en.strings");
        var resolver = new StringsSourcesResolver(GameRelease.Starfield, Data, fileSystem: fs);
        var first = Tables(resolver);
        first.ShouldBe(new (StringsSource, Language, string?)[] { (StringsSource.Normal, Language.English, Main) });

        // The archive's bytes made unreadable, its size and write time kept: a resolver that read its name table again
        // would fail; the kept listing answers
        var length = fs.FileInfo.New(Main).Length;
        fs.File.WriteAllBytes(Main, new byte[length]);
        fs.File.SetLastWriteTimeUtc(Main, Written);
        Tables(resolver).ShouldBe(first);
        resolver.ResolveSources(MyMod).Select(t => (t.Source, t.Language, t.ArchivePath)).ShouldBe(first);
        // A new resolver reads it, and finds nothing it can read
        Should.Throw<Exception>(() => Tables(new StringsSourcesResolver(GameRelease.Starfield, Data, fileSystem: fs)));
    }

    [Fact]
    public void Refresh_ArchiveSizeChanged()
    {
        var fs = FileSystem();
        WriteArchive(fs, Main, Written, "strings/mymod_en.strings");
        var resolver = new StringsSourcesResolver(GameRelease.Starfield, Data, fileSystem: fs);
        Tables(resolver).Length.ShouldBe(1);
        WriteArchive(fs, Main, Written, "strings/mymod_en.strings", "strings/mymod_en.dlstrings");
        Tables(resolver).ShouldBe(new (StringsSource, Language, string?)[]
        {
            (StringsSource.Normal, Language.English, Main),
            (StringsSource.DL, Language.English, Main),
        });
    }

    [Fact]
    public void Refresh_ArchiveWriteTimeChanged()
    {
        var fs = FileSystem();
        WriteArchive(fs, Main, Written, "strings/mymod_en.strings");
        var resolver = new StringsSourcesResolver(GameRelease.Starfield, Data, fileSystem: fs);
        Tables(resolver).Single().Language.ShouldBe(Language.English);
        // Same size, another language, a later write time
        WriteArchive(fs, Main, Written.AddMinutes(1), "strings/mymod_de.strings");
        Tables(resolver).Single().Language.ShouldBe(Language.German);
    }

    [Fact]
    public void Refresh_ArchiveAddedAndRemoved()
    {
        var fs = FileSystem();
        WriteArchive(fs, Main, Written, "strings/mymod_en.strings");
        var resolver = new StringsSourcesResolver(GameRelease.Starfield, Data, fileSystem: fs);
        Tables(resolver).Length.ShouldBe(1);
        var second = Path.Combine(Data, "MyMod - Voices_en.ba2");
        WriteArchive(fs, second, Written, "strings/mymod_en.ilstrings");
        Tables(resolver).ShouldBe(new (StringsSource, Language, string?)[]
        {
            (StringsSource.Normal, Language.English, Main),
            (StringsSource.IL, Language.English, second),
        });
        fs.File.Delete(Main);
        Tables(resolver).ShouldBe(new (StringsSource, Language, string?)[] { (StringsSource.IL, Language.English, second) });
    }

    [Fact]
    public void Refresh_StringsFolderFilesChanged()
    {
        var fs = FileSystem();
        WriteArchive(fs, Main, Written, "strings/mymod_en.strings");
        var resolver = new StringsSourcesResolver(GameRelease.Starfield, Data, fileSystem: fs);
        Tables(resolver).Single().Archive.ShouldBe(Main);
        // A loose table wins over the archive's
        var loose = Path.Combine(Data, "Strings", "MyMod_en.strings");
        fs.AddFile(loose, new MockFileData(new byte[8]));
        Tables(resolver).Single().Archive.ShouldBeNull();
        resolver.ResolveSources(MyMod).Single().SourcePath.ShouldBe(loose);
        fs.File.Delete(loose);
        Tables(resolver).Single().Archive.ShouldBe(Main);
    }

    [Fact]
    public void StaticCalls_GiveTheResolversAnswers()
    {
        var fs = FileSystem();
        WriteArchive(fs, Main, Written, "strings/mymod_en.strings", "strings/mymod_fr.strings", "strings/other_en.strings");
        var resolver = new StringsSourcesResolver(GameRelease.Starfield, Data, fileSystem: fs);
        StringsFolderLookupOverlay.ResolveSources(GameRelease.Starfield, MyMod, Data, fileSystem: fs)
            .ShouldBe(resolver.ResolveSources(MyMod));
        StringsFolderLookupOverlay.ResolveSources(GameRelease.Starfield, new[] { MyMod }, Data, fileSystem: fs)[MyMod]
            .ShouldBe(resolver.ResolveSources(new[] { MyMod })[MyMod]);
        resolver.ResolveSources(MyMod).Count.ShouldBe(2);
    }
}
