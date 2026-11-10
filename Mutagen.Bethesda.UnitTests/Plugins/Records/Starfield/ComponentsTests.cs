using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

/// <summary>
/// Records authored in the Creation Kit can carry BFCB..BFCE component blocks on record types
/// that vanilla never uses them on.  These must be read into Components and written back out.
/// </summary>
public class ComponentsTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private static readonly MasterReferenceCollection Masters = new(TestModKey);
    private static readonly FormKey KeywordKey = new(TestModKey, 0x801);

    private static ParsingMeta Meta()
    {
        return new ParsingMeta(GameConstants.Starfield, TestModKey, SeparatedMasterPackage.NotSeparate(Masters));
    }

    private static byte[] Subrecord(string type, byte[] content)
    {
        var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
        return stream.ToArray();
    }

    private static byte[] Zstring(string s) => Encoding.ASCII.GetBytes(s + "\0");

    private static byte[] Component(string name, params byte[][] subrecords)
    {
        return Subrecord("BFCB", Zstring(name))
            .Concat(subrecords.SelectMany(x => x))
            .Concat(Subrecord("BFCE", []))
            .ToArray();
    }

    private static byte[] KeywordComponent()
    {
        return Component(
            "BGSKeywordForm_Component",
            Subrecord("KSIZ", BitConverter.GetBytes(1u)),
            Subrecord("KWDA", BitConverter.GetBytes(0x801u)));
    }

    private static byte[] MakeRecord(string recordType, params byte[][] subrecords)
    {
        var body = subrecords.SelectMany(x => x).ToArray();
        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes(recordType));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)555));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static byte[] Write(IBinaryItem item)
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
            item.WriteToBinary(writer);
        }
        return stream.ToArray();
    }

    private static void AssertKeywordComponent(IReadOnlyList<IAComponentGetter> components)
    {
        components.Count.ShouldBe(1);
        var keywords = components[0].ShouldBeAssignableTo<IKeywordFormComponentGetter>()!;
        keywords.Keywords.ShouldNotBeNull();
        keywords.Keywords.Select(x => x.FormKey).ShouldBe([KeywordKey]);
    }

    private static void RunKeywordComponentCase<TGetter>(
        byte[] bytes,
        Func<MutagenFrame, TGetter> readDirect,
        Func<OverlayStream, BinaryOverlayFactoryPackage, TGetter> readOverlay,
        Func<TGetter, IReadOnlyList<IAComponentGetter>> getComponents)
        where TGetter : IMajorRecordGetter, IBinaryItem
    {
        var direct = readDirect(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        var overlay = readOverlay(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
        foreach (var record in new[] { direct, overlay })
        {
            record.EditorID.ShouldBe("Test");
            AssertKeywordComponent(getComponents(record));
            var written = Write(record);
            written.ShouldBe(bytes);
        }
    }

    [Fact]
    public void ConditionRecord_KeywordComponent_RoundTrips()
    {
        RunKeywordComponentCase<IConditionRecordGetter>(
            MakeRecord("CNDF",
                Subrecord("EDID", Zstring("Test")),
                KeywordComponent()),
            f => ConditionRecord.CreateFromBinary(f),
            (s, p) => ConditionRecordBinaryOverlay.ConditionRecordFactory(s, p),
            x => x.Components);
    }

    [Fact]
    public void PlanetContentManagerTree_KeywordComponent_RoundTrips()
    {
        RunKeywordComponentCase<IPlanetContentManagerTreeGetter>(
            MakeRecord("PCMT",
                Subrecord("EDID", Zstring("Test")),
                KeywordComponent(),
                Subrecord("NAM1", new byte[4]),
                Subrecord("NAM2", new byte[4]),
                Subrecord("NAM5", new byte[1])),
            f => PlanetContentManagerTree.CreateFromBinary(f),
            (s, p) => PlanetContentManagerTreeBinaryOverlay.PlanetContentManagerTreeFactory(s, p),
            x => x.Components);
    }

    [Fact]
    public void Outfit_KeywordComponent_RoundTrips()
    {
        RunKeywordComponentCase<IOutfitGetter>(
            MakeRecord("OTFT",
                Subrecord("EDID", Zstring("Test")),
                KeywordComponent(),
                Subrecord("INAM", BitConverter.GetBytes(0x802u))),
            f => Outfit.CreateFromBinary(f),
            (s, p) => OutfitBinaryOverlay.OutfitFactory(s, p),
            x => x.Components);
    }

    [Fact]
    public void Message_KeywordComponent_RoundTrips()
    {
        RunKeywordComponentCase<IMessageGetter>(
            MakeRecord("MESG",
                Subrecord("EDID", Zstring("Test")),
                KeywordComponent(),
                Subrecord("DESC", Zstring("Description")),
                Subrecord("INAM", new byte[4]),
                Subrecord("DNAM", new byte[4])),
            f => Message.CreateFromBinary(f),
            (s, p) => MessageBinaryOverlay.MessageFactory(s, p),
            x => x.Components);
    }

    [Fact]
    public void WWiseEventData_KeywordComponent_RoundTrips()
    {
        RunKeywordComponentCase<IWWiseEventDataGetter>(
            MakeRecord("WWED",
                Subrecord("EDID", Zstring("Test")),
                KeywordComponent()),
            f => WWiseEventData.CreateFromBinary(f),
            (s, p) => WWiseEventDataBinaryOverlay.WWiseEventDataFactory(s, p),
            x => x.Components);
    }

    [Fact]
    public void PlacedHazard_KeywordComponent_RoundTrips()
    {
        RunKeywordComponentCase<IPlacedHazardGetter>(
            MakeRecord("PHZD",
                Subrecord("EDID", Zstring("Test")),
                KeywordComponent(),
                Subrecord("NAME", BitConverter.GetBytes(0x802u)),
                Subrecord("DATA", new byte[24])),
            f => PlacedHazard.CreateFromBinary(f),
            (s, p) => PlacedHazardBinaryOverlay.PlacedHazardFactory(s, p),
            x => x.Components);
    }

    [Fact]
    public void Package_KeywordComponent_IsReadAndWritten()
    {
        var bytes = MakeRecord("PACK",
            Subrecord("EDID", Zstring("Test")),
            KeywordComponent(),
            Subrecord("PKDT", new byte[12]),
            Subrecord("PSDT", new byte[12]),
            Subrecord("PKCU", new byte[12]));
        var meta = Meta();
        var records = new IPackageGetter[]
        {
            Package.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta()))),
            PackageBinaryOverlay.PackageFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta)),
        };
        foreach (var record in records)
        {
            AssertKeywordComponent(record.Components);
            // Package writes other always-present subrecords, so only check the component survives
            var written = Write(record);
            var reread = Package.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(written, Meta())));
            AssertKeywordComponent(reread.Components);
        }
    }

    private static byte[] LocationWithComponents()
    {
        return MakeRecord("LCTN",
            Subrecord("EDID", Zstring("Test")),
            Component(
                "BGSWorldSpaceOverlay_Component",
                Subrecord("SNAM", [0xFF, 0xFF, 0xFF, 0xFF]),
                Subrecord("PNAM", [0xFF, 0xFF, 0xFF, 0xFF]),
                Subrecord("BNAM", new byte[4])),
            Component(
                "BGSPlanetContentManagerContentProperties_Component",
                Subrecord("ZNAM", new byte[4]),
                Subrecord("YNAM", [7]),
                Subrecord("XNAM", new byte[4])),
            Subrecord("FULL", Zstring("Location Name")),
            Subrecord("PNAM", BitConverter.GetBytes(0x802u)));
    }

    private static IEnumerable<ILocationGetter> ReadLocations(byte[] bytes)
    {
        yield return Location.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return LocationBinaryOverlay.LocationFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    [Fact]
    public void Location_ComponentSubrecords_AreNotReadAsLocationFields()
    {
        foreach (var location in ReadLocations(LocationWithComponents()))
        {
            location.Components.Count.ShouldBe(2);
            var overlay = location.Components[0].ShouldBeAssignableTo<IWorldSpaceOverlayComponentGetter>()!;
            overlay.PlanetId.ShouldBe(-1);
            var props = location.Components[1].ShouldBeAssignableTo<IPlanetContentManagerContentPropertiesComponentGetter>()!;
            props.DoAllBeforeRepeating.ShouldBe((byte)7);
            props.NumberOfTimesAllowedGlobal.ShouldBe(0u);

            location.ParentLocation.FormKey.ShouldBe(new FormKey(TestModKey, 0x802));
            location.PlanetID.ShouldBeNull();
            location.StarID.ShouldBeNull();
            location.Name.ShouldNotBeNull();
            location.Name.String.ShouldBe("Location Name");
        }
    }

    [Fact]
    public void Location_Components_RoundTrip()
    {
        var bytes = LocationWithComponents();
        foreach (var location in ReadLocations(bytes))
        {
            Write(location).ShouldBe(bytes);
        }
    }
}
