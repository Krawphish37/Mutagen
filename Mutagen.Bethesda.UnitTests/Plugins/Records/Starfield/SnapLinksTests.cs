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

public class SnapLinksTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private static readonly MasterReferenceCollection Masters = new(TestModKey);

    private static ParsingMeta Meta()
    {
        return new ParsingMeta(GameConstants.Starfield, TestModKey, SeparatedMasterPackage.NotSeparate(Masters));
    }

    private static void WriteSubrecord(MemoryStream stream, string type, byte[] content)
    {
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
    }

    private static byte[] UInts(params uint[] values)
    {
        return values.SelectMany(BitConverter.GetBytes).ToArray();
    }

    // XSL1: u32 count, then count x { FormID ref, u32 node pair count, n x { u32 parent, u32 linked } }
    private static readonly byte[] ThreeLinks = UInts(
        3,
        0x801, 1, 8, 10,
        0x802, 0,
        0x803, 1, 16, 6);

    private static byte[] MakePlacedObjectBytes(byte[] xsl1)
    {
        var content = new MemoryStream();
        WriteSubrecord(content, "NAME", UInts(0x800));
        WriteSubrecord(content, "XSL1", xsl1);
        WriteSubrecord(content, "XSCL", BitConverter.GetBytes(1.5f));
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("REFR"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x810));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)555));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static PlacedObject ReadDirect(byte[] bytes)
    {
        return PlacedObject.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
    }

    private static IPlacedObjectGetter ReadOverlay(byte[] bytes)
    {
        var meta = Meta();
        return PlacedObjectBinaryOverlay.PlacedObjectFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    private static IEnumerable<IPlacedObjectGetter> ReadBoth(byte[] bytes)
    {
        yield return ReadDirect(bytes);
        yield return ReadOverlay(bytes);
    }

    private static byte[] Write(IPlacedObjectGetter item)
    {
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(
                   stream,
                   new WritingBundle(GameConstants.Starfield)
                   {
                       MasterReferences = Masters,
                       SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(Masters),
                   }))
        {
            PlacedObjectBinaryWriteTranslation.Instance.Write(writer, item, default);
        }
        return stream.ToArray();
    }

    private static byte[] Xsl1Content(byte[] record)
    {
        return new MajorRecordFrame(GameConstants.Starfield, record)
            .FindSubrecord(RecordTypes.XSL1)
            .Content.ToArray();
    }

    [Fact]
    public void CountPrefixedEntries_ReadAsOneSnapLinkEach()
    {
        foreach (var placed in ReadBoth(MakePlacedObjectBytes(ThreeLinks)))
        {
            placed.SnapLinks.ShouldNotBeNull();
            placed.SnapLinks.Select(x => x.LinkedReference.FormKey).ShouldBe(new[]
            {
                new FormKey(TestModKey, 0x801),
                new FormKey(TestModKey, 0x802),
                new FormKey(TestModKey, 0x803),
            });
            placed.SnapLinks.Select(x => x.Links.Select(l => (l.ParentNode, l.LinkedNode)).ToArray()).ShouldBe(new[]
            {
                new[] { (8u, 10u) },
                Array.Empty<(uint, uint)>(),
                new[] { (16u, 6u) },
            });
            placed.Scale.ShouldBe(1.5f);
        }
    }

    [Fact]
    public void LinkedReferences_AreEnumeratedFormLinks()
    {
        foreach (var placed in ReadBoth(MakePlacedObjectBytes(ThreeLinks)))
        {
            var links = placed.EnumerateFormLinks().Select(x => x.FormKey).ToArray();
            links.ShouldContain(new FormKey(TestModKey, 0x801));
            links.ShouldContain(new FormKey(TestModKey, 0x802));
            links.ShouldContain(new FormKey(TestModKey, 0x803));
            links.ShouldNotContain(new FormKey(TestModKey, 3));
        }
    }

    [Fact]
    public void RemapLinks_UpdatesLinkedReferences()
    {
        var placed = ReadDirect(MakePlacedObjectBytes(ThreeLinks));
        placed.RemapLinks(new Dictionary<FormKey, FormKey>
        {
            { new FormKey(TestModKey, 0x801), new FormKey(TestModKey, 0x901) },
        });
        placed.SnapLinks!.Select(x => x.LinkedReference.FormKey).ShouldBe(new[]
        {
            new FormKey(TestModKey, 0x901),
            new FormKey(TestModKey, 0x802),
            new FormKey(TestModKey, 0x803),
        });
        Xsl1Content(Write(placed)).ShouldBe(UInts(
            3,
            0x901, 1, 8, 10,
            0x802, 0,
            0x803, 1, 16, 6));
    }

    [Fact]
    public void Write_RoundTripsXsl1()
    {
        foreach (var placed in ReadBoth(MakePlacedObjectBytes(ThreeLinks)))
        {
            Xsl1Content(Write(placed)).ShouldBe(ThreeLinks);
        }
    }

    [Fact]
    public void ZeroCount_ReadsEmptyAndRoundTrips()
    {
        foreach (var placed in ReadBoth(MakePlacedObjectBytes(UInts(0))))
        {
            placed.SnapLinks.ShouldNotBeNull();
            placed.SnapLinks.ShouldBeEmpty();
            placed.Scale.ShouldBe(1.5f);
            Xsl1Content(Write(placed)).ShouldBe(UInts(0));
        }
    }
}
