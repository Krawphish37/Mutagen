using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

// A counted list (counterRecType) whose items are Loqui objects without their own record type, stored together in
// the list's one subrecord (e.g. Starfield XWCN count + XWCU items). The full reader's count overload skipped the item
// subrecord header only for non-Loqui items, so the first item read the header bytes. The generator now passes
// skipHeader: true for such lists. Overlay and writer were already right.
public class CountedListInOneSubrecordTests
{
    // XWCN = 2, then XWCU with two 8-byte items (SurfaceBlockIntItem: two UInt32s First/Second, no record type of its own)
    private static readonly byte[] Bytes = Subrecord("XWCN", "02-00-00-00")
        .Concat(Subrecord("XWCU", "01-00-00-00-02-00-00-00-03-00-00-00-04-00-00-00"))
        .Concat(Subrecord("EDID", "00"))
        .ToArray();

    private static bool Item(MutagenFrame f, out SurfaceBlockIntItem item, TypedParseParams p)
    {
        item = SurfaceBlockIntItem.CreateFromBinary(f);
        return true;
    }

    private static MutagenFrame Frame() => new(new MutagenMemoryReadStream(Bytes, Meta()));

    [Fact]
    public void SkipHeader_ReadsItemsAfterTheHeader()
    {
        var frame = Frame();
        var list = ListBinaryTranslation<SurfaceBlockIntItem>.Instance.Parse(
            reader: frame,
            countLengthLength: 4,
            countRecord: new RecordType("XWCN"),
            triggeringRecord: new RecordType("XWCU"),
            transl: Item,
            skipHeader: true);
        list.Select(x => (x.First, x.Second)).ShouldBe(new[] { (1u, 2u), (3u, 4u) });
        // Positioned at the next subrecord
        frame.Position.ShouldBe(10 + 6 + 16);
    }

    [Fact]
    public void WithoutSkipHeader_FirstItemReadsTheHeader()
    {
        // The old behavior, kept for lists whose Loqui items read their own subrecord header
        var list = ListBinaryTranslation<SurfaceBlockIntItem>.Instance.Parse(
            reader: Frame(),
            countLengthLength: 4,
            countRecord: new RecordType("XWCN"),
            triggeringRecord: new RecordType("XWCU"),
            transl: Item);
        list[0].First.ShouldBe(BitConverter.ToUInt32("XWCU"u8));
    }

    [Fact]
    public void SkipHeader_EmptyListConsumesItsEmptySubrecord()
    {
        var bytes = Subrecord("XWCN", "00-00-00-00").Concat(Subrecord("XWCU", "")).Concat(Subrecord("EDID", "00")).ToArray();
        var frame = new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta()));
        var list = ListBinaryTranslation<SurfaceBlockIntItem>.Instance.Parse(
            reader: frame,
            countLengthLength: 4,
            countRecord: new RecordType("XWCN"),
            triggeringRecord: new RecordType("XWCU"),
            transl: Item,
            skipHeader: true);
        list.ShouldBeEmpty();
        frame.Position.ShouldBe(10 + 6);
    }
}
