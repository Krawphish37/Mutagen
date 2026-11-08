using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield lock data (XLOC): Level is one byte + 3 unused, then Key, Flags one byte + 3 unused, then an Int32 that
// xEdit calls Unknown (it holds values: 7 on 279 locks, 8 on 160). 9 of 1,942 locks in Starfield.esm have filler in
// the bytes after Level (FD 5E 00), so read as 4 bytes Inaccessible (254) became 6225406 with no name.
public class LockDataTests
{
    // XLOC of REFR 10C9CE in Starfield.esm: level 254 (Inaccessible) + FD 5E 00, no key, flags 01
    private const string Lock = "FE-FD-5E-00-00-00-00-00-01-00-00-00-00-00-00-00";

    [Fact]
    public void Level_WithFiller_Reads()
    {
        var bytes = Record("REFR", Subrecord("XLOC", Lock));
        foreach (var placed in ReadBoth<IPlacedObjectGetter>(bytes, f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            placed.Lock!.Level.ShouldBe(LockLevel.Inaccessible);
            placed.Lock!.LevelUnused.ToArray().ShouldBe(new byte[] { 0xFD, 0x5E, 0x00 });
            ((int)placed.Lock!.Flags).ShouldBe(1);
            placed.Lock!.Unknown.ShouldBe(0);
        }
    }

    [Fact]
    public void Lock_RoundTrips()
    {
        var placed = PlacedObject.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(Record("REFR", Subrecord("XLOC", Lock)), Meta())));
        var written = GameConstants.Starfield.MajorRecord(Write(w => placed.WriteToBinary(w)));
        written.FindSubrecord(new RecordType("XLOC")).Content.ToArray().ShouldBe(Hex(Lock));
    }
}
