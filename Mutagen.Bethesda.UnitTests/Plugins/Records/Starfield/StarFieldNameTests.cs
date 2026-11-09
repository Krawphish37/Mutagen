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
}
