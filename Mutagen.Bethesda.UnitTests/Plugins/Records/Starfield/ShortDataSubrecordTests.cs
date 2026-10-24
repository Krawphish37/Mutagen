using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

public class ShortDataSubrecordTests
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

    // The 64 byte ANAM of form version 575, which lacks the trailing ConeOfFireIgnoresMovement bool of later versions
    private static byte[] ShortAnam()
    {
        var anam = new MemoryStream();
        for (int i = 0; i < 15; i++)
        {
            anam.Write(i == 11 ? BitConverter.GetBytes(12u) : BitConverter.GetBytes(i + 1f));
        }
        anam.Write(BitConverter.GetBytes(0.7f));
        return anam.ToArray();
    }

    private static byte[] MakeAimModelBytes(byte[] anam, byte[] trailing)
    {
        var content = new MemoryStream();
        WriteSubrecord(content, "EDID", Encoding.ASCII.GetBytes("TestAimModel\0"));
        WriteSubrecord(content, "ANAM", anam);
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("AMDL"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)575));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        // Whatever follows the record in the file, such as the next record's header
        record.Write(trailing);
        return record.ToArray();
    }

    private static IAimModelGetter ReadDirect(byte[] bytes)
    {
        return AimModel.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
    }

    private static IAimModelGetter ReadOverlay(byte[] bytes)
    {
        var meta = Meta();
        return AimModelBinaryOverlay.AimModelFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    [Fact]
    public void AimModel_ShortAnamFollowedByMoreData_OverlayDoesNotReadPastIt()
    {
        var bytes = MakeAimModelBytes(ShortAnam(), Encoding.ASCII.GetBytes("AMDL"));
        var direct = ReadDirect(bytes);
        var overlay = ReadOverlay(bytes);

        direct.ConeOfFireIgnoresMovement.ShouldBeFalse();
        overlay.ConeOfFireIgnoresMovement.ShouldBeFalse();
        overlay.RecoilShotsForRunaway.ShouldBe(12u);
        overlay.RecoilBaseStability.ShouldBe(0.7f);
        overlay.DeepCopy().Equals(direct).ShouldBeTrue();
    }

    [Fact]
    public void AimModel_ShortAnamAtEndOfData_OverlayReadsDefault()
    {
        var bytes = MakeAimModelBytes(ShortAnam(), []);
        var direct = ReadDirect(bytes);
        var overlay = ReadOverlay(bytes);

        overlay.ConeOfFireIgnoresMovement.ShouldBeFalse();
        overlay.RecoilBaseStability.ShouldBe(0.7f);
        overlay.DeepCopy().Equals(direct).ShouldBeTrue();
    }

    [Fact]
    public void AimModel_FullAnam_OverlayReadsTrailingBool()
    {
        var bytes = MakeAimModelBytes([..ShortAnam(), 1], []);
        var direct = ReadDirect(bytes);
        var overlay = ReadOverlay(bytes);

        direct.ConeOfFireIgnoresMovement.ShouldBeTrue();
        overlay.ConeOfFireIgnoresMovement.ShouldBeTrue();
        overlay.DeepCopy().Equals(direct).ShouldBeTrue();
    }
}
