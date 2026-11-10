using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C82: Starfield.esm holds the same subrecords in more than one order (403 REFR subrecord pairs occur both ways), so no
// field order makes every record come back byte for byte. With GameConstants.KeepSubrecordOrder (Starfield), a record
// whose subrecords were read in an order Mutagen would not write keeps that order (SubrecordOrder in Core): the full
// import notes the subrecord types and a hash of each one's bytes, the overlay rereads its own bytes. On write, if the
// record is unchanged (every subrecord equal to the one in its place), it is written in the file's order; otherwise
// in Mutagen's order.
public class KeepSubrecordOrderTests
{
    // REFR 08518E (Starfield.esm): XRNK before XRDS and XGDS before XLYR; Mutagen writes XLYR before XGDS
    private const string Refr = "424643422400424753436F6E74616374536861646F77436F6D706F6E656E745F436F6D706F6E656E7400464C43531400BC74133C0000404000002041CDCC4C3E8FC2753D4246434500004E414D45040097DB3B0058524E4B04000000000058524453040004B6A540584C4947200068DCF2C1E4611F3EECC172408EC2AD3F83AD06C200000000ABABABAB00ABABAB584C4244180000000000000000000000000000000000000000000000ABAB58414C44100000000000000000000000000000000000584C534D040001000000584C56440400000000C0584C43440C0000ECFFFF0000000000ABABAB58474453040000000000584C5952040036F92F00584C524414000000000000000000000000000000000001ABABAB44415441180090EC383E4093F4C02C4EF33E3F4C5D3FDB0FC9BF85998040";

    // ACHR 014AEE (Starfield.esm): NAME XLYR XLKR XLKR DATA
    private const string Achr = "4E414D45040037F61A00584C59520400B93C2B00584C4B520800FABC02006ECB2F00584C4B5208004924030071CB2F00444154411800A47B5D4244742D423404D440000000000000000044ED5440";

    private static IEnumerable<IPlacedObjectGetter> ReadRefr() => ReadBoth<IPlacedObjectGetter>(Record("REFR", Hex(Refr)),
        f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p));

    private static List<string> Signatures(ReadOnlySpan<byte> content)
    {
        var ret = new List<string>();
        for (int p = 0; p + 6 <= content.Length; p += 6 + BitConverter.ToUInt16(content.Slice(p + 4, 2)))
            ret.Add(System.Text.Encoding.ASCII.GetString(content.Slice(p, 4)));
        return ret;
    }

    [Fact]
    public void OnlyStarfieldKeepsOrder()
    {
        GameConstants.Starfield.KeepSubrecordOrder.ShouldBeTrue();
        GameConstants.Fallout4.KeepSubrecordOrder.ShouldBeFalse();
        GameConstants.SkyrimSE.KeepSubrecordOrder.ShouldBeFalse();
        GameConstants.Oblivion.KeepSubrecordOrder.ShouldBeFalse();
    }

    [Fact]
    public void UnchangedRecord_WrittenInFileOrder()
    {
        foreach (var refr in ReadRefr())
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(Refr));
        }
    }

    [Fact]
    public void ChangedRecord_WrittenInMutagenOrder()
    {
        var refr = PlacedObject.CreateFromBinary(new Mutagen.Bethesda.Plugins.Binary.Streams.MutagenFrame(
            new Mutagen.Bethesda.Plugins.Binary.Streams.MutagenMemoryReadStream(Record("REFR", Hex(Refr)), Meta())));
        refr.Radius = 6f;
        var sigs = Signatures(GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w))).Content);
        sigs.IndexOf("XLYR").ShouldBeLessThan(sigs.IndexOf("XGDS"));
        sigs.OrderBy(x => x).ShouldBe(Signatures(Hex(Refr)).OrderBy(x => x));
    }

    [Fact]
    public void Copy_WrittenInMutagenOrder()
    {
        foreach (var refr in ReadRefr())
        {
            // A copy is a new record: it has no file order to keep
            var copy = refr.DeepCopy();
            var sigs = Signatures(GameConstants.Starfield.MajorRecord(Write(w => copy.WriteToBinary(w))).Content);
            sigs.IndexOf("XLYR").ShouldBeLessThan(sigs.IndexOf("XGDS"));
        }
    }

    [Fact]
    public void ReorderedList_KeepsTheNewOrder()
    {
        // Swapping the two linked references changes the bytes in each XLKR place, so the record counts as changed
        var achr = PlacedNpc.CreateFromBinary(new Mutagen.Bethesda.Plugins.Binary.Streams.MutagenFrame(
            new Mutagen.Bethesda.Plugins.Binary.Streams.MutagenMemoryReadStream(Record("ACHR", Hex(Achr)), Meta())));
        var first = achr.LinkedReferences[0];
        achr.LinkedReferences.RemoveAt(0);
        achr.LinkedReferences.Add(first);
        var written = Write(w => achr.WriteToBinary(w));
        foreach (var reread in ReadBoth<IPlacedNpcGetter>(written,
                     f => PlacedNpc.CreateFromBinary(f), (s, p) => PlacedNpcBinaryOverlay.PlacedNpcFactory(s, p)))
        {
            reread.LinkedReferences.Select(l => l.Reference.FormKey.ID).ShouldBe(new uint[] { 0x2FCB71, 0x2FCB6E });
        }
    }
}
