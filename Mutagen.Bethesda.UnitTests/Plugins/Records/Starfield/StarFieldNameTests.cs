using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Star BNAM is xEdit's System Parsec Location (three floats, in parsecs); Mutagen named it after its signature.
// Starfield.esm: 12 bytes on all 122 stars that have it.
public class StarFieldNameTests
{
    [Fact]
    public void BohrStar_SystemParsecLocation()
    {
        // BNAM of BohrStar (05E5BF:Starfield.esm)
        var bytes = Record("STDT", Subrecord("BNAM", "9E-41-C6-C1-69-22-C3-C1-2D-C0-86-40"));
        foreach (var star in ReadBoth<IStarGetter>(bytes, f => Star.CreateFromBinary(f), (s, p) => StarBinaryOverlay.StarFactory(s, p)))
        {
            var loc = star.SystemParsecLocation!.Value;
            loc.X.ShouldBe(-24.782f, 0.001f);
            loc.Y.ShouldBe(-24.392f, 0.001f);
            loc.Z.ShouldBe(4.211f, 0.001f);
            var written = GameConstants.Starfield.MajorRecord(Write(w => star.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("BNAM")).Content.ToArray().ShouldBe(Hex("9E-41-C6-C1-69-22-C3-C1-2D-C0-86-40"));
        }
    }

    // The orbited data component (BGSOrbitedDataComponent_Component) DATA per xEdit: Gravity Well (double), Mass (in SM),
    // Radius (in km), Surface Gravity (float). Mutagen had Unknown1 (UInt64) and Unknown2. Starfield.esm: on all 122 stars,
    // 20 bytes, Gravity Well 0 and Surface Gravity 1.0 on every one.
    // BohrStar (05E5BF:Starfield.esm): its component header and DATA
    private const string BohrOrbited = "00-00-00-00-00-00-00-00-79-DF-BA-71-70-18-1E-49-00-00-80-3F";

    [Fact]
    public void BohrStar_OrbitedData()
    {
        var bytes = Record("STDT",
            Subrecord("BFCB", System.Text.Encoding.ASCII.GetBytes("BGSOrbitedDataComponent_Component\0")),
            Subrecord("DATA", BohrOrbited),
            Subrecord("BFCE", ""));
        foreach (var star in ReadBoth<IStarGetter>(bytes, f => Star.CreateFromBinary(f), (s, p) => StarBinaryOverlay.StarFactory(s, p)))
        {
            var orbited = star.Components.OfType<IOrbitedDataComponentGetter>().Single();
            orbited.GravityWell.ShouldBe(0d);
            orbited.MassInSm.ShouldBe(0.93f, 0.001f);
            orbited.RadiusInKm.ShouldBe(647559f);
            orbited.SurfaceGravity.ShouldBe(1f);
            var written = GameConstants.Starfield.MajorRecord(Write(w => star.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(bytes.AsSpan(24).ToArray());
        }
    }
}
