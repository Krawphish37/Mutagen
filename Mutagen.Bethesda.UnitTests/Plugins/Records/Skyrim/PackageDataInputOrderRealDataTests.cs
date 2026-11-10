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

// C97 on real data: ThievesGuildSapphireSandboxMultiPackage0x0 (000F8D:Skyrim.esm) holds its package data inputs out of index order (UNAM order
// 5C 00 20 23 29 25 ...). Before the fix the writer sorted them by index and the record came back reordered.
// Read whole with both readers and written back byte for byte.
public class PackageDataInputOrderRealDataTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private static readonly MasterReferenceCollection Masters = new(TestModKey);

    private static ParsingMeta Meta() => new(GameConstants.SkyrimLE, TestModKey, SeparatedMasterPackage.NotSeparate(Masters));

    // ThievesGuildSapphireSandboxMultiPackage0x0 (000F8D:Skyrim.esm), decompressed subrecords
    private const string Package = "454449442B00546869657665734775696C64536170706869726553616E64626F784D756C74695061636B61676530783000504B44540C000000000012000241B7FC0000505344540C00FFFF00FFFF61732C000000004354444120000069004E0000803F3B000000A8190C00C80000000000000000000000FFFFFFFF504B43550C0019000000868B060011000000414E414D0500426F6F6C00434E414D010001414E414D09004C6F636174696F6E00504C44540C00000000000E780B002C010000414E414D0600466C6F617400434E414D04000000F041414E414D09004C6F636174696F6E00504C44540C00000000000D780B00C2010000414E414D0600466C6F617400434E414D040000007042414E414D09004C6F636174696F6E00504C44540C0000000000503810002C010000414E414D0600466C6F617400434E414D040000007042414E414D09004C6F636174696F6E00504C44540C00030000000000000000000000414E414D0600466C6F617400434E414D040000000000414E414D0500426F6F6C00434E414D010000414E414D0500426F6F6C00434E414D010001414E414D0500426F6F6C00434E414D010000414E414D0500426F6F6C00434E414D010001414E414D0500426F6F6C00434E414D010001414E414D0500426F6F6C00434E414D010001414E414D0500426F6F6C00434E414D010001414E414D0500426F6F6C00434E414D010000414E414D0500426F6F6C00434E414D010000414E414D0600466C6F617400434E414D040000008C42414E414D0500426F6F6C00434E414D010000414E414D0500426F6F6C00434E414D010000414E414D09004C6F636174696F6E00504C44540C00030000000000000000000000414E414D0600466C6F617400434E414D040000000000414E414D09004C6F636174696F6E00504C44540C00030000000000000000000000414E414D0600466C6F617400434E414D040000000000554E414D01005C554E414D010000554E414D010020554E414D010023554E414D010029554E414D010025554E414D01002B554E414D010027554E414D01002D554E414D01000E554E414D010001554E414D010003554E414D010004554E414D010005554E414D010006554E414D01005E554E414D010007554E414D010019554E414D01001D554E414D01001B554E414D010021554E414D01005F554E414D010061554E414D010060554E414D010062584E414D01005F504F42410000494E414D0400000000005044544F08000000000000000000504F45410000494E414D0400000000005044544F08000000000000000000504F43410000494E414D0400000000005044544F08000000000000000000";

    private static byte[] Record()
    {
        var body = Convert.FromHexString(Package);
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

    [Fact]
    public void RealPackage_DataInputsKeepTheirOrder()
    {
        var meta = Meta();
        var all = new IPackageGetter[]
        {
            Mutagen.Bethesda.Skyrim.Package.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(Record(), Meta()))),
            PackageBinaryOverlay.PackageFactory(new OverlayStream(Record(), meta), new BinaryOverlayFactoryPackage(meta)),
        };
        foreach (var pack in all)
        {
            pack.EditorID.ShouldBe("ThievesGuildSapphireSandboxMultiPackage0x0");
            var stream = new MemoryStream();
            using (var writer = new MutagenWriter(stream, new WritingBundle(GameConstants.SkyrimLE)
                   {
                       MasterReferences = Masters,
                       SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(Masters),
                   }, dispose: false))
            {
                pack.WriteToBinary(writer);
            }
            GameConstants.SkyrimLE.MajorRecord(stream.ToArray()).Content.ToArray().ShouldBe(Convert.FromHexString(Package));
        }
    }
}
