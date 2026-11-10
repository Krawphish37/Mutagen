using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Fallout4;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Fallout4;

// C117 for Fallout 4: the UNAM/BNAM/PNAM list after the procedure tree (names and flags of the data inputs) was written
// sorted by index, but records hold it in their own order. DN121_TravelForgeSceneJakeFacePrisoner holds 01 03 05 07 04
// (the 5 differing packages of Fallout4.esm after C97 were all this). As for Starfield (C82), the package keeps the read
// order (internal ProcedureTreeDataOrder, kept by copies, not compared). Read whole with both readers and written back
// byte for byte.
public class PackageNameOrderTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private static readonly MasterReferenceCollection Masters = new(TestModKey);

    private static ParsingMeta Meta() => new(GameConstants.Fallout4, TestModKey, SeparatedMasterPackage.NotSeparate(Masters));

    // DN121_TravelForgeSceneJakeFacePrisoner (1A94EC:Fallout4.esm)
    private const string JakeFacePrisoner = "454449442700444E3132315F54726176656C466F7267655363656E654A616B6546616365507269736F6E657200504B44540C00002000001200010000000000505344540C00FFFF00FFFF000000000000004354444120008053184F0000E1433A00560940630200000000000000000000000000FFFFFFFF514E414D040040630200504B43550C00050000000000000001000000414E414D09004C6F636174696F6E00504C4454100000000000B83E13000000000000000000414E414D0500426F6F6C00434E414D010000414E414D0500426F6F6C00434E414D010001414E414D0500426F6F6C00434E414D010000414E414D0A0053696E676C6552656600505444410C00040000001A00000000000000554E414D010001554E414D010003554E414D010005554E414D010007554E414D010004584E414D010005414E414D090053657175656E636500434954430400000000005052434208000100000000000000414E414D0D0053696D756C74616E656F757300434954430400000000005052434208000200000000000000414E414D0A0050726F6365647572650043495443040000000000504E414D070054726176656C00464E414D040000000000504B4332010001504B43320100FF504B4332010003504B4332010005504B4332010007504B43320100FF414E414D0A0050726F6365647572650043495443040000000000504E414D0A0048656164747261636B00464E414D040000000000504B4332010004504B43320100FF504B43320100FF504B43320100FF504B43320100FF554E414D010001424E414D1000506C61636520746F2054726176656C00504E414D040001000000554E414D010003424E414D17005072656665722050726566657272656420506174683F00504E414D040001000000554E414D010005424E414D13005573652022736F667420726164697573223F00504E414D040001000000554E414D010007424E414D1B0049676E6F7265206D61726B6572206F7269656E746174696F6E3F00504E414D040001000000554E414D010004424E414D0A00507269736F6E65723100504E414D040001000000504F42410000494E414D0400000000005044544F08000000000000000000504F45410000494E414D0400000000005044544F08000000000000000000504F43410000494E414D0400000000005044544F08000000000000000000";

    private static byte[] Record()
    {
        var body = Convert.FromHexString(JakeFacePrisoner);
        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("PACK"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)131));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static byte[] Write(IPackageGetter pack)
    {
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(stream, new WritingBundle(GameConstants.Fallout4)
               {
                   MasterReferences = Masters,
                   SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(Masters),
               }, dispose: false))
        {
            pack.WriteToBinary(writer);
        }
        return GameConstants.Fallout4.MajorRecord(stream.ToArray()).Content.ToArray();
    }

    [Fact]
    public void NameList_KeepsRecordOrder()
    {
        var meta = Meta();
        var all = new IPackageGetter[]
        {
            Package.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(Record(), Meta()))),
            PackageBinaryOverlay.PackageFactory(new OverlayStream(Record(), meta), new BinaryOverlayFactoryPackage(meta)),
        };
        foreach (var pack in all)
        {
            Write(pack).ShouldBe(Convert.FromHexString(JakeFacePrisoner));
            Write(pack.DeepCopy()).ShouldBe(Convert.FromHexString(JakeFacePrisoner));
        }
    }

    [Fact]
    public void NameList_OrderIsNotData()
    {
        var read = Package.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(Record(), Meta())));
        read.ProcedureTreeDataOrder.ShouldBe(new sbyte[] { 0x01, 0x03, 0x05, 0x07, 0x04 });
        // Without the read order the list is written by index: other bytes, same data
        var copy = read.DeepCopy();
        copy.ProcedureTreeDataOrder = null;
        var sorted = Write(copy);
        sorted.ShouldNotBe(Convert.FromHexString(JakeFacePrisoner));
        var body = sorted;
        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("PACK"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)131));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        var readSorted = Package.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(record.ToArray(), Meta())));
        readSorted.Equals(read).ShouldBeTrue();
        read.Equals(readSorted).ShouldBeTrue();
        readSorted.GetHashCode().ShouldBe(read.GetHashCode());
    }
}
