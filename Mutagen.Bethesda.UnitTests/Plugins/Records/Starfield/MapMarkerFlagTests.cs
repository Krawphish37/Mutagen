using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield map marker flags (FNAM after XMRK) go beyond Fallout 4's four: Visible on Star Map 0x10,
// Discovered 0x20, Requires Scanning 0x40, Allow Grav Jump 0x80, Suppress Discovery 0x200, Use Accurate Z-Height
// 0x400, Limit Map Visibility To Compass Range 0x800, Must Cruise to First 0x1000 (xEdit's Starfield definitions).
// Many of the 517 map markers in Starfield.esm use them (value 27, with bit 4, on 106).
public class MapMarkerFlagTests
{
    [Fact]
    public void StarMapAndSuppressDiscovery_Read()
    {
        // REFR 1C6D19 in Starfield.esm: map flags 0x213
        var bytes = Record("REFR", Subrecord("XMRK", ""), Subrecord("FNAM", "13-02-00-00"));
        foreach (var placed in ReadBoth<IPlacedObjectGetter>(bytes, f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            placed.MapMarker!.Flags.ShouldBe(PlacedObjectMapMarker.Flag.Visible | PlacedObjectMapMarker.Flag.CanTravelTo
                | PlacedObjectMapMarker.Flag.VisibleOnStarMap | PlacedObjectMapMarker.Flag.SuppressDiscovery);
        }
    }

    [Theory]
    [InlineData(PlacedObjectMapMarker.Flag.VisibleOnStarMap, 0x10u)]
    [InlineData(PlacedObjectMapMarker.Flag.AllowGravJump, 0x80u)]
    [InlineData(PlacedObjectMapMarker.Flag.MustCruiseToFirst, 0x1000u)]
    public void Flag_Writes(PlacedObjectMapMarker.Flag flag, uint expected)
    {
        var placed = new PlacedObject(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            MapMarker = new PlacedObjectMapMarker { Flags = flag },
        };
        var written = GameConstants.Starfield.MajorRecord(Write(w => placed.WriteToBinary(w)));
        BitConverter.ToUInt32(written.FindSubrecord(new RecordType("FNAM")).Content.Slice(0, 4)).ShouldBe(expected);
    }
}
