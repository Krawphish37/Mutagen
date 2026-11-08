using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// The block editor meta data component (SFBK DAT2), per xEdit: Created Time, Source Type (1 byte), Also Created Time,
// Source Editor ID, a counted list of 5-byte items (4 + 1), Location X, Location Y, Planet System ID, Satellite ID,
// then Longitude and Latitude as doubles (kept raw for now, see C59). Mutagen read the items as Int32s, so every field after them was shifted by
// the item count (Location X = 1 read as 256 on 410 of 471 SFBK). In Starfield.esm 470 blocks end with the 2 doubles
// and one (2990F9, an old record) has only 8 bytes after the 4 Int32s.
public class BlockEditorMetaDataTests
{
    private const string ComponentName = "BGSBlockEditorMetaData_Component";

    // DAT2 of SFBK 002FA1 OverlayHillsRockySmall03: 1 item (50 85 29 00, 00), Location 1/1, Planet System -1, Satellite -1
    private const string OneItem =
        "18-00-00-00-54-68-75-20-4A-75-6C-20-32-38-20-31-30-3A-34-36-3A-35-30-20-32-30-32-32-01-18-00-00-00-54-68-75-20-4A-75-6C-20-32-38-20-31-30-3A-34-36-3A-35-30-20-32-30-32-32-18-00-00-00-4F-76-65-72-6C-61-79-48-69-6C-6C-73-52-6F-63-6B-79-53-6D-61-6C-6C-30-33-01-00-00-00-50-85-29-00-00-01-00-00-00-01-00-00-00-FF-FF-FF-FF-FF-FF-FF-FF-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00";
    // DAT2 of SFBK 0DF7B1: 2 items, Location 8/7
    private const string TwoItems =
        "18-00-00-00-4D-6F-6E-20-53-65-70-20-31-33-20-31-35-3A-34-39-3A-35-37-20-32-30-32-31-01-18-00-00-00-4D-6F-6E-20-53-65-70-20-31-33-20-31-35-3A-34-39-3A-35-37-20-32-30-32-31-1B-00-00-00-4D-6F-75-6E-74-61-69-6E-73-54-65-72-72-61-63-65-64-4D-65-64-69-75-6D-31-4B-30-36-02-00-00-00-29-38-2E-00-00-29-38-2E-00-00-08-00-00-00-07-00-00-00-FF-FF-FF-FF-FF-FF-FF-FF-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00";
    // DAT2 of SFBK 2990F9: 4 items, Location 7/7, and only 8 bytes after the Int32s
    private const string ShortEnding =
        "18-00-00-00-57-65-64-20-4D-61-79-20-31-33-20-31-31-3A-34-36-3A-35-38-20-32-30-32-30-01-18-00-00-00-57-65-64-20-4D-61-79-20-31-33-20-31-31-3A-34-36-3A-35-38-20-32-30-32-30-20-00-00-00-46-69-65-6C-64-73-53-6C-6F-74-43-61-6E-79-6F-6E-73-53-68-61-72-70-4D-65-64-69-75-6D-31-4B-30-35-04-00-00-00-83-F5-2F-00-00-83-F5-2F-00-00-83-F5-2F-00-00-83-F5-2F-00-00-07-00-00-00-07-00-00-00-FF-FF-FF-FF-FF-FF-FF-FF-00-00-00-00-00-00-00-00";

    private static byte[] Block(string dat2)
    {
        var bytes = Record("SFBK",
            Subrecord("BFCB", Encoding.ASCII.GetBytes(ComponentName + "\0")),
            Subrecord("DAT2", dat2),
            Subrecord("BFCE", Array.Empty<byte>()));
        BitConverter.GetBytes((ushort)581).CopyTo(bytes, 20); // Starfield.esm's SFBK form version
        return bytes;
    }

    private static IEnumerable<IBlockEditorMetaDataComponentGetter> Read(string dat2) =>
        ReadBoth<ISurfaceBlockGetter>(Block(dat2), f => SurfaceBlock.CreateFromBinary(f), (s, p) => SurfaceBlockBinaryOverlay.SurfaceBlockFactory(s, p))
            .Select(b => b.Components.OfType<IBlockEditorMetaDataComponentGetter>().Single());

    [Fact]
    public void OneItem_Reads()
    {
        foreach (var meta in Read(OneItem))
        {
            meta.CreatedTime.ShouldBe("Thu Jul 28 10:46:50 2022");
            meta.SourceType.ShouldBe(BlockEditorMetaDataComponent.SourceTypeOption.Block);
            meta.SourceEditorID.ShouldBe("OverlayHillsRockySmall03");
            meta.UnknownItems.Count.ShouldBe(1);
            meta.UnknownItems[0].Unknown.ShouldBe(0x00298550u);
            meta.UnknownItems[0].Unknown2.ShouldBe((byte)0);
            meta.LocationX.ShouldBe(1);
            meta.LocationY.ShouldBe(1);
            meta.PlanetSystemId.ShouldBe(-1);
            meta.SatelliteId.ShouldBe(-1);
            meta.LongitudeLatitude.Length.ShouldBe(16); // two doubles, both 0 here
        }
    }

    [Fact]
    public void TwoItems_Reads()
    {
        foreach (var meta in Read(TwoItems))
        {
            meta.UnknownItems.Select(i => i.Unknown).ShouldBe(new[] { 0x002E3829u, 0x002E3829u });
            meta.LocationX.ShouldBe(8);
            meta.LocationY.ShouldBe(7);
            meta.SourceEditorID.ShouldBe("MountainsTerracedMedium1K06");
        }
    }

    [Fact]
    public void ShortEnding_Reads()
    {
        foreach (var meta in Read(ShortEnding))
        {
            meta.UnknownItems.Count.ShouldBe(4);
            meta.LocationX.ShouldBe(7);
            meta.LocationY.ShouldBe(7);
            meta.LongitudeLatitude.Length.ShouldBe(8);
        }
    }

    [Theory]
    [InlineData(OneItem)]
    [InlineData(TwoItems)]
    [InlineData(ShortEnding)]
    public void WritesSameBytes(string dat2)
    {
        foreach (var block in ReadBoth<ISurfaceBlockGetter>(Block(dat2), f => SurfaceBlock.CreateFromBinary(f), (s, p) => SurfaceBlockBinaryOverlay.SurfaceBlockFactory(s, p)))
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => block.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("DAT2")).Content.ToArray().ShouldBe(Hex(dat2));
        }
    }
}
