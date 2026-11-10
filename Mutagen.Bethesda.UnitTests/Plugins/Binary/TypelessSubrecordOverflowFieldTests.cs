using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

/// <summary>
/// Inside a typeless subrecord object handed a parent's overflow length (TypedParseParams.LengthOverride, the real
/// length of its first subrecord, whose header says 0), a field reads that length in both readers when it declares
/// overflowRecordType. (A field without it read the header's 0 in the overlay, so the generator now refuses a parent
/// overflowRecordType on a typeless object whose variable-length record-typed fields don't declare one, and passes
/// the override to the overlay factory as the full import already did.)
/// Here: Starfield's EffectSequenceComponent, a typeless object whose REFL declares overflowRecordType="XXXX".
/// </summary>
public class TypelessSubrecordOverflowFieldTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private const int OverflowLength = 70_000;

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Starfield, TestModKey, masters);
    }

    // REFL with a 0 length header followed by its 70,000 bytes (as after an XXXX), then an unrelated subrecord
    private static (byte[] Bytes, byte[] Payload) Data()
    {
        var payload = new byte[OverflowLength];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 7);
        var s = new MemoryStream();
        s.Write(Encoding.ASCII.GetBytes("REFL"));
        s.Write(BitConverter.GetBytes((ushort)0));
        s.Write(payload);
        s.Write(Encoding.ASCII.GetBytes("EDID"));
        s.Write(BitConverter.GetBytes((ushort)3));
        s.Write(Encoding.ASCII.GetBytes("ab\0"));
        return (s.ToArray(), payload);
    }

    [Fact]
    public void FullImport()
    {
        var (bytes, payload) = Data();
        var frame = new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta()));
        var comp = EffectSequenceComponent.CreateFromBinary(frame, TypedParseParams.FromLengthOverride(OverflowLength));
        comp.REFL!.Value.ToArray().ShouldBe(payload);
        frame.Position.ShouldBe(6 + OverflowLength);
    }

    [Fact]
    public void Overlay()
    {
        var (bytes, payload) = Data();
        var meta = Meta();
        var stream = new OverlayStream(bytes, meta);
        var comp = EffectSequenceComponentBinaryOverlay.EffectSequenceComponentFactory(
            stream, new BinaryOverlayFactoryPackage(meta), TypedParseParams.FromLengthOverride(OverflowLength));
        comp.REFL!.Value.ToArray().ShouldBe(payload);
    }
}
