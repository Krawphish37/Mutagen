using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Shouldly;
using Xunit;
using F3 = Mutagen.Bethesda.Fallout3;
using F4 = Mutagen.Bethesda.Fallout4;
using SK = Mutagen.Bethesda.Skyrim;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

// A <Break /> that follows a field of unknown length (here DebrisModel's null-terminated ModelFilename, then the
// optional Flags byte). The overlay used to check the break before the filename's end position was known, against a
// position in the whole record, so the break never fired: Flags was read past a DATA that ends after the filename, and
// writing the overlay wrote a byte that was never there. Both readers must agree and write the DATA back unchanged,
// with and without the trailing byte.
public class OverlayBreakAfterVariableFieldTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private const string Path = @"Meshes\Debris\Rock01.nif";

    private static ParsingMeta Meta(GameConstants constants) =>
        new(constants, TestModKey, SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey)));

    private static byte[] Sub(string type, byte[] content)
    {
        var s = new MemoryStream();
        s.Write(Encoding.ASCII.GetBytes(type));
        s.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        s.Write(content);
        return s.ToArray();
    }

    private static byte[] DebrisRecord(bool withFlags)
    {
        var data = new List<byte> { 0x32 };
        data.AddRange(Encoding.ASCII.GetBytes(Path));
        data.Add(0);
        if (withFlags) data.Add(0x01);
        var body = Sub("DATA", data.ToArray());
        var r = new MemoryStream();
        r.Write(Encoding.ASCII.GetBytes("DEBR"));
        r.Write(BitConverter.GetBytes(body.Length));
        r.Write(BitConverter.GetBytes(0));
        r.Write(BitConverter.GetBytes(0x800));
        r.Write(BitConverter.GetBytes(0));
        r.Write(BitConverter.GetBytes((ushort)0));
        r.Write(BitConverter.GetBytes((ushort)0));
        r.Write(body);
        return r.ToArray();
    }

    private static IEnumerable<T> ReadBoth<T>(GameConstants constants, byte[] bytes, Func<MutagenFrame, T> full, Func<OverlayStream, BinaryOverlayFactoryPackage, T> overlay)
    {
        yield return full(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta(constants))));
        var meta = Meta(constants);
        yield return overlay(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    private static byte[] WrittenData(GameConstants constants, Action<MutagenWriter> write)
    {
        var masters = new MasterReferenceCollection(TestModKey);
        var bundle = new WritingBundle(constants) { MasterReferences = masters, SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(masters) };
        var mem = new MemoryStream();
        using (var writer = new MutagenWriter(mem, bundle, dispose: false)) write(writer);
        return constants.MajorRecord(mem.ToArray()).FindSubrecord(new RecordType("DATA")).Content.ToArray();
    }

    private static byte[] OriginalData(GameConstants constants, bool withFlags) =>
        constants.MajorRecord(DebrisRecord(withFlags)).FindSubrecord(new RecordType("DATA")).Content.ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Skyrim_DebrisModel(bool withFlags)
    {
        var c = GameConstants.SkyrimSE;
        foreach (var debris in ReadBoth<SK.IDebrisGetter>(c, DebrisRecord(withFlags), f => SK.Debris.CreateFromBinary(f), (s, p) => SK.DebrisBinaryOverlay.DebrisFactory(s, p)))
        {
            var model = debris.Models.Single();
            model.Percentage.ShouldBe((byte)0x32);
            model.ModelFilename.GivenPath.ShouldBe(Path);
            model.DATADataTypeState.HasFlag(SK.DebrisModel.DATADataType.Break0).ShouldBe(!withFlags);
            model.Flags.ShouldBe(withFlags ? SK.DebrisModel.Flag.HasCollisionData : 0);
            WrittenData(c, w => debris.WriteToBinary(w)).ShouldBe(OriginalData(c, withFlags));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fallout4_DebrisModel(bool withFlags)
    {
        var c = GameConstants.Fallout4;
        foreach (var debris in ReadBoth<F4.IDebrisGetter>(c, DebrisRecord(withFlags), f => F4.Debris.CreateFromBinary(f), (s, p) => F4.DebrisBinaryOverlay.DebrisFactory(s, p)))
        {
            var model = debris.Models.Single();
            model.Percentage.ShouldBe((byte)0x32);
            model.ModelFilename.ShouldBe(Path);
            model.DATADataTypeState.HasFlag(F4.DebrisModel.DATADataType.Break0).ShouldBe(!withFlags);
            model.Flags.ShouldBe(withFlags ? F4.DebrisModel.Flag.HasCollisionData : 0);
            WrittenData(c, w => debris.WriteToBinary(w)).ShouldBe(OriginalData(c, withFlags));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fallout3_DebrisModel(bool withFlags)
    {
        var c = GameConstants.Fallout3;
        foreach (var debris in ReadBoth<F3.IDebrisGetter>(c, DebrisRecord(withFlags), f => F3.Debris.CreateFromBinary(f), (s, p) => F3.DebrisBinaryOverlay.DebrisFactory(s, p)))
        {
            var model = debris.Models.Single();
            model.Percentage.ShouldBe((byte)0x32);
            model.ModelFileName.ShouldBe(Path);
            model.DATADataTypeState.HasFlag(F3.DebrisModel.DATADataType.Break0).ShouldBe(!withFlags);
            model.HasCollision.ShouldBe(withFlags);
            WrittenData(c, w => debris.WriteToBinary(w)).ShouldBe(OriginalData(c, withFlags));
        }
    }
}
