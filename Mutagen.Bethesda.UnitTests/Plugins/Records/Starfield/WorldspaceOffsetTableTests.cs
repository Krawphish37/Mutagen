using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Worldspace OFST and CLSZ were raw bytes. xEdit: Offsets and Cell Sizes, each a grid of u32 (a row per Y, a column
// per X of the NAM0/NAM9 bounds). In Starfield.esm both are on 432 of the 433 worldspaces, always the same length,
// always 4 x rows x columns, and zero in the same places. Each OFST value is the offset of the exterior CELL record
// at that grid position from the start of the WRLD record (checked on all 18,300 exterior cells). They become lists
// of UInt32, kept as read.
public class WorldspaceOffsetTableTests
{
    // OverlayTraitCharredEcosystemMd02Water (2C7419:Starfield.esm): bounds -4,-3 to 4,4 (9 columns, 8 rows), 52 cells
    private const string CharredWater = "4544494426004F7665726C617954726169744368617272656445636F73797374656D4D643032576174657200424643421F00424753576F726C6453706163654F7665726C61795F436F6D706F6E656E7400534E414D040000000000504E414D040000000000424E414D04004EB02C00424643450000424643423300424753506C616E6574436F6E74656E744D616E61676572436F6E74656E7450726F706572746965735F436F6D706F6E656E74005A4E414D040000000000594E414D010000584E414D040000000000574E414D040000000000564E414D010001554E414D0400000000004E414D310400000000004E414D320400000000004E414D330400000000004E414D34040000FF00004E414D350400000000004E414D360400000000004E414D370400000000004E414D380100014E414D3904000000000043495443040001000000435444412000000000000000803F4503F94446F22C00000000000000000000000000FFFFFFFF42464345000046554C4C040087390300584C434E0400841C0000574E414D04003C000000504E414D020000004E414D320400E91700004E414D330400180000004E414D34040000000000444E414D0800000000C5000048C34D4E414D1000000000000000000000000000000000004F4E414D10000000803F0000000000000000000000004E414D4104000000803F44415441010001464E414D0100014E414D3008000000C8C3000096C34E414D3908000000C8430000C843474E414D04000000803F58434C570000574847540000484E414D0100054F46535420010000000016BF01001EBE010068BF01002DCA000075D100007FCA0000C7D1000000000000A5A4020010C0010070BE0100BCBF010029CB00006BD20000D4CA000019D2000000000000F7A4020062C00100C2BE0100B6C001007BCB0000BDD2000023D1000011D30000000000000000000002BC01004ABD010086D50000ED130000591100009C130000071100000000000000000000E0D40000F8BC010032D50000F61200005E10000049130000B2100000000000009CBD01008ED40000A6BC01003BD40000A31200000A10000051120000B60F0000000000000000000095D3000054BC0100E7D30000AB1100000D0F0000FE110000620F000000000000000000000000000000000000000000000000000000000000000000000000000000000000434C535A200100000000520000005200000054000000520000005200000055000000520000000000000052000000520000005200000054000000520000005200000055000000520000000000000053000000540000005400000007E40000C00500005400000052000000540000000000000000000000520000005200000094E6000028B6000052000000510000005200000000000000000000005200000052000000540000005300000054000000530000005500000000000000520000005200000052000000530000005300000054000000520000005400000000000000000000005200000052000000540000005300000055000000530000005400000000000000000000000000000000000000000000000000000000000000000000000000000000000000";

    private static IEnumerable<IWorldspaceGetter> Read(byte[] bytes)
        => ReadBoth<IWorldspaceGetter>(bytes, f => Worldspace.CreateFromBinary(f), (s, p) => WorldspaceBinaryOverlay.WorldspaceFactory(s, p));

    [Fact]
    public void OffsetsAndCellSizes()
    {
        foreach (var world in Read(Record("WRLD", Hex(CharredWater))))
        {
            world.OffsetData.ShouldNotBeNull();
            world.CellSizeData.ShouldNotBeNull();
            world.OffsetData.Count.ShouldBe(72);
            world.CellSizeData.Count.ShouldBe(72);
            world.OffsetData.Count(x => x != 0).ShouldBe(52);
            world.OffsetData[0].ShouldBe(0u);
            world.OffsetData[1].ShouldBe(0x1BF16u);
            world.CellSizeData[1].ShouldBe(0x52u);
            var written = GameConstants.Starfield.MajorRecord(Write(w => world.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(CharredWater));
        }
    }

    // Copied as an override into another plugin, a worldspace loses both tables, as in xEdit: they only fit the file
    // they were read from. Everything else is copied as read.
    [Fact]
    public void OverrideCopy_DropsOffsetsAndCellSizes()
    {
        var original = GameConstants.Starfield.MajorRecord(Record("WRLD", Hex(CharredWater)));
        var expected = original.EnumerateSubrecords()
            .Where(x => x.RecordType != new RecordType("OFST") && x.RecordType != new RecordType("CLSZ"))
            .SelectMany(x => x.HeaderAndContentData.ToArray())
            .ToArray();
        foreach (var world in Read(Record("WRLD", Hex(CharredWater))))
        {
            var mod = new StarfieldMod(ModKey.FromFileName("OverrideTest.esp"), StarfieldRelease.Starfield);
            var copy = mod.Worldspaces.GetOrAddAsOverride(world);
            copy.OffsetData.ShouldBeNull();
            copy.CellSizeData.ShouldBeNull();
            var written = GameConstants.Starfield.MajorRecord(Write(w => copy.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(expected);
        }
    }

    // More than 65,535 bytes goes in an XXXX subrecord first (none in Starfield.esm, but a large worldspace needs it)
    [Fact]
    public void Overflow()
    {
        var world = new Worldspace(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            OffsetData = new(Enumerable.Range(0, 20000).Select(i => (uint)i)),
            CellSizeData = new(Enumerable.Range(0, 20000).Select(i => (uint)(i * 3))),
        };
        var bytes = Write(w => world.WriteToBinary(w));
        var hex = Convert.ToHexString(bytes);
        hex.Split("58585858040080380100").Length.ShouldBe(3); // XXXX, length 4, 80,000 bytes, twice
        foreach (var read in Read(bytes))
        {
            read.OffsetData!.Count.ShouldBe(20000);
            read.OffsetData[19999].ShouldBe(19999u);
            read.CellSizeData!.Count.ShouldBe(20000);
            read.CellSizeData[19999].ShouldBe(59997u);
        }
    }
}
