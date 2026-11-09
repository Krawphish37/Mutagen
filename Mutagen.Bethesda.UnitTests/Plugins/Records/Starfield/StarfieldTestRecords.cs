using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Builds Starfield records from raw subrecord bytes (taken from Starfield.esm) and reads them with both
// the full import and the binary overlay, or writes a record and returns its bytes.
internal static class StarfieldTestRecords
{
    public static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    public static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Starfield, TestModKey, masters);
    }

    public static byte[] Hex(string hex) => Convert.FromHexString(hex.Replace("-", "").Replace(" ", ""));

    public static byte[] Subrecord(string type, byte[] content)
    {
        var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
        return stream.ToArray();
    }

    public static byte[] Subrecord(string type, string hex) => Subrecord(type, Hex(hex));

    public static byte[] Record(string type, params byte[][] subrecords)
    {
        var body = subrecords.SelectMany(x => x).ToArray();
        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes(type));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)555));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    public static IEnumerable<T> ReadBoth<T>(
        byte[] bytes,
        Func<MutagenFrame, T> full,
        Func<OverlayStream, BinaryOverlayFactoryPackage, T> overlay)
    {
        yield return full(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return overlay(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    public static byte[] Write(Action<MutagenWriter> write)
    {
        var masters = new MasterReferenceCollection(TestModKey);
        var bundle = new WritingBundle(GameConstants.Starfield)
        {
            MasterReferences = masters,
            SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(masters),
        };
        var memStream = new MemoryStream();
        using (var writer = new MutagenWriter(memStream, bundle, dispose: false))
        {
            write(writer);
        }
        return memStream.ToArray();
    }
}
