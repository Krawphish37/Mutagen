using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Planet NNAM (Player Knowledge) bit 31 had no name; xEdit calls it "Unknown 31". In Starfield.esm NNAM is on 932
// planets: 0 on 518, bit 31 alone on 412, bit 31 with Initial Scan, Visited and Entered System on VecteraPlanetData
// (03F59E:Starfield.esm) and with Entered System on _SEAC01OrbitalPlanetData (05E816:Starfield.esm). The enum becomes a uint so the bit fits.
// The test reads real NNAM values with both readers and writes them back byte for byte.
public class PlanetPlayerKnowledgeTests
{
    [Theory]
    // VecteraPlanetData (03F59E:Starfield.esm)
    [InlineData("07000080", Planet.PlayerKnowledgeFlag.InitialScan | Planet.PlayerKnowledgeFlag.Visited | Planet.PlayerKnowledgeFlag.EnteredSystem | Planet.PlayerKnowledgeFlag.Unknown31)]
    // BohrIIPlanetData (03F598:Starfield.esm) and 411 others
    [InlineData("00000080", Planet.PlayerKnowledgeFlag.Unknown31)]
    public void Unknown31(string nnam, Planet.PlayerKnowledgeFlag expected)
    {
        var bytes = Record("PNDT", Subrecord("NNAM", nnam));
        foreach (var planet in ReadBoth<IPlanetGetter>(bytes, f => Planet.CreateFromBinary(f), (s, p) => PlanetBinaryOverlay.PlanetFactory(s, p)))
        {
            planet.PlayerKnowledge.ShouldBe(expected);
            var written = GameConstants.Starfield.MajorRecord(Write(w => planet.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("NNAM")).Content.ToArray().ShouldBe(Hex(nnam));
        }
    }
}
