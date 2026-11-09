using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// REFR subrecords are unordered (xEdit SetUnordered) and Starfield.esm has no single order: for 403 pairs of
// subrecords both orders occur (e.g. NAME XLYR XLMS DATA on 103,951 refs, NAME XLMS XLYR DATA on 84,473). Mutagen writes
// fields in XML order, so 951,699 of 3,291,860 REFR came out with their subrecords in a different order. Five fields
// moved in PlacedObject.xml (ReferenceGroup XRFG after XLRD, SourcePackIn XPCS after XLCD, FactionRank XRNK after XACT,
// LightFlicker XLFD and Ownership XOWN after XESP) bring that to 478,556; no single order does much better
// (a search over all orders found 474,859 at best).
public class PlacedObjectSubrecordOrderTests
{
    // Whole REFR 0624B3 (Starfield.esm): NAME XLYR XRFG XSCL DATA, the order of 154,318 refs
    private const string LayerThenGroup = "4E414D45040019CA0600584C59520400AA240600585246470400C92406005853434C04000000C03F44415441180031900E42DFFF07419A9999C0C5A53C3EDB0FC9BF1F7BB13F";

    // Whole REFR 221C13 (Starfield.esm): NAME XLYR XRFG DATA (110,010 refs)
    private const string LayerThenGroupNoScale = "4E414D4504009B3F2500584C59520400A61C2200585246470400151C2200444154411800F270B4BE009481BE01CF4D3E5AED75BF95F0E93E345DA040";

    // REFR 08518E (Starfield.esm): a light with XRNK before XRDS (5,901 refs have that order) and XGDS before XLYR
    private const string RankThenRadius = "424643422400424753436F6E74616374536861646F77436F6D706F6E656E745F436F6D706F6E656E7400464C43531400BC74133C0000404000002041CDCC4C3E8FC2753D4246434500004E414D45040097DB3B0058524E4B04000000000058524453040004B6A540584C4947200068DCF2C1E4611F3EECC172408EC2AD3F83AD06C200000000ABABABAB00ABABAB584C4244180000000000000000000000000000000000000000000000ABAB58414C44100000000000000000000000000000000000584C534D040001000000584C56440400000000C0584C43440C0000ECFFFF0000000000ABABAB58474453040000000000584C5952040036F92F00584C524414000000000000000000000000000000000001ABABAB44415441180090EC383E4093F4C02C4EF33E3F4C5D3FDB0FC9BF85998040";

    private static IEnumerable<IPlacedObjectGetter> Read(string hex)
        => ReadBoth<IPlacedObjectGetter>(Record("REFR", Hex(hex)), f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p));

    private static List<string> Signatures(ReadOnlySpan<byte> content)
    {
        var ret = new List<string>();
        for (int p = 0; p + 6 <= content.Length; p += 6 + BitConverter.ToUInt16(content.Slice(p + 4, 2)))
            ret.Add(System.Text.Encoding.ASCII.GetString(content.Slice(p, 4)));
        return ret;
    }

    [Theory]
    [InlineData(LayerThenGroup)]
    [InlineData(LayerThenGroupNoScale)]
    public void LayerBeforeReferenceGroup_WrittenBackByteForByte(string hex)
    {
        foreach (var refr in Read(hex))
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(hex));
        }
    }

    [Fact]
    public void FactionRankBeforeRadius_KeepsOrder()
    {
        foreach (var refr in Read(RankThenRadius))
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
            // XRNK now comes before XRDS, as stored. (This record also has XGDS before XLYR, an order that stays different.)
            var sigs = Signatures(written.Content);
            sigs.IndexOf("XRNK").ShouldBeLessThan(sigs.IndexOf("XRDS"));
            sigs.Where(s => s != "XGDS").ShouldBe(Signatures(Hex(RankThenRadius)).Where(s => s != "XGDS"));
        }
    }
}
