using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A placed light's flicker (XLFD) is Period, Intensity Amplitude, Movement Amplitude (floats) and Flicker Effect (one
// byte: None, Flicker, Pulse) + 3 unused (xEdit's Starfield definitions). Movement Amplitude is non-zero on 142 of 7,367
// in Starfield.esm; read as a UInt32, 0.75 became 1,061,158,912.
public class PlacedLightFlickerTests
{
    // XLFD of REFR 3AE6B in Starfield.esm
    private const string Flicker = "00-00-A0-40-CD-CC-4C-3E-CD-CC-CC-3D-02-AB-AB-AB";

    [Fact]
    public void Flicker_Reads()
    {
        var bytes = Record("REFR", Subrecord("XLFD", Flicker));
        foreach (var placed in ReadBoth<IPlacedObjectGetter>(bytes, f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            var flicker = placed.LightFlicker!;
            flicker.Period.ShouldBe(5f);
            flicker.IntensityAmplitude.ShouldBe(0.2f);
            flicker.MovementAmplitude.ShouldBe(0.1f);
            flicker.FlickerEffect.ShouldBe(Light.FlickerEffectOption.Pulse);
        }
    }

    [Fact]
    public void Flicker_RoundTrips()
    {
        var placed = PlacedObject.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(Record("REFR", Subrecord("XLFD", Flicker)), Meta())));
        var written = GameConstants.Starfield.MajorRecord(Write(w => placed.WriteToBinary(w)));
        written.FindSubrecord(new RecordType("XLFD")).Content.ToArray().ShouldBe(Hex(Flicker));
    }
}
