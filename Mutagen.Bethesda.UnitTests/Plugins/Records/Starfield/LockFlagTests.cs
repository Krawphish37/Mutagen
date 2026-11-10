using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Lock flags: Mutagen had LeveledLock on bit 0; xEdit has "Unknown 0" there and Leveled Lock on bit 2. In Starfield.esm
// bit 0 is on 1,436 of 1,958 locks, 493 of them Inaccessible, Requires Key or Requires Terminal, which can't be
// leveled; bit 2 is on 24. Bit 0 becomes Unknown0 and LeveledLock moves to 0x04.
public class LockFlagTests
{
    private static IEnumerable<IPlacedObjectGetter> Read(string xloc)
        => ReadBoth<IPlacedObjectGetter>(Record("REFR", Subrecord("XLOC", xloc)), f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p));

    [Theory]
    // REFR 26FECA:Starfield.esm: level 75, key LC012Scow_ZooPassword (0F55E0:Starfield.esm), flags 05
    [InlineData("4B-00-00-00-E0-55-0F-00-05-00-00-00-00-00-00-00", LockLevel.Expert, LockData.Flag.Unknown0 | LockData.Flag.LeveledLock)]
    // REFR 150B03:Starfield.esm: level None, key LC102EngineeringKey (150A8C:Starfield.esm), flags 04
    [InlineData("00-00-00-00-8C-0A-15-00-04-00-00-00-07-00-00-00", LockLevel.None, LockData.Flag.LeveledLock)]
    // REFR 10C9CE:Starfield.esm: Inaccessible, flags 01
    [InlineData("FE-FD-5E-00-00-00-00-00-01-00-00-00-00-00-00-00", LockLevel.Inaccessible, LockData.Flag.Unknown0)]
    public void Flags(string xloc, LockLevel level, LockData.Flag flags)
    {
        foreach (var placed in Read(xloc))
        {
            placed.Lock!.Level.ShouldBe(level);
            placed.Lock.Flags.ShouldBe(flags);
            var written = GameConstants.Starfield.MajorRecord(Write(w => placed.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("XLOC")).Content.ToArray().ShouldBe(Hex(xloc));
        }
    }
}
