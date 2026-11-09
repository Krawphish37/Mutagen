using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Noggog;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Surface block fields get xEdit's names and types:
// - HNAM Manual Alignment Value is signed (-10 read as 65526 on 4 blocks; 17 of 1,753 are negative);
// - INAM/JNAM/KNAM are the alignment mode, grid alignment and grid sampling enums, GNAM the rarity enum;
// - FNAM Connections is a grid of 16-bit values (Size X * Size Y of them on all 1,730 blocks with FNAM), was raw;
// - NAM2/NAM3 Cell Min/Max are X/Y Int32 pairs (NAM2 was one Int64);
// - ANAM Data File, DNAM Size X/Y, ENAM Height and NAM4 Height Range (Min/Max floats since C33), WHGT Water Height,
//   NAM0 Source File, NAM5 Source Block.
public class SurfaceBlockFieldTests
{
    // OverlayBlockLC136World (21AA74:Starfield.esm), whole record: Manual Alignment -68, Absolute, Min, Full
    private const string LC136 = "4544494417004F7665726C6179426C6F636B4C43313336576F726C6400424643422100424753426C6F636B456469746F724D657461446174615F436F6D706F6E656E7400444154327900180000004D6F6E204F63742031322032303A32303A3433203230323001180000004D6F6E204F63742031322032303A32303A343320323032301300000048696C6C73526F636B794C61726765314B30320100000090CA0600000100000001000000FFFFFFFFFFFFFFFF00000000000000000000000000000000424643450000414E414D1C00446174615C5445525241494E5C4C43313336576F726C642E62746400444E414D08000800000008000000454E414D08000000FAC300007A44464E414D8000DE58E15725574D58FB565356C7541E553A59405BB25C985C1C5B7357E955AD54E654F4587D5E735ED45C235A165930582D546C58FC608C5F585E19605C645A5ED054D458C761256191600264746F14679B5606603263FB658E651E690D71476D295975633666F06B4F6E6270FF6DC172D45A7364B76FA96F4573A96F7D6C9B6C474E414D010000484E414D0200BCFF494E414D0100014A4E414D0100014B4E414D010002574847540400FFFF7FFF4E414D310D004F7665726C6179426C6F636B004E414D32080000000000000000004E414D33080000000000000000004E414D34080000000000000000004E414D35040090CA0600";

