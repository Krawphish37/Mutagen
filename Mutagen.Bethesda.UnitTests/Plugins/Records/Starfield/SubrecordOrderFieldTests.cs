using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C82 for the smaller types: Mutagen wrote these records' subrecords in another order than Starfield.esm holds them.
// Fields moved to where most records have them (records written in another order, before -> after):
// - ACHR (PlacedNpc.xml): Layer XLYR after ReferenceGroup, RagdollBipedRotation XRGB after RagdollData, EnableParent XESP
//   after FactionRank, IsIgnoredBySandbox XIS2 after LocationRefTypes: 4,751 -> 4,131 of 9,530 (no single order fits:
//   XLCN, XLKR, XLRT and XLYR come in every order)
// - PGRE/PHZD (APlacedTrap in PlacedTrap.xml): LocationRefTypes XLRT and Layer XLYR around RagdollData: PGRE 307 -> 33,
//   PHZD 24 -> 2
// - CELL: ImageSpace XCIM before CellSkyRegion, AcousticSpace XCAS and Ownership XOWN after Music, EnvironmentMap XEMP
//   after Traversals: 483 -> 246 of 30,717
// - RACE subgraphs: TargetKeywords STKD before BehaviorGraph SGNM: 10 -> 0
// - PACK: data inputs written in the order they were read (Starfield.esm doesn't sort them by index): 484 -> 0; the
//   names/flags after the procedure tree by index read as unsigned (0x00..0x7F, then 0x80..)
// Each record below is a real one (minus localized FULL) that came back in another order before; read with both readers
// and written back byte for byte.
public class SubrecordOrderFieldTests
{
    // ACHR 014AEE (Starfield.esm): NAME XLYR XLKR XLKR DATA
    private const string Achr = "4E414D45040037F61A00584C59520400B93C2B00584C4B520800FABC02006ECB2F00584C4B5208004924030071CB2F00444154411800A47B5D4244742D423404D440000000000000000044ED5440";

    // PGRE 094744 (Starfield.esm): NAME XLYR XOWN DATA
    private const string Pgre = "4E414D450400FEC42100584C59520400B63C2B00584F574E0C008D0227000000000000000000444154411800FE338443FE55674300E01E43000000000000000000000000";

    // PHZD 084AC5 (Starfield.esm): NAME XLYR XRFG XESP DATA
    private const string Phzd = "4E414D4504005EAC1400584C5952040032AC1400585246470400B94A080058455350080033AC140000000000444154411800008065C3000042C200003442000000000000008000000000";

    // scMQ101a (0131DF:Starfield.esm): ... XILS XCIM XCCM
    private const string Cell = "45444944090073634D5131303161004441544104000109000058434C4C6C0028241700000000004B63610000803B4400803B46000000005A0000000000000000803B463333333F4B6361000000000000000000000000001311120000000000000000004B6361004B6361000000803F00803B4600204B46000000000000000000401C4600000000020000004C544D500400E200030058434C5704000000000058494C5304000000803F5843494D04002AA024005843434D0400A59F2400";

    // OE_Flee_Package (0F3CFA:Starfield.esm): data inputs SingleRef, Float, Float, Bool x5 with indexes 0E 0A 03 07 08 0B 0C 0D
    private const string Pack = "4544494410004F455F466C65655F5061636B61676500504B44540C00003000001200036550FA0000505344540C0000FF00FFFF00000000000000504B43550C0008000000349A030002000000414E414D0A0053696E676C6552656600505444410C0003000000987420000000000043495443040000000000414E414D0600466C6F617400434E414D040000009643414E414D0600466C6F617400434E414D040000002040414E414D0500426F6F6C00434E414D010000414E414D0500426F6F6C00434E414D010001414E414D0500426F6F6C00434E414D010000414E414D0500426F6F6C00434E414D010000414E414D0500426F6F6C00434E414D010000554E414D01000E554E414D01000A554E414D010003554E414D010007554E414D010008554E414D01000B554E414D01000C554E414D01000D584E414D01000F504F42410000494E414D0400000000005044544F08000000000000000000504F45410000494E414D0400000000005044544F08000000000000000000504F43410000494E414D0400000000005044544F08000000000000000000";

    // HopperARace (00A1D7:Starfield.esm), one subgraph: STKD, SGNM, SAPT, SRAF
    private const string Subgraph = "53544B4404000E531200" +
        "53474E4D2900416E696D54657874446174615C5461626C65735C4772617068735C4675726E69747572652E61677800" +
        "534150542B006163746F72735C686F70706572615C616E696D6174696F6E735C6675726E69747572655C77616C6C6F7700" +
        "53524146040002000000";

