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

// Surface tree pattern swap items (SFTR DAT2) are a Surface Pattern link and one byte, plus 4 more bytes on every item
// but the last (xEdit). Mutagen read 5-byte items, so with 2 items the second "link" was the first item's 4 bytes
// (a fake link that FormID remapping would change) and the real second pattern sat in Unknown.
public class SurfaceTreePatternSwapTests
{
    private const string ComponentName = "SurfaceTreePatternSwapInfo_Component";

    // DAT2 of SFTR 299071: PatternMountainsTerracedFieldsFlat02 (SFPT 23DEC4), A8, 10 65 00 00;
    // PatternCanyonsSharpLargeFieldsWarped01 (SFPT 20BC2E), 88
    private const string TwoItems = "02-00-00-00-C4-DE-23-00-A8-10-65-00-00-2E-BC-20-00-88";
    // DAT2 of SFTR 0478AD: one item, no extra 4 bytes (30 of the 32 SFTR look like this)
    private const string OneItem = "01-00-00-00-0C-E9-0E-00-C8";

    private static byte[] Tree(string dat2)
    {
        var bytes = Record("SFTR",
            Subrecord("BFCB", Encoding.ASCII.GetBytes(ComponentName + "\0")),
            Subrecord("DAT2", dat2),
            Subrecord("BFCE", Array.Empty<byte>()));
        BitConverter.GetBytes((ushort)581).CopyTo(bytes, 20); // Starfield.esm's SFTR form version
        return bytes;
    }

    private static IEnumerable<ISurfaceTreeGetter> Read(string dat2) =>
        ReadBoth<ISurfaceTreeGetter>(Tree(dat2), f => SurfaceTree.CreateFromBinary(f), (s, p) => SurfaceTreeBinaryOverlay.SurfaceTreeFactory(s, p));

    [Fact]
    public void TwoItems_Read()
    {
        foreach (var tree in Read(TwoItems))
        {
            var data = tree.Components.OfType<ISurfaceTreePatternSwapInfoComponentGetter>().Single().Data!;
            data.Data.Count.ShouldBe(2);
            data.Data[0].SurfacePattern.FormKey.ID.ShouldBe(0x23DEC4u);
            data.Data[0].Unknown.ShouldBe(unchecked((sbyte)0xA8));
            data.Data[0].Unknown2.ShouldBe(0x6510u);
            data.Data[1].SurfacePattern.FormKey.ID.ShouldBe(0x20BC2Eu);
            data.Data[1].Unknown.ShouldBe(unchecked((sbyte)0x88));
            data.Data[1].Unknown2.ShouldBeNull();
            data.Unknown.Length.ShouldBe(0);
            var links = tree.EnumerateFormLinks().Select(l => l.FormKey.ID).ToArray();
            links.ShouldContain(0x23DEC4u);
            links.ShouldContain(0x20BC2Eu);
            links.ShouldNotContain(0x6510u); // the old fake link
        }
    }

    [Theory]
    [InlineData(TwoItems)]
    [InlineData("ONE_ITEM")]
    public void WritesSameBytes(string dat2)
    {
        if (dat2 == "ONE_ITEM") dat2 = OneItem;
        foreach (var tree in Read(dat2))
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => tree.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("DAT2")).Content.ToArray().ShouldBe(Hex(dat2));
        }
    }
}
