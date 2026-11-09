using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A location's DATA Encounter Min/Max Level are unsigned bytes (xEdit's Starfield definitions: itU8); Faction Rank
// stays signed (itS8). Starfield.esm's levels are 0 to 100 and 255 (Max 255 on 135 locations, Min 255 on 2), which
// Int8 read as -1.
public class LocationEncounterLevelTests
{
    [Theory]
    // CityAkilaCityLocation (010DFB:Starfield.esm): Min 1, Max 255
    [InlineData("00-00-00-00-00-01-00-FF", 0, 1, 0, 255)]
    // UC04_Terrormorph_CustomLoc (04AEC8:Starfield.esm): Min 255, Match PC Level, Max 255
    [InlineData("00-00-00-00-00-FF-02-FF", 0, 255, 2, 255)]
    // ShipVendorLocation (209AC7:Starfield.esm): Max 100
    [InlineData("00-00-00-00-00-00-00-64", 0, 0, 0, 100)]
    // OEAF026Location (00119B:Starfield.esm): Faction Rank -1, flag 0x10
    [InlineData("00-00-00-00-FF-00-10-00", -1, 0, 0x10, 0)]
    public void EncounterLevels_AreUnsigned(string hex, int rank, int min, int flags, int max)
    {
        var bytes = Record("LCTN", Subrecord("DATA", hex));
        foreach (var loc in ReadBoth<ILocationGetter>(bytes, f => Location.CreateFromBinary(f), (s, p) => LocationBinaryOverlay.LocationFactory(s, p)))
        {
            var data = loc.Owner!;
            data.FactionRank.ShouldBe((sbyte)rank);
            data.EncounterMinLevel.ShouldBe((byte)min);
            ((int)data.EncounterFlags).ShouldBe(flags);
            data.EncounterMaxLevel.ShouldBe((byte)max);
            var written = GameConstants.Starfield.MajorRecord(Write(w => loc.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("DATA")).Content.ToArray().ShouldBe(Hex(hex));
        }
    }
}
