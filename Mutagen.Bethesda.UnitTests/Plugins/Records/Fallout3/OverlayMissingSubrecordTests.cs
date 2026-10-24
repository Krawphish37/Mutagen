using System.Text;
using Mutagen.Bethesda.Fallout3;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Fallout3;

// Hand-written overlay getters that read a subrecord without checking it was present,
// and so disagreed with the full import when it was missing.
public class OverlayMissingSubrecordTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Fallout3, TestModKey, masters);
    }

    private static byte[] Subrecord(string type, byte[] content)
    {
        var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
        return stream.ToArray();
    }

    private static byte[] Record(string type, params byte[][] subrecords)
    {
        var body = subrecords.SelectMany(x => x).ToArray();
        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes(type));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)15));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static byte[] EditorID(string edid) => Subrecord("EDID", Encoding.ASCII.GetBytes(edid + "\0"));

    private static IEnumerable<IPackageGetter> ReadPackages(byte[] bytes)
    {
        yield return Package.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return PackageBinaryOverlay.PackageFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    [Fact]
    public void Package_WithoutPkdt_FlagsAreDefault()
    {
        var bytes = Record("PACK", EditorID("PackEdidOnly"));

        foreach (var package in ReadPackages(bytes))
        {
            package.Flags.ShouldBeNull();
            package.GeneralFlags.ShouldBe(default(Package.Flag));
            package.Unused.ShouldBe((byte)0);
            package.BehaviorFlags.ShouldBe(default(Package.BehaviorFlag));
            package.Unused2.ShouldBeNull();
            Should.NotThrow(() => package.DeepCopy());
        }
    }

    [Fact]
    public void Package_WithPkdt_ReadsFlags()
    {
        var pkdt = new byte[12];
        BitConverter.GetBytes(0x5u).CopyTo(pkdt, 0);
        pkdt[5] = 0x7;
        BitConverter.GetBytes((ushort)0x9).CopyTo(pkdt, 6);
        BitConverter.GetBytes((ushort)0xB).CopyTo(pkdt, 10);
        var bytes = Record("PACK", EditorID("PackPkdt"), Subrecord("PKDT", pkdt));

        foreach (var package in ReadPackages(bytes))
        {
            package.Flags.ShouldNotBeNull();
            ((uint)package.GeneralFlags).ShouldBe(0x5u);
            package.Unused.ShouldBe((byte)0x7);
            ((ushort)package.BehaviorFlags).ShouldBe((ushort)0x9);
            package.Unused2.ShouldBe((ushort)0xB);
        }
    }
}
