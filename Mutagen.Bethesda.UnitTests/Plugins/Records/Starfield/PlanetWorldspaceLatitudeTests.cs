using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A planet's master worldspaces (CNAM) store latitude, then longitude (xEdit: Position = Latitude, Longitude, in
// radians); Mutagen had the names the other way round. In Starfield.esm the first double is within +-pi/2 on all 72
// entries and the second is beyond +-pi/2 on 41 of them, so only the first can be a latitude.
// AddedWorldspace (EOVR) has the same layout in xEdit and gets the same order (no EOVR in Starfield.esm).
public class PlanetWorldspaceLatitudeTests
{
    // VecteraPlanetData (03F59E:Starfield.esm): lc001world (208D92:Starfield.esm)
    private const string Cnam = "F97C6CA15B2DD1BF39142B22D2E802C0928D2000";

    [Fact]
    public void MasterWorldspace_LatitudeFirst()
    {
        var bytes = Record("PNDT", Subrecord("CNAM", Cnam));
        foreach (var planet in ReadBoth<IPlanetGetter>(bytes, f => Planet.CreateFromBinary(f), (s, p) => PlanetBinaryOverlay.PlanetFactory(s, p)))
        {
            var world = planet.MasterWorldspaces.ShouldNotBeNull().Single();
            world.Latitude.ShouldBe(BitConverter.ToDouble(Hex("F97C6CA15B2DD1BF")));
            world.Longitude.ShouldBe(BitConverter.ToDouble(Hex("39142B22D2E802C0")));
            Math.Abs(world.Latitude).ShouldBeLessThan(Math.PI / 2);
            Math.Abs(world.Longitude).ShouldBeGreaterThan(Math.PI / 2);
            world.Worldspace.FormKey.ID.ShouldBe(0x208D92u);
            var written = GameConstants.Starfield.MajorRecord(Write(w => planet.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("CNAM")).Content.ToArray().ShouldBe(Hex(Cnam));
        }
    }
}
