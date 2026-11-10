using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
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

    // AlgorabIIPlanetData (05DE57:Starfield.esm) as overridden in SFBGS050.esm: one EOVR entry, Type 1 (Added).
    // xEdit EOVR 'Edited Worldspaces': Position, Worldspace, Type (u8 enum: 0 Removed, 1 Added). All 8 entries in the
    // official plugins are 1; the Removed case is the same entry with the last byte 0.
    private const string Eovr = "D52C2BAFCFEFDC3F1EE3373B20970540F4B2020201";

    [Theory]
    [InlineData(Eovr, AddedWorldspace.ChangeType.Added)]
    [InlineData("D52C2BAFCFEFDC3F1EE3373B20970540F4B2020200", AddedWorldspace.ChangeType.Removed)]
    public void AddedWorldspace_Type(string eovr, AddedWorldspace.ChangeType expected)
    {
        // SFBGS050.esm masters: Starfield.esm, SFBGS00D.esm; worldspace 0202B2F4 is its own (02B2F4:SFBGS050.esm)
        var self = ModKey.FromNameAndExtension("SFBGS050.esm");
        var masters = new MasterReferenceCollection(self, new[]
        {
            new MasterReference { Master = ModKey.FromNameAndExtension("Starfield.esm") },
            new MasterReference { Master = ModKey.FromNameAndExtension("SFBGS00D.esm") },
        });
        var separated = SeparatedMasterPackage.NotSeparate(masters);
        ParsingMeta Meta() => new(GameConstants.Starfield, self, separated);

        var bytes = Record("PNDT", Subrecord("EOVR", eovr));
        var readers = new IPlanetGetter[]
        {
            Planet.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta()))),
            PlanetBinaryOverlay.PlanetFactory(new OverlayStream(bytes, Meta()), new BinaryOverlayFactoryPackage(Meta())),
        };
        foreach (var planet in readers)
        {
            var world = planet.AddedWorldspaces.ShouldNotBeNull().Single();
            world.Type.ShouldBe(expected);
            world.Worldspace.FormKey.ShouldBe(new FormKey(self, 0x02B2F4));
            var stream = new MemoryStream();
            using (var writer = new MutagenWriter(stream, new WritingBundle(GameConstants.Starfield)
                   {
                       MasterReferences = masters,
                       SeparatedMasterPackage = separated,
                   }, dispose: false))
            {
                planet.WriteToBinary(writer);
            }
            var written = GameConstants.Starfield.MajorRecord(stream.ToArray());
            written.FindSubrecord(new RecordType("EOVR")).Content.ToArray().ShouldBe(Hex(eovr));
        }
    }
}
