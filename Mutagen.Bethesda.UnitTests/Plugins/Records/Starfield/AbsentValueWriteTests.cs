using System.Buffers.Binary;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Values that the readers leave absent (null) must be writable, so that a record
// read by Mutagen, or a freshly constructed one, can be written back.
public class AbsentValueWriteTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private static readonly MasterReferenceCollection Masters = new(TestModKey);
    private static readonly FormKey TestFormKey = new(TestModKey, 0x800);

    private static ParsingMeta Meta() => new(
        GameConstants.Starfield,
        TestModKey,
        SeparatedMasterPackage.NotSeparate(Masters));

    private static byte[] Subrecord(string type, byte[] content)
    {
        var header = new byte[6];
        Encoding.ASCII.GetBytes(type).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), checked((ushort)content.Length));
        return header.Concat(content).ToArray();
    }

    private static byte[] Record(string type, params byte[][] subrecords)
    {
        var content = subrecords.SelectMany(x => x).ToArray();
        var header = new byte[GameConstants.Starfield.MajorConstants.HeaderLength];
        Encoding.ASCII.GetBytes(type).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)content.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), TestFormKey.ID);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 555);
        return header.Concat(content).ToArray();
    }

    private const uint DeletedFlag = 0x20;

    private static byte[] Deleted(byte[] record)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), DeletedFlag);
        return record;
    }

    private static byte[] EditorId(string edid) => Subrecord("EDID", Encoding.ASCII.GetBytes(edid + "\0"));

    private static MutagenFrame Frame(byte[] bytes) => new(new MutagenMemoryReadStream(bytes, Meta()));

    private static byte[] Write(Action<MutagenWriter> write)
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
            write(writer);
        }
        return stream.ToArray();
    }

    private static IEnumerable<IInstanceNamingRulesGetter> ReadInstanceNamingRules(byte[] bytes)
    {
        yield return InstanceNamingRules.CreateFromBinary(Frame(bytes));
        var meta = Meta();
        yield return InstanceNamingRulesBinaryOverlay.InstanceNamingRulesFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    private static IEnumerable<IAObjectModificationGetter> ReadObjectModification(byte[] bytes)
    {
        yield return AObjectModification.CreateFromBinary(Frame(bytes), default);
        var meta = Meta();
        yield return AObjectModificationBinaryOverlay.AObjectModificationFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta),
            default);
    }

    [Fact]
    public void InstanceNamingRules_NoUnam_WritesBackWithoutUnam()
    {
        var bytes = Record("INNR", EditorId("TestInnr"));
        foreach (var innr in ReadInstanceNamingRules(bytes))
        {
            innr.Rules.ShouldBeNull();
            Write(w => innr.WriteToBinary(w)).ShouldBe(bytes);
        }
    }

    [Fact]
    public void InstanceNamingRules_New_WritesAndReadsBackNullRules()
    {
        var innr = new InstanceNamingRules(TestFormKey, StarfieldRelease.Starfield)
        {
            EditorID = "TestInnr",
        };
        var written = Write(w => innr.WriteToBinary(w));
        foreach (var reread in ReadInstanceNamingRules(written))
        {
            reread.EditorID.ShouldBe("TestInnr");
            reread.Rules.ShouldBeNull();
        }
    }

    [Fact]
    public void ObjectModification_NoData_WritesBackWithoutData()
    {
        var bytes = Record("OMOD", EditorId("TestOmod"));
        foreach (var omod in ReadObjectModification(bytes))
        {
            omod.ShouldBeAssignableTo<IUnknownObjectModificationGetter>()!
                .ObjectModificationTargetName.ShouldBeNull();
            omod.Unknown.ShouldBe((ushort)0);
            omod.AttachPoint.IsNull.ShouldBeTrue();
            omod.Includes.ShouldBeEmpty();
            Write(w => omod.WriteToBinary(w)).ShouldBe(bytes);
        }
    }

    [Fact]
    public void ObjectModification_DeletedNoData_Writes()
    {
        var bytes = Deleted(Record("OMOD", EditorId("TestOmod")));
        // Deleted records are written as their header only
        var expected = Deleted(Record("OMOD"));
        foreach (var omod in ReadObjectModification(bytes))
        {
            Write(w => omod.WriteToBinary(w)).ShouldBe(expected);
            omod.IsDeleted.ShouldBeTrue();
            omod.ShouldBeAssignableTo<IUnknownObjectModificationGetter>()!
                .ObjectModificationTargetName.ShouldBeNull();
        }
    }

    [Fact]
    public void ContainerModification_WritesAndReadsBack()
    {
        var keyword = new FormKey(TestModKey, 0x801);
        var omod = new ContainerModification(TestFormKey, StarfieldRelease.Starfield)
        {
            EditorID = "TestContainerMod",
            Unknown = 1,
            Unknown3 = 2,
        };
        omod.Properties.Add(new ObjectModFormLinkIntProperty<Container.Property>()
        {
            Property = Container.Property.Keyword,
            Record = new FormLink<IStarfieldMajorRecordGetter>(keyword),
            Value = 3,
        });
        var written = Write(w => omod.WriteToBinary(w));
        foreach (var reread in ReadObjectModification(written))
        {
            var container = reread.ShouldBeAssignableTo<IContainerModificationGetter>()!;
            container.EditorID.ShouldBe("TestContainerMod");
            container.Unknown.ShouldBe((ushort)1);
            container.Unknown3.ShouldBe(2u);
            var prop = container.Properties.ShouldHaveSingleItem()
                .ShouldBeAssignableTo<IObjectModFormLinkIntPropertyGetter<Container.Property>>()!;
            prop.Property.ShouldBe(Container.Property.Keyword);
            prop.Record.FormKey.ShouldBe(keyword);
            prop.Value.ShouldBe(3u);
        }
    }

    [Fact]
    public void GameplayOptionsGroup_NullOptions_ThrowsNamingTheField()
    {
        var gpog = new GameplayOptionsGroup(TestFormKey, StarfieldRelease.Starfield);
        var ex = Should.Throw<Exception>(() => Write(w => gpog.WriteToBinary(w)));
        ex.GetBaseException().Message.ShouldBe("Options was null");
    }
}
