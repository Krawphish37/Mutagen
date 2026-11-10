using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Planet biome (PPBD) fields:
// - ResourceGeneration linked to any record; xEdit: [NULL, RSGD]. In Starfield.esm it is null on 3,181 of 3,192
//   biomes and a ResourceGenerationData on the other 11.
// - The trailing items were four Int32 each; they are two doubles. Only one biome in Starfield.esm has items (10):
//   the first double is always within +-pi, the second within +-pi/2; read as Int32 they were halves of doubles.
// The test reads a real PPBD with both readers and writes it back byte for byte.
public class PlanetBiomeFieldTests
{
    // SchrodingerIIPlanetData (05E0B5:Starfield.esm), its biome CanyonsNoLife02 (216D45:Starfield.esm) with resource
    // generation UniqueTablelandsCanyonsAldumiteRes (18CE03:Starfield.esm) and 10 items
    private const string Ppbd = "456D21000000C8414BF6285C03CE1800000000000000000000000000090000000A0000008C3266F911E4FF3F2E67B936C01BE2BFC72E72808F8AEDBF2A3179FD2459F6BFA54C30BF2E8904C071C6D124C355F23F4F29F40B6CE00140E5DE3CA6EB29EC3FD48DAAAE158F01C0F0D970BD24A3ECBF1DECFE31A929E93FF04A84987CA0E03F53678DF6065D074054FF1F289432DEBF48A6147A5C9A0040D914EBEA323DCE3F05B64338D02207C0B6FD3F790F4FF03F16B0947B239BB53F9311697491D6F6BF";

    [Fact]
    public void ResourceGenerationAndItems()
    {
        var bytes = Record("PNDT", Subrecord("PPBD", Ppbd));
        foreach (var planet in ReadBoth<IPlanetGetter>(bytes, f => Planet.CreateFromBinary(f), (s, p) => PlanetBinaryOverlay.PlanetFactory(s, p)))
        {
            var biome = planet.Biomes.Single();
            biome.Biome.FormKey.ID.ShouldBe(0x216D45u);
            biome.Chance.ShouldBe(25f);
            biome.ResourceGeneration.FormKey.ID.ShouldBe(0x18CE03u);
            biome.ResourceGeneration.ShouldBeAssignableTo<IFormLinkGetter<IResourceGenerationDataGetter>>();
            biome.UnknownItems.Count.ShouldBe(10);
            biome.UnknownItems[0].Longitude.ShouldBe(1.9931812040478318);
            biome.UnknownItems[0].Latitude.ShouldBe(-0.5658875531036978);
            biome.UnknownItems[9].Longitude.ShouldBe(0.0843984772406546);
            biome.UnknownItems[9].Latitude.ShouldBe(-1.4273848101885036);
            biome.UnknownItems.ShouldAllBe(i => Math.Abs(i.Longitude) <= Math.PI && Math.Abs(i.Latitude) <= Math.PI / 2);
            var written = GameConstants.Starfield.MajorRecord(Write(w => planet.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("PPBD")).Content.ToArray().ShouldBe(Hex(Ppbd));
        }
    }
}
