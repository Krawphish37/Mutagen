using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C125 (generator): a String field of its own subrecord with binaryType NullPaddedFixedLength read the 6-byte
// subrecord header into the string ("MOD2CityMaps\...") and wrote no header. The full import now skips the header and
// reads the content; the writer writes the header around the fixed-length content. (The overlay already read the
// subrecord's content.) No existing definition used this combination; CityMapsUsageComponent.ModelPath (MOD2, xEdit
// 'Model Path', 260 bytes zero-padded) is the first.
// A minimal record carrying the component is read with both readers and written back byte for byte.
public class FixedLengthStringSubrecordTests
{
    // MOD2 of CELL 0F4ED4:SFBGS008.esm: "CityMaps\SFBGS008\SFBGS008_CityMaps_NewAtlantisTheWell.nif", zero-padded to 260
    private const string CityMapPath = "436974794D6170735C53464247533030385C53464247533030385F436974794D6170735F4E657741746C616E74697354686557656C6C2E6E696600000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000";

    private static byte[] Gbfm(byte[] mod2) => Record("GBFM",
        Subrecord("EDID", Encoding.ASCII.GetBytes("CityMapTest\0")),
        Subrecord("BFCB", Encoding.ASCII.GetBytes("BGSCityMapsUsage_Component\0")),
        Subrecord("MOD2", mod2),
        Subrecord("BFCE", Array.Empty<byte>()));

    private static IEnumerable<IGenericBaseFormGetter> Read(byte[] bytes)
        => ReadBoth<IGenericBaseFormGetter>(bytes, f => GenericBaseForm.CreateFromBinary(f), (s, p) => GenericBaseFormBinaryOverlay.GenericBaseFormFactory(s, p));

    [Fact]
    public void RealPath_ReadAndWrittenBack()
    {
        var mod2 = Hex(CityMapPath);
        foreach (var gbfm in Read(Gbfm(mod2)))
        {
            gbfm.Components.OfType<ICityMapsUsageComponentGetter>().Single().ModelPath
                .ShouldBe(@"CityMaps\SFBGS008\SFBGS008_CityMaps_NewAtlantisTheWell.nif");
            var written = GameConstants.Starfield.MajorRecord(Write(w => ((IGenericBaseFormGetter)gbfm).WriteToBinary(w)));
            written.TryFindSubrecord(new RecordType("MOD2"))!.Value.Content.ToArray().ShouldBe(mod2);
        }
    }

    [Fact]
    public void NewValue_WrittenAsHeaderPlusPaddedContent()
    {
        var gbfm = new GenericBaseForm(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield);
        gbfm.Components.Add(new CityMapsUsageComponent { ModelPath = @"CityMaps\Test.nif" });
        var written = GameConstants.Starfield.MajorRecord(Write(w => ((IGenericBaseFormGetter)gbfm).WriteToBinary(w)));
        var mod2 = written.TryFindSubrecord(new RecordType("MOD2"))!.Value.Content.ToArray();
        mod2.Length.ShouldBe(260);
        Encoding.ASCII.GetString(mod2, 0, 17).ShouldBe(@"CityMaps\Test.nif");
        mod2.Skip(17).ShouldAllBe(b => b == 0);
        foreach (var read in Read(Write(w => ((IGenericBaseFormGetter)gbfm).WriteToBinary(w))))
        {
            read.Components.OfType<ICityMapsUsageComponentGetter>().Single().ModelPath.ShouldBe(@"CityMaps\Test.nif");
        }
    }
}