    private static void WritesBack<T>(string type, string hex, Func<Mutagen.Bethesda.Plugins.Binary.Streams.MutagenFrame, T> full,
        Func<Mutagen.Bethesda.Plugins.Binary.Overlay.OverlayStream, Mutagen.Bethesda.Plugins.Binary.Overlay.BinaryOverlayFactoryPackage, T> overlay)
        where T : IMajorRecordGetter
    {
        foreach (var rec in ReadBoth(Record(type, Hex(hex)), full, overlay))
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => rec.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(hex));
        }
    }

    [Fact]
    public void PlacedNpc_LayerBeforeLinkedReferences() => WritesBack<IPlacedNpcGetter>("ACHR", Achr,
        f => PlacedNpc.CreateFromBinary(f), (s, p) => PlacedNpcBinaryOverlay.PlacedNpcFactory(s, p));

    [Fact]
    public void PlacedTrap_LayerBeforeOwnership() => WritesBack<IPlacedTrapGetter>("PGRE", Pgre,
        f => PlacedTrap.CreateFromBinary(f), (s, p) => PlacedTrapBinaryOverlay.PlacedTrapFactory(s, p));

    [Fact]
    public void PlacedHazard_LayerBeforeReferenceGroup() => WritesBack<IPlacedHazardGetter>("PHZD", Phzd,
        f => PlacedHazard.CreateFromBinary(f), (s, p) => PlacedHazardBinaryOverlay.PlacedHazardFactory(s, p));

    [Fact]
    public void Cell_ImageSpaceBeforeSkyRegion() => WritesBack<ICellGetter>("CELL", Cell,
        f => Mutagen.Bethesda.Starfield.Cell.CreateFromBinary(f), (s, p) => CellBinaryOverlay.CellFactory(s, p));

    [Fact]
    public void Package_DataInputsInFileOrder()
    {
        WritesBack<IPackageGetter>("PACK", Pack,
            f => Package.CreateFromBinary(f), (s, p) => PackageBinaryOverlay.PackageFactory(s, p));
        foreach (var pack in ReadBoth<IPackageGetter>(Record("PACK", Hex(Pack)),
                     f => Package.CreateFromBinary(f), (s, p) => PackageBinaryOverlay.PackageFactory(s, p)))
        {
            pack.Data.Keys.ShouldBe(new sbyte[] { 0x0E, 0x0A, 0x03, 0x07, 0x08, 0x0B, 0x0C, 0x0D });
            pack.Data[0x0E].ShouldBeOfType<PackageDataTarget>();
        }
    }

    [Fact]
    public void Package_NewDataInputs_WrittenInInsertionOrder()
    {
        var pack = new Package(new Mutagen.Bethesda.Plugins.FormKey(TestModKey, 0x800), StarfieldRelease.Starfield);
        pack.Data[5] = new PackageDataBool { Data = true };
        pack.Data[2] = new PackageDataFloat { Data = 1.5f };
        var written = Write(w => pack.WriteToBinary(w));
        foreach (var reread in ReadBoth<IPackageGetter>(written,
                     f => Package.CreateFromBinary(f), (s, p) => PackageBinaryOverlay.PackageFactory(s, p)))
        {
            reread.Data.Keys.ShouldBe(new sbyte[] { 5, 2 });
            reread.Data[5].ShouldBeAssignableTo<IPackageDataBoolGetter>()!.Data.ShouldBeTrue();
            reread.Data[2].ShouldBeAssignableTo<IPackageDataFloatGetter>()!.Data.ShouldBe(1.5f);
        }
    }

    [Fact]
    public void Subgraph_TargetKeywordsBeforeBehaviorGraph()
    {
        var bytes = Hex(Subgraph);
        foreach (var subgraph in ReadBoth<ISubgraphGetter>(bytes,
                     f => Mutagen.Bethesda.Starfield.Subgraph.CreateFromBinary(f), (s, p) => SubgraphBinaryOverlay.SubgraphFactory(s, p)))
        {
            subgraph.TargetKeywords.ShouldHaveSingleItem().FormKey.ID.ShouldBe(0x12530Eu);
            subgraph.BehaviorGraph.ShouldBe(@"AnimTextData\Tables\Graphs\Furniture.agx");
            Write(w => subgraph.WriteToBinary(w)).ShouldBe(bytes);
        }
    }
}
