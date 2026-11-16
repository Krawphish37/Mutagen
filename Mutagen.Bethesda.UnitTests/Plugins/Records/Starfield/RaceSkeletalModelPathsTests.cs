using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.HandReadLayoutFile;
using Noggog;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

/// <summary>
/// C58: a race's skeletal model (MNAM / FNAM markers, each followed by the model's ANAM, NAM5, NAM6) is read by the
/// generated dispatch of the MNAM and FNAM subrecords, which decides they are the skeletal model's. The full import's
/// custom reader then checked again that nothing at or past the skeletal model had been read (and, with nothing read
/// before, its null compare said no), so it skipped markers the overlay read: one logic now, the dispatch's.
/// </summary>
public class RaceSkeletalModelPathsTests
{
    private readonly ITestOutputHelper _output;

    public RaceSkeletalModelPathsTests(ITestOutputHelper output) => _output = output;

    private static byte[] Text(string s) => Encoding.ASCII.GetBytes(s + "\0");

    private static IEnumerable<IRaceGetter> Read(byte[] bytes) =>
        ReadBoth<IRaceGetter>(bytes, f => Race.CreateFromBinary(f), (s, p) => RaceBinaryOverlay.RaceFactory(s, p));

    private static void Check(byte[] bytes)
    {
        var read = Read(bytes).ToArray();
        foreach (var race in read)
        {
            race.SkeletalModel.ShouldNotBeNull();
            race.SkeletalModel!.Male!.Model!.File.GivenPath.ShouldBe("m.nif");
            race.SkeletalModel.Female!.Model!.File.GivenPath.ShouldBe("f.nif");
        }
        read[0].SkeletalModel!.Equals(read[1].SkeletalModel).ShouldBeTrue();
    }

    [Fact]
    public void MarkersBeforeAnyField()
    {
        // Nothing parsed before the MNAM (lastParsed has no index): the full import used to skip it
        Check(Record("RACE",
            Subrecord("MNAM", Array.Empty<byte>()), Subrecord("ANAM", Text("m.nif")),
            Subrecord("FNAM", Array.Empty<byte>()), Subrecord("ANAM", Text("f.nif"))));
    }

    [Fact]
    public void MarkersInPlace()
    {
        Check(Record("RACE",
            Subrecord("EDID", Text("TestRace")),
            Subrecord("MNAM", Array.Empty<byte>()), Subrecord("ANAM", Text("m.nif")),
            Subrecord("FNAM", Array.Empty<byte>()), Subrecord("ANAM", Text("f.nif"))));
    }

    [Fact]
    public void MarkersAfterTheLaterFields()
    {
        // The first gendered markers of the record after a field past DialogueQuest (SNAM, the plural name): the dispatch
        // reads them as the skeletal model (its first gendered group); the full import used to skip them
        Check(Record("RACE",
            Subrecord("EDID", Text("TestRace")),
            Subrecord("SNAM", Text("Testers")),
            Subrecord("MNAM", Array.Empty<byte>()), Subrecord("ANAM", Text("m.nif")),
            Subrecord("FNAM", Array.Empty<byte>()), Subrecord("ANAM", Text("f.nif"))));
    }

    /// <summary>
    /// Every RACE of Starfield.esm and the official plugins reads the same in both readers: the skeletal model, and the
    /// whole record
    /// </summary>
    [Fact]
    [Trait("Category", "RealData")]
    public void RealData_OfficialPlugins_EveryRaceTheSameInBothReaders()
    {
        var data = Environment.GetEnvironmentVariable("MUTAGEN_STARFIELD_DATA");
        if (string.IsNullOrEmpty(data)) return; // needs the game's Data folder
        var styles = new Cache<IModMasterStyledGetter, ModKey>(x => x.ModKey);
        foreach (var name in HandReadLayoutFileTests.OfficialPlugins)
        {
            styles.Set(new KeyedMasterStyle(ModKey.FromFileName(name), ModHeaderFrame.FromPath(Path.Combine(data, name), GameRelease.Starfield).MasterStyle));
        }
        var param = new BinaryReadParameters { MasterFlagsLookup = styles };
        int races = 0, withSkeleton = 0;
        foreach (var name in HandReadLayoutFileTests.OfficialPlugins)
        {
            var path = new ModPath(ModKey.FromFileName(name), Path.Combine(data, name));
            using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, param);
            var full = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, param, importMask: new GroupMask(false) { Races = true });
            var fullRaces = full.Races.ToDictionary(r => r.FormKey);
            overlay.Races.Count.ShouldBe(fullRaces.Count, name);
            foreach (var race in overlay.Races)
            {
                races++;
                var fromFull = fullRaces[race.FormKey];
                if (race.SkeletalModel != null) withSkeleton++;
                (race.SkeletalModel?.Equals(fromFull.SkeletalModel) ?? fromFull.SkeletalModel == null).ShouldBeTrue($"{name} {race.FormKey} {race.EditorID}");
                race.Equals(fromFull).ShouldBeTrue($"{name} {race.FormKey} {race.EditorID}");
            }
        }
        _output.WriteLine($"{races} races, {withSkeleton} with a skeletal model");
        races.ShouldBeGreaterThan(0);
    }
}
