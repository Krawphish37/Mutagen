using System.Buffers.Binary;
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

// C119: Skyrim's ArmorAddon WeightSliderEnabled is read by the full import into ArmorAddonWeightSliderContainer (keeps
// the raw DNAM bytes), by the overlay into a GenderedItem<bool>. The container had no Equals, so like C118 an ArmorAddon
// was never Equal to its overlay, a second import or its copy. It now compares by its male and female values.
public class ArmorAddonWeightSliderEqualityTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private static readonly MasterReferenceCollection Masters = new(TestModKey);

    private static ParsingMeta Meta() => new(GameConstants.SkyrimSE, TestModKey, SeparatedMasterPackage.NotSeparate(Masters));

    // MageApprenticeBootsVariant1AA (10E2DB:Skyrim.esm), DNAM weight slider bytes 02 02
    private const string MageBoots = "454449441E004D61676541707072656E74696365426F6F747356617269616E7431414100424F44540C008001000000A2CB0002000000524E414D040019000000444E414D0C000A0A020200000018000000004D4F44323100436C6F746865735C4D61676541707072656E746963655C4D61676541707072656E74696365426F6F74735F312E6E6966004D4F3254180011D36341646473008544CA93D9EC2ADB646473008544CA934D4F32532A000200000007000000626F6F74733A31DAE210000100000007000000626F6F74733A30DAE21000000000004D4F44333200436C6F746865735C4D61676541707072656E746963655C4D61676541707072656E74696365426F6F7473465F312E6E6966004D4F3354180011D36341646473008544CA93D9EC2ADB646473008544CA934D4F33532A000200000007000000626F6F74733A31DAE210000100000007000000626F6F74733A30DAE21000000000004D4F444C0400403701004D4F444C04003A8808004D4F444C0400413701004D4F444C04003C8808004D4F444C0400423701004D4F444C04003D8808004D4F444C0400D87C06004D4F444C0400BA820A004D4F444C0400433701004D4F444C0400408808004D4F444C0400443701004D4F444C0400448808004D4F444C0400453701004D4F444C0400458808004D4F444C04000A7610004D4F444C0400463701004D4F444C0400948708004D4F444C0400473701004D4F444C0400B9820A004D4F444C0400483701004D4F444C0400468808004D4F444C0400493701004D4F444C040084880800";

    private static byte[] Record(byte[] content)
    {
        var header = new byte[24];
        Encoding.ASCII.GetBytes("ARMA").CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)content.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 0x800);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 44);
        return header.Concat(content).ToArray();
    }

    private static IArmorAddonGetter ReadFull(byte[] bytes) =>
        ArmorAddon.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));

    private static IArmorAddonGetter ReadOverlay(byte[] bytes)
    {
        var meta = Meta();
        return ArmorAddonBinaryOverlay.ArmorAddonFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    [Fact]
    public void WeightSlider_FullImportOverlayAndCopyEqual()
    {
        var bytes = Record(Convert.FromHexString(MageBoots));
        var full = ReadFull(bytes);
        var overlay = ReadOverlay(bytes);
        full.WeightSliderEnabled.Male.ShouldBeTrue();
        full.WeightSliderEnabled.Female.ShouldBeTrue();
        full.WeightSliderEnabled.Equals(overlay.WeightSliderEnabled).ShouldBeTrue();
        full.WeightSliderEnabled.Equals(new GenderedItem<bool>(true, true)).ShouldBeTrue();
        full.WeightSliderEnabled.Equals(new GenderedItem<bool>(true, false)).ShouldBeFalse();
        full.WeightSliderEnabled.GetHashCode().ShouldBe(overlay.WeightSliderEnabled.GetHashCode());
        full.Equals(overlay).ShouldBeTrue();
        overlay.Equals(full).ShouldBeTrue();
        full.Equals(ReadFull(bytes)).ShouldBeTrue();
        ((ArmorAddon)full).DeepCopy().Equals(full).ShouldBeTrue();
    }
}
