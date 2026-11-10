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

// C97 on real data: WorkshopCatSandbox (249E4A:Fallout4.esm) holds its package data inputs out of index order (UNAM order
// 02 15 0C 05 06 07 ...). Before the fix the writer sorted them by index and the record came back reordered.
// Read whole with both readers and written back byte for byte.
public class PackageDataInputOrderRealDataTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private static readonly MasterReferenceCollection Masters = new(TestModKey);

    private static ParsingMeta Meta() => new(GameConstants.Fallout4, TestModKey, SeparatedMasterPackage.NotSeparate(Masters));

    // WorkshopCatSandbox (249E4A:Fallout4.esm), decompressed subrecords
    private const string Package = "454449441300576F726B73686F7043617453616E64626F7800504B44540C000000000012000100BF020000505344540C0000FF00FFFF00000000000000435444412000002E23410000803F45000000CF9A0C00000000000000000000000000FFFFFFFF435444412000002E23410000803F6A0100008F050200000000000000000000000000FFFFFFFF504B43550C0011000000A4C2050007000000414E414D09004C6F636174696F6E00504C44541000060000008F0502008813000000000000414E414D0F0054617267657453656C6563746F7200505444410C0007000000499E240000000000414E414D0500426F6F6C00434E414D010000414E414D0500426F6F6C00434E414D010001414E414D0500426F6F6C00434E414D010000414E414D0500426F6F6C00434E414D010001414E414D0500426F6F6C00434E414D010001414E414D0500426F6F6C00434E414D010001414E414D0500426F6F6C00434E414D010001414E414D0500426F6F6C00434E414D010001414E414D0500426F6F6C00434E414D010000414E414D0600466C6F617400434E414D040000000000414E414D0500426F6F6C00434E414D010000414E414D0B004F626A6563744C69737400434E414D040000000044414E414D0500426F6F6C00434E414D010000414E414D0500426F6F6C00434E414D010001414E414D0F0054617267657453656C6563746F7200505444410C00020000000000000000000000554E414D010002554E414D010015554E414D01000C554E414D010005554E414D010006554E414D010007554E414D010008554E414D010009554E414D01000E554E414D01000A554E414D010010554E414D010012554E414D010014554E414D010017554E414D010019554E414D01001B554E414D01001D584E414D010016504F42410000494E414D0400000000005044544F08000000000000000000504F45410000494E414D0400000000005044544F08000000000000000000504F43410000494E414D0400000000005044544F08000000000000000000";

    private static byte[] Record()
    {
        var body = Convert.FromHexString(Package);
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

    [Fact]
    public void RealPackage_DataInputsKeepTheirOrder()
    {
        var meta = Meta();
        var all = new IPackageGetter[]
        {
            Mutagen.Bethesda.Fallout4.Package.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(Record(), Meta()))),
            PackageBinaryOverlay.PackageFactory(new OverlayStream(Record(), meta), new BinaryOverlayFactoryPackage(meta)),
        };
        foreach (var pack in all)
        {
            pack.EditorID.ShouldBe("WorkshopCatSandbox");
            var stream = new MemoryStream();
            using (var writer = new MutagenWriter(stream, new WritingBundle(GameConstants.Fallout4)
                   {
                       MasterReferences = Masters,
                       SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(Masters),
                   }, dispose: false))
            {
                pack.WriteToBinary(writer);
            }
            GameConstants.Fallout4.MajorRecord(stream.ToArray()).Content.ToArray().ShouldBe(Convert.FromHexString(Package));
        }
    }
}
