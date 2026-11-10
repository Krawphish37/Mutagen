using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Skyrim;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Skyrim;

// Two package write-back bugs found with a whole-Skyrim.esm probe (on top of C97):
// - C116: a Bool data input's CNAM byte other than 0/1 (150 packages; 0x62 here) was written back as 0x01. It still
//   reads as true; the stored byte is kept in RawValue and written back while Data stays true.
// - C117: the UNAM/BNAM/PNAM list after the procedure tree (names and flags of inputs) was written sorted by index,
//   but records hold it in their own order (MQ304AlduinFlightGrabTemplate: 05 08 06 0A 0C 0D ..., while its data
//   inputs are 05 0D 0E 0F 10 15). As for Starfield (C82), the package keeps the read order (internal
//   ProcedureTreeDataOrder, kept by copies, not compared) and writes it while the indexes there are unchanged.
// Each real package is read whole with both readers and written back byte for byte.
public class PackageBoolAndNameOrderTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private static readonly MasterReferenceCollection Masters = new(TestModKey);

    private static ParsingMeta Meta() => new(GameConstants.SkyrimLE, TestModKey, SeparatedMasterPackage.NotSeparate(Masters));

    // IvarsteadTembaMillSandbox0x0 (083DAB:Skyrim.esm)
    private const string TembaMill = "454449441D0049766172737465616454656D62614D696C6C53616E64626F7830783000504B44540C0000000000120002ABFFFE7E00505344540C00FFFF00FFFFABABAB00000000504B43550C000C000000EE0406000B000000414E414D09004C6F636174696F6E00504C44540C0000000000A93D080000020000414E414D0500426F6F6C00434E414D010000414E414D0500426F6F6C00434E414D010000414E414D0500426F6F6C00434E414D010001414E414D0500426F6F6C00434E414D010001414E414D0500426F6F6C00434E414D010001414E414D0500426F6F6C00434E414D010001414E414D0500426F6F6C00434E414D010000414E414D0500426F6F6C00434E414D010000414E414D0500426F6F6C00434E414D010000414E414D0600466C6F617400434E414D040000004842414E414D0500426F6F6C00434E414D010062554E414D010000554E414D01000E554E414D010001554E414D010003554E414D010004554E414D010005554E414D010006554E414D010007554E414D010019554E414D01001B554E414D01001D554E414D01001F584E414D01001E504F42410000494E414D0400000000005044544F08000000000000000000504F45410000494E414D0400000000005044544F08000000000000000000504F43410000494E414D0400000000005044544F08000000000000000000";

    // MQ304AlduinFlightGrabTemplate (0E997A:Skyrim.esm)
    private const string AlduinGrab = "454449441E004D51333034416C6475696E466C696768744772616254656D706C61746500504B44540C00000010081300026400000000505344540C00FFFF00FFFF00000000000000504B43550C00060000000000000001000000414E414D0A0053696E676C6552656600505444410C00030000000000000000000000414E414D0600466C6F617400434E414D040000008044414E414D0500426F6F6C00434E414D010000414E414D0A0053696E676C6552656600505444410C00030000000000000000000000414E414D0A0053696E676C6552656600505444410C00030000000000000000000000414E414D0500426F6F6C00434E414D010001554E414D010005554E414D01000D554E414D01000E554E414D01000F554E414D010010554E414D010015584E414D010016414E414D090053657175656E636500434954430400000000005052434208000300000000000000414E414D0A0050726F6365647572650043495443040000000000504E414D0700506174726F6C00464E414D040000000000504B433201000F504B433201000D504B433201000E504B433201000E504B433201000E504B433201000E414E414D0A0050726F6365647572650043495443040001000000435444412000802ACC1A000000400E0000004400000000000000060000000000000005000000504E414D0B00466C696768744772616200464E414D040000000000504B4332010005504B4332010015414E414D0A0050726F6365647572650043495443040000000000504E414D0700506174726F6C00464E414D040001000000504B4332010010504B433201000D504B433201000E504B433201000E504B433201000E504B433201000E554E414D010005424E414D070054617267657400504E414D040001000000554E414D010008424E414D09004F726269744C6F6300504E414D040001000000554E414D010006424E414D0F004F726269744D696E52616469757300504E414D040001000000554E414D01000A424E414D0F004F726269744D617852616469757300504E414D040001000000554E414D01000C424E414D0C004F7262697448656967687400504E414D040001000000554E414D01000D424E414D0C00506F696E7452616469757300504E414D040001000000554E414D01000E424E414D0A00426F6F6C46616C736500504E414D040000000000554E414D01000F424E414D1500506174726F6C50617468426567696E537461727400504E414D040001000000554E414D010010424E414D1300506174726F6C50617468456E64537461727400504E414D040001000000554E414D010012424E414D0A00426F6F6C46616C736500504E414D040000000000554E414D010015424E414D0900426F6F6C5472756500504E414D040000000000504F42410000494E414D0400000000005044544F08000000000000000000504F45410000494E414D0400000000005044544F08000000000000000000504F43410000494E414D0400000000005044544F08000000000000000000";

    private static byte[] Record(string hex)
    {
        var body = Convert.FromHexString(hex);
        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("PACK"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)43));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static IEnumerable<IPackageGetter> ReadBoth(string hex)
    {
        yield return Package.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(Record(hex), Meta())));
        var meta = Meta();
        yield return PackageBinaryOverlay.PackageFactory(new OverlayStream(Record(hex), meta), new BinaryOverlayFactoryPackage(meta));
    }

    private static byte[] Write(IPackageGetter pack)
    {
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(stream, new WritingBundle(GameConstants.SkyrimLE)
               {
                   MasterReferences = Masters,
                   SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(Masters),
               }, dispose: false))
        {
            pack.WriteToBinary(writer);
        }
        return GameConstants.SkyrimLE.MajorRecord(stream.ToArray()).Content.ToArray();
    }

    [Fact]
    public void BoolInput_KeepsStoredByte()
    {
        foreach (var pack in ReadBoth(TembaMill))
        {
            var b = pack.Data[0x1F].ShouldBeAssignableTo<IPackageDataBoolGetter>()!;
            b.Data.ShouldBeTrue();
            b.RawValue.ShouldBe((byte)0x62);
            pack.Data[0x0E].ShouldBeAssignableTo<IPackageDataBoolGetter>()!.RawValue.ShouldBeNull();
            Write(pack).ShouldBe(Convert.FromHexString(TembaMill));
        }
    }

    [Fact]
    public void BoolInput_SetFalse_WritesZero_SetTrue_WritesOne()
    {
        var pack = Package.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(Record(TembaMill), Meta())));
        var b = (PackageDataBool)pack.Data[0x1F];
        b.Data = false;
        var written = Write(pack);
        var copy = Package.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(Record(Convert.ToHexString(written)), Meta())));
        ((PackageDataBool)copy.Data[0x1F]).Data.ShouldBeFalse();
        var fresh = new PackageDataBool { Data = true };
        pack.Data[0x1F] = fresh;
        var copy2 = Package.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(Record(Convert.ToHexString(Write(pack))), Meta())));
        ((PackageDataBool)copy2.Data[0x1F]).Data.ShouldBeTrue();
        ((PackageDataBool)copy2.Data[0x1F]).RawValue.ShouldBeNull();
    }
    [Fact]
    public void NameList_KeepsRecordOrder()
    {
        foreach (var pack in ReadBoth(AlduinGrab))
        {
            Write(pack).ShouldBe(Convert.FromHexString(AlduinGrab));
            Write(pack.DeepCopy()).ShouldBe(Convert.FromHexString(AlduinGrab));
        }
    }

    [Fact]
    public void NameList_OrderIsNotData()
    {
        var read = Package.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(Record(AlduinGrab), Meta())));
        read.ProcedureTreeDataOrder.ShouldNotBeNull().Take(3).ShouldBe(new sbyte[] { 0x05, 0x08, 0x06 });
        // Without the read order the list is written by index: other bytes, same data
        var copy = read.DeepCopy();
        copy.ProcedureTreeDataOrder = null;
        var sorted = Write(copy);
        sorted.ShouldNotBe(Convert.FromHexString(AlduinGrab));
        var readSorted = Package.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(Record(Convert.ToHexString(sorted)), Meta())));
        readSorted.Equals(read).ShouldBeTrue();
        read.Equals(readSorted).ShouldBeTrue();
        readSorted.GetHashCode().ShouldBe(read.GetHashCode());
    }
}
