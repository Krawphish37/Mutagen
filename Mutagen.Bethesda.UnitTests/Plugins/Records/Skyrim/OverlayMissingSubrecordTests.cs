using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Skyrim;

// Hand-written overlay getters that read a subrecord without checking it was present,
// and so disagreed with the full import when it was missing.
public class OverlayMissingSubrecordTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.SkyrimSE, TestModKey, masters);
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
        record.Write(BitConverter.GetBytes((ushort)44));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static IEnumerable<IPackageGetter> ReadPackages(byte[] bytes)
    {
        yield return Package.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return PackageBinaryOverlay.PackageFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    [Fact]
    public void Package_WithoutPkcu_PackageTemplateIsNullLink()
    {
        var bytes = Record("PACK", Subrecord("EDID", Encoding.ASCII.GetBytes("PackEdidOnly\0")));

        foreach (var package in ReadPackages(bytes))
        {
            package.PackageTemplate.ShouldNotBeNull();
            package.PackageTemplate.IsNull.ShouldBeTrue();
            Should.NotThrow(() => package.EnumerateFormLinks().ToArray());
            Should.NotThrow(() => package.DeepCopy());
        }
    }
}
