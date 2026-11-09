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

public class EmptyComponentTests
{
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

    // A component with nothing between its BFCB and BFCE, as found in some Creations
    private static byte[] MakeSurfaceBlockBytes(AComponent.ComponentType componentType)
    {
        var content = new MemoryStream();
        WriteSubrecord(content, "EDID", Encoding.ASCII.GetBytes("TestSurfaceBlock\0"));
        WriteSubrecord(content, "BFCB", Encoding.ASCII.GetBytes($"{componentType}\0"));
        WriteSubrecord(content, "BFCE", []);
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("SFBK"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)555));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static ISurfaceBlockGetter ReadDirect(byte[] bytes)
    {
        return SurfaceBlock.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
    }

    private static ISurfaceBlockGetter ReadOverlay(byte[] bytes)
    {
        var meta = Meta();
        return SurfaceBlockBinaryOverlay.SurfaceBlockFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    public static IEnumerable<object[]> ComponentTypes =>
        Enum.GetValues<AComponent.ComponentType>().Select(x => new object[] { x });

    [Theory]
    [MemberData(nameof(ComponentTypes))]
    public void EmptyComponent_OverlayMatchesDirect(AComponent.ComponentType componentType)
    {
        var bytes = MakeSurfaceBlockBytes(componentType);
        var direct = ReadDirect(bytes);
        var overlay = ReadOverlay(bytes);

        overlay.EditorID.ShouldBe("TestSurfaceBlock");
        var directComponent = direct.Components.ShouldHaveSingleItem();
        var overlayComponent = overlay.Components.ShouldHaveSingleItem().DeepCopy();
        overlayComponent.GetType().ShouldBe(directComponent.GetType());
        overlayComponent.Equals(directComponent).ShouldBeTrue();
    }

    [Fact]
    public void EmptyBlockEditorMetaDataComponent_OverlayReadsDefaults()
    {
        var overlay = ReadOverlay(MakeSurfaceBlockBytes(AComponent.ComponentType.BGSBlockEditorMetaData_Component));

        var component = overlay.Components.ShouldHaveSingleItem().ShouldBeAssignableTo<IBlockEditorMetaDataComponentGetter>()!;
        component.CreatedTime.ShouldBeEmpty();
        component.SourceType.ShouldBe((BlockEditorMetaDataComponent.SourceTypeOption)0);
        component.AlsoCreatedTime.ShouldBeEmpty();
        component.SourceEditorID.ShouldBeEmpty();
        component.UnknownItems.ShouldBeEmpty();
        component.LocationX.ShouldBe(0);
        component.SatelliteId.ShouldBe(0);
        component.Longitude.ShouldBe(0d);
        component.Latitude.ShouldBe(0d);
        component.UnknownEnding.Length.ShouldBe(0);
    }
}
