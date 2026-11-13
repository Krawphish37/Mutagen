using System.Text;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Fallout4;

// C154: a lock (XLOC) can end after its flags (12 bytes, no trailing Int32). Both readers have to read it and the
// writer has to give back 12 bytes, not 16. A full 16-byte lock still writes 16.
public class LockDataBreakTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private static readonly MasterReferenceCollection Masters = new(TestModKey);

    private static ParsingMeta Meta() => new(GameConstants.Fallout4, TestModKey, SeparatedMasterPackage.NotSeparate(Masters));

    // Level 100 (Master), no key, flags 0x01; the long form adds an Int32 of 0
    private const string ShortLock = "640000000000000001000000";
    private const string FullLock = "640000000000000001000000" + "00000000";

    private static byte[] Record(string xloc)
    {
        var content = Convert.FromHexString(xloc);
        var sub = new MemoryStream();
        sub.Write(Encoding.ASCII.GetBytes("XLOC"));
        sub.Write(BitConverter.GetBytes((ushort)content.Length));
        sub.Write(content);
        var body = sub.ToArray();
        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("REFR"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)131));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static byte[] WrittenXloc(IPlacedObjectGetter placed)
    {
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(stream, new WritingBundle(GameConstants.Fallout4)
               {
                   MasterReferences = Masters,
                   SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(Masters),
               }, dispose: false))
        {
            placed.WriteToBinary(writer);
        }
        return GameConstants.Fallout4.MajorRecord(stream.ToArray()).FindSubrecord(new RecordType("XLOC")).Content.ToArray();
    }

    [Theory]
    [InlineData(ShortLock)]
    [InlineData(FullLock)]
    public void Lock_BothReaders_WriteSameBytes(string xloc)
    {
        var meta = Meta();
        var all = new IPlacedObjectGetter[]
        {
            PlacedObject.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(Record(xloc), Meta()))),
            PlacedObjectBinaryOverlay.PlacedObjectFactory(new OverlayStream(Record(xloc), meta), new BinaryOverlayFactoryPackage(meta)),
        };
        foreach (var placed in all)
        {
            placed.Lock!.Level.ShouldBe(LockLevel.Master);
            placed.Lock!.Flags.ShouldBe(LockData.Flag.LeveledLock);
            placed.Lock!.Unused.ShouldBe(0);
            placed.Lock!.Versioning.HasFlag(LockData.VersioningBreaks.Break0).ShouldBe(xloc == ShortLock);
            WrittenXloc(placed).ShouldBe(Convert.FromHexString(xloc));
        }
    }
}
