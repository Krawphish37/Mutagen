using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A placed map marker's TNAM, VNAM and VISI were numbers named Unknown, and FNAM lacked 8 of xEdit's flag names.
// TNAM is a 2-byte type (Mutagen split it into Int8 Type and UInt8 Unknown; the second byte is 0 on all 517 markers).
// Values from the marker on REFR 2031E6:Starfield.esm (no Editor ID); FULL and UNAM are left out because
// Starfield.esm stores them as string IDs.
public class MapMarkerEnumTests
{
    [Fact]
    public void ReadsAndWrites()
    {
        var bytes = Record("REFR", Subrecord("XMRK", ""), Subrecord("FNAM", "13-00-00-00"), Subrecord("TNAM", "2F-00"), Subrecord("VNAM", "06-00"), Subrecord("VISI", "02"));
        foreach (var refr in ReadBoth<IPlacedObjectGetter>(bytes, f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            var marker = refr.MapMarker.ShouldNotBeNull();
            marker.Flags.ShouldBe(PlacedObjectMapMarker.Flag.Visible | PlacedObjectMapMarker.Flag.CanTravelTo | PlacedObjectMapMarker.Flag.VisibleOnStarMap);
            marker.Type.ShouldBe(PlacedObjectMapMarker.MarkerType.Ship);
            marker.Category.ShouldBe(PlacedObjectMapMarker.MarkerCategory.Ship);
            marker.UndiscoveredVisibility.ShouldBe(PlacedObjectMapMarker.Visibility.Planet);
            var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
            foreach (var (sig, hex) in new[] { ("FNAM", "13-00-00-00"), ("TNAM", "2F-00"), ("VNAM", "06-00"), ("VISI", "02") })
            {
                written.FindSubrecord(new RecordType(sig)).Content.ToArray().ShouldBe(Hex(hex), sig);
            }
        }
    }
}
