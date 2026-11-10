using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Translations.Binary;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield floats round-trip with their exact bits (GameConstants.ExactFloats). Mutagen's long-standing float rule
// (read float.Epsilon as 0, write -0.0 and float.Epsilon as 0) stays for the other games. In Starfield.esm -0.0
// (00 00 00 80) is common: e.g. 1,155,910 REFR DATA Rotation Y values, which were written back as 00 00 00 00.
public class ExactFloatTests
{
    // DATA of REFR 02071F:Starfield.esm (no EditorID): Rotation Y is -0.0
    private const string RefrData = "00-82-4B-BF-3B-D3-80-BF-33-33-33-3E-DC-0F-C9-BF-00-00-00-80-DB-0F-49-40";

    [Fact]
    public void PlacedObject_NegativeZeroRotation_RoundTrips()
    {
        var bytes = Record("REFR", Subrecord("DATA", RefrData));
        foreach (var refr in ReadBoth<IPlacedObjectGetter>(bytes, f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            BitConverter.SingleToUInt32Bits(refr.Rotation.Y).ShouldBe(0x8000_0000u);
            var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("DATA")).Content.ToArray().ShouldBe(Hex(RefrData));
        }
    }

    [Fact]
    public void Epsilon_ReadsAndWritesExactly()
    {
        // No float field in Starfield.esm was found holding float.Epsilon (01 00 00 00); this is REFR XSCL with it.
        var bytes = Record("REFR", Subrecord("XSCL", "01-00-00-00"));
        foreach (var refr in ReadBoth<IPlacedObjectGetter>(bytes, f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            refr.Scale.ShouldBe(float.Epsilon);
            var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("XSCL")).Content.ToArray().ShouldBe(Hex("01-00-00-00"));
        }
    }

    [Theory]
    [InlineData(0x8000_0000u)]
    [InlineData(0x0000_0001u)]
    public void OtherGames_KeepTheZeroRule(uint bits)
    {
        GameConstants.SkyrimSE.ExactFloats.ShouldBeFalse();
        GameConstants.Fallout4.ExactFloats.ShouldBeFalse();
        GameConstants.Oblivion.ExactFloats.ShouldBeFalse();
        GameConstants.Starfield.ExactFloats.ShouldBeTrue();
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(stream, GameConstants.Fallout4, dispose: false))
        {
            FloatBinaryTranslation<MutagenFrame, MutagenWriter>.Instance.Write(writer, BitConverter.UInt32BitsToSingle(bits));
        }
        // The fixes chain already writes -0.0 as -0.0 for every game (fix/keep-negative-zero-floats); float.Epsilon still becomes 0
        stream.ToArray().ShouldBe(bits == 0x8000_0000u ? BitConverter.GetBytes(bits) : new byte[4]);
    }
}
