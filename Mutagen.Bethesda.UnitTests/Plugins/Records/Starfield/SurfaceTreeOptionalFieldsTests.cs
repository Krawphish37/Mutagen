using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Starfield.Internals;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

public class SurfaceTreeOptionalFieldsTests
{
    private const int PatternCount = 65536;
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Starfield, TestModKey, masters);
    }

    private static void WriteSubrecord(MemoryStream stream, string type, byte[] content)
    {
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
    }

    private static void WriteOverflowSubrecord(MemoryStream stream, string type, byte[] content)
    {
        WriteSubrecord(stream, "XXXX", BitConverter.GetBytes(content.Length));
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes((ushort)0));
        stream.Write(content);
    }

    private static byte[] Patterns(uint firstId)
    {
        var ret = new byte[PatternCount * 4];
        BitConverter.GetBytes(firstId).CopyTo(ret, 0);
        return ret;
    }

    private static byte[] MakeSurfaceTreeBytes(bool withGrid)
    {
        var content = new MemoryStream();
        WriteSubrecord(content, "EDID", Encoding.ASCII.GetBytes("TestSurfaceTree\0"));
        if (withGrid)
        {
            WriteSubrecord(content, "CNAM", BitConverter.GetBytes(2));
            WriteSubrecord(content, "DNAM", BitConverter.GetBytes(256));
            WriteOverflowSubrecord(content, "FNAM", Patterns(0x801));
            WriteOverflowSubrecord(content, "FNAM", Patterns(0x802));
            WriteSubrecord(content, "NAM1", Encoding.ASCII.GetBytes("Filter\0"));
        }
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("SFTR"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)555));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static ISurfaceTreeGetter ReadDirect(byte[] bytes)
    {
        return SurfaceTree.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
    }

    private static ISurfaceTreeGetter ReadOverlay(byte[] bytes)
    {
        var meta = Meta();
        return SurfaceTreeBinaryOverlay.SurfaceTreeFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    private static IEnumerable<ISurfaceTreeGetter> ReadBoth(byte[] bytes)
    {
        yield return ReadDirect(bytes);
        yield return ReadOverlay(bytes);
    }

    private static byte[] Write(ISurfaceTreeGetter surfaceTree)
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
            surfaceTree.WriteToBinary(writer);
        }
        return memStream.ToArray();
    }

    private static void ShouldHaveNoGridSubrecords(byte[] bytes)
    {
        var record = GameConstants.Starfield.MajorRecord(bytes);
        record.TryFindSubrecord(RecordTypes.CNAM, out _).ShouldBeFalse();
        record.TryFindSubrecord(RecordTypes.DNAM, out _).ShouldBeFalse();
        record.TryFindSubrecord(RecordTypes.XXXX, out _).ShouldBeFalse();
        record.TryFindSubrecord(RecordTypes.FNAM, out _).ShouldBeFalse();
        record.TryFindSubrecord(RecordTypes.NAM1, out _).ShouldBeFalse();
    }

    [Fact]
    public void AbsentGridFields_ReadAsNull()
    {
        var bytes = MakeSurfaceTreeBytes(withGrid: false);

        foreach (var surfaceTree in ReadBoth(bytes))
        {
            surfaceTree.EditorID.ShouldBe("TestSurfaceTree");
            surfaceTree.CNAM.ShouldBeNull();
            surfaceTree.EdgeSize.ShouldBeNull();
            surfaceTree.SurfacePatterns.ShouldBeNull();
            surfaceTree.SurfacePatterns2.ShouldBeNull();
            surfaceTree.Filter.ShouldBeNull();
        }
    }

    [Fact]
    public void AbsentGridFields_NotWritten()
    {
        var bytes = MakeSurfaceTreeBytes(withGrid: false);

        foreach (var surfaceTree in ReadBoth(bytes))
        {
            var written = Write(surfaceTree);
            ShouldHaveNoGridSubrecords(written);
            written.ShouldBe(bytes);
        }
    }

    [Fact]
    public void NewSurfaceTree_WritesNoGridSubrecords()
    {
        var surfaceTree = new SurfaceTree(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            EditorID = "TestSurfaceTree"
        };

        ShouldHaveNoGridSubrecords(Write(surfaceTree));
    }

    [Fact]
    public void PresentGridFields_ReadAndRoundTrip()
    {
        var bytes = MakeSurfaceTreeBytes(withGrid: true);

        foreach (var surfaceTree in ReadBoth(bytes))
        {
            surfaceTree.CNAM.ShouldBe(2u);
            surfaceTree.EdgeSize.ShouldBe(256u);
            surfaceTree.SurfacePatterns!.Value.Length.ShouldBe(PatternCount);
            surfaceTree.SurfacePatterns.Value[0].FormKey.ShouldBe(new FormKey(TestModKey, 0x801));
            surfaceTree.SurfacePatterns.Value[1].IsNull.ShouldBeTrue();
            surfaceTree.SurfacePatterns2!.Value.Length.ShouldBe(PatternCount);
            surfaceTree.SurfacePatterns2.Value[0].FormKey.ShouldBe(new FormKey(TestModKey, 0x802));
            surfaceTree.Filter.ShouldBe("Filter");
            Write(surfaceTree).ShouldBe(bytes);
        }
    }

    [Fact]
    public void AbsentGridFields_EqualsDeepCopy()
    {
        var bytes = MakeSurfaceTreeBytes(withGrid: false);

        foreach (var surfaceTree in ReadBoth(bytes))
        {
            surfaceTree.Equals(surfaceTree.DeepCopy()).ShouldBeTrue();
            surfaceTree.DeepCopy().Equals(ReadDirect(bytes)).ShouldBeTrue();
        }
    }

    [Fact]
    public void DeepCopy_DuplicatesPatternLinks()
    {
        var bytes = MakeSurfaceTreeBytes(withGrid: true);

        foreach (var surfaceTree in ReadBoth(bytes))
        {
            var copy = surfaceTree.DeepCopy();
            var patterns = copy.SurfacePatterns.ShouldNotBeNull();
            var patterns2 = copy.SurfacePatterns2.ShouldNotBeNull();
            patterns.Length.ShouldBe(PatternCount);
            patterns2.Length.ShouldBe(PatternCount);
            patterns[0].ShouldNotBeSameAs(surfaceTree.SurfacePatterns!.Value[0]);
            patterns2[0].ShouldNotBeSameAs(surfaceTree.SurfacePatterns2!.Value[0]);

            patterns[0].ShouldBeAssignableTo<IFormLink<ISurfacePatternGetter>>()!
                .SetTo(new FormKey(TestModKey, 0x999));
            patterns2[0].ShouldBeAssignableTo<IFormLink<ISurfacePatternGetter>>()!
                .SetTo(new FormKey(TestModKey, 0x999));
            surfaceTree.SurfacePatterns!.Value[0].FormKey.ShouldBe(new FormKey(TestModKey, 0x801));
            surfaceTree.SurfacePatterns2!.Value[0].FormKey.ShouldBe(new FormKey(TestModKey, 0x802));

            // Arrays are typed as the getter interface, so any link can be assigned
            patterns[1] = new FormLinkNullable<ISurfacePatternGetter>(new FormKey(TestModKey, 0x803));
            patterns[1].FormKey.ShouldBe(new FormKey(TestModKey, 0x803));
        }
    }

    [Fact]
    public void AbsentVersusPresentPatterns_NotEqual()
    {
        var absent = ReadDirect(MakeSurfaceTreeBytes(withGrid: false)).DeepCopy();
        var present = absent.DeepCopy();
        present.SurfacePatterns = Enumerable.Repeat(FormLink<ISurfacePatternGetter>.Null, PatternCount)
            .Cast<IFormLinkGetter<ISurfacePatternGetter>>()
            .ToArray();

        absent.Equals(present).ShouldBeFalse();
        present.Equals(absent).ShouldBeFalse();
        present.Equals(present.DeepCopy()).ShouldBeTrue();
    }
}