    [Fact]
    public void WholeRecord_FieldsNamedAndTyped()
    {
        var bytes = Record("SFBK", Hex(LC136));
        BitConverter.GetBytes((ushort)581).CopyTo(bytes, 20); // Starfield.esm's SFBK form version (DAT2 needs 558+)
        foreach (var block in ReadBoth<ISurfaceBlockGetter>(bytes, f => SurfaceBlock.CreateFromBinary(f), (s, p) => SurfaceBlockBinaryOverlay.SurfaceBlockFactory(s, p)))
        {
            block.DataFile.ShouldBe(@"Data\TERRAIN\LC136World.btd");
            block.Size.X.ShouldBe(8u);
            block.Size.Y.ShouldBe(8u);
            block.Connections!.Count.ShouldBe(64);
            block.Connections[0].ShouldBe((ushort)0x58DE);
            block.Connections[63].ShouldBe((ushort)0x6C9B);
            block.Rarity.ShouldBe(SurfaceBlock.RarityEnum.Common);
            block.ManualAlignmentValue.ShouldBe((short)-68);
            block.ManualAlignmentMode.ShouldBe(SurfaceBlock.AlignmentMode.Absolute);
            block.GridAlignmentOverride.ShouldBe(SurfaceBlock.GridAlignment.Min);
            block.GridSamplingOverride.ShouldBe(SurfaceBlock.GridSampling.Full);
            block.WaterHeight.ShouldBe(float.MinValue);
            block.Height.Min.ShouldBe(-500f);
            block.Height.Max.ShouldBe(1000f);
            block.HeightRange!.Min.ShouldBe(0f);
            block.HeightRange!.Max.ShouldBe(0f);
            block.SourceFile.ShouldBeNull();
            block.CellMin.ShouldBe(new P2Int(0, 0));
            block.CellMax.ShouldBe(new P2Int(0, 0));
            block.SourceBlock.FormKey.ID.ShouldBe(0x06CA90u); // HillsRockyLarge1K02 (06CA90:Starfield.esm)
            var written = GameConstants.Starfield.MajorRecord(Write(w => block.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(LC136));
        }
    }

    // FieldsPoolsLarge1K03 (06CA37:Starfield.esm): Size 14 x 14, so 196 connection values; the first is 0x9586,
    // which a signed reading would make -27258. Cell Max 13/13, a Source File, Height -8.79 to 18.21, Height Range 0 to 800.
    private const string FieldsPoolsFnam = "86-95-E2-A6-2F-B1-30-A6-E7-93-6A-8A-62-8C-F1-9F-A3-AE-B7-BB-64-B6-97-B8-28-C1-B6-C6-9A-7B-6F-90-29-8F-BC-81-A3-60-03-51-23-54-BF-6B-C7-82-C9-85-7B-86-06-89-0E-A0-2E-BC-B7-7B-89-7F-71-7A-41-62-14-4D-88-31-7D-22-D5-54-4D-71-CD-64-AE-48-CB-61-42-70-B5-9F-7E-97-13-8D-A2-79-D6-49-2A-1D-6E-1B-C6-1B-EE-4E-87-7C-ED-40-CE-32-6E-42-54-61-2C-87-C8-B7-F9-9D-8F-7A-7F-4C-5E-14-5D-06-8C-02-3A-59-DC-04-83-04-62-0C-A3-6E-2F-4A-A9-69-71-BA-AA-90-88-71-F3-62-C4-18-02-03-9F-01-E5-29-0B-01-DA-01-87-3C-01-9C-10-3C-0B-68-22-AD-69-7C-10-55-80-33-8E-05-9F-01-4C-23-F3-3B-15-02-5C-11-D0-27-FE-23-B7-3B-54-7A-BB-9A-C0-60-3B-40-27-33-22-1F-81-01-46-01-7C-14-EC-32-B8-49-19-3A-79-2A-BE-44-CC-6F-24-89-90-48-A1-2B-FB-39-B2-27-4F-26-28-01-6E-02-99-06-AE-48-23-3B-24-49-8A-74-16-90-CE-72-C7-42-2F-23-B9-09-3C-27-3D-1C-BD-28-48-1D-2B-04-C9-05-6D-0D-D9-33-21-78-F1-9F-77-75-0A-5A-26-3E-FA-44-57-24-F6-25-18-13-C6-0D-70-05-E7-05-C0-12-36-53-E7-6C-0D-92-A4-95-84-79-D8-65-90-C8-88-8A-1B-3D-22-46-02-43-8B-1B-2B-12-E1-4A-A2-52-E8-53-B4-6A-2F-B1-13-A6-CB-88-A9-82-5A-5C-A2-52-52-5E-68-6E-F8-4F-16-50-09-4C-29-36-9F-41-E2-58-0E-A0-0A-A8-92-A4-B8-89-5C-78-83-84-E4-9B-A5-A3-36-93-75-72-D7-57-4F-4D-71-53-41-62";
    private const string FieldsPoolsEnam = "98-B5-0C-C1-04-AA-91-41";
    private const string FieldsPoolsNam0 = "2E-2E-5C-53-6F-75-72-63-65-5C-54-65-72-72-61-69-6E-5C-46-69-65-6C-64-73-50-6F-6F-6C-73-4C-61-72-67-65-31-4B-5C-46-69-65-6C-64-73-50-6F-6F-6C-73-4C-61-72-67-65-31-4B-30-33-2E-74-69-66-00";

    [Fact]
    public void Connections_CellMax_SourceFile()
    {
        var bytes = Record("SFBK",
            Subrecord("DNAM", "0E-00-00-00-0E-00-00-00"),
            Subrecord("ENAM", FieldsPoolsEnam),
            Subrecord("FNAM", FieldsPoolsFnam),
            Subrecord("NAM0", FieldsPoolsNam0),
            Subrecord("NAM3", "0D-00-00-00-0D-00-00-00"),
            Subrecord("NAM4", "00-00-00-00-00-00-48-44"));
        foreach (var block in ReadBoth<ISurfaceBlockGetter>(bytes, f => SurfaceBlock.CreateFromBinary(f), (s, p) => SurfaceBlockBinaryOverlay.SurfaceBlockFactory(s, p)))
        {
            block.Size.X.ShouldBe(14u);
            block.Connections!.Count.ShouldBe(14 * 14);
            block.Connections[0].ShouldBe((ushort)0x9586);
            block.Connections[195].ShouldBe((ushort)0x6241);
            block.SourceFile.ShouldBe(@"..\Source\Terrain\FieldsPoolsLarge1K\FieldsPoolsLarge1K03.tif");
            block.CellMax.ShouldBe(new P2Int(13, 13));
            block.Height.Min.ShouldBe(BitConverter.ToSingle(Hex(FieldsPoolsEnam), 0));
            block.Height.Min.ShouldBeInRange(-8.8f, -8.7f);
            block.Height.Max.ShouldBe(BitConverter.ToSingle(Hex(FieldsPoolsEnam), 4));
            block.Height.Max.ShouldBeInRange(18.2f, 18.21f);
            block.HeightRange!.Min.ShouldBe(0f);
            block.HeightRange!.Max.ShouldBe(800f);
            var written = GameConstants.Starfield.MajorRecord(Write(w => block.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("FNAM")).Content.ToArray().ShouldBe(Hex(FieldsPoolsFnam));
            written.FindSubrecord(new RecordType("NAM3")).Content.ToArray().ShouldBe(Hex("0D-00-00-00-0D-00-00-00"));
            written.FindSubrecord(new RecordType("ENAM")).Content.ToArray().ShouldBe(Hex(FieldsPoolsEnam));
            written.FindSubrecord(new RecordType("NAM4")).Content.ToArray().ShouldBe(Hex("00-00-00-00-00-00-48-44"));
        }
    }
}
