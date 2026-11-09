using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A planet model's XMPM File Hash and Folder Hash and a planet's HNAM Random Seed are unsigned 32-bit values
// (xEdit's Starfield definitions: itU32). Int32 read the high half as negative, e.g. 850 of the 1,765 random seeds
// in Starfield.esm, and file hashes such as NavacciPlanetData's 0x8449235B. The component is also on 120 stars (STDT).
public class PlanetHashTests
{
    // NavacciPlanetData (03F59C:Starfield.esm): its planet model component and HNAM
    private const string ModelComponentName = "54-45-53-50-6C-61-6E-65-74-4D-6F-64-65-6C-5F-43-6F-6D-70-6F-6E-65-6E-74-00";
    private const string Modl = "44-61-74-61-2F-4D-65-73-68-65-73-2F-50-6C-61-6E-65-74-73-2F-48-6F-75-64-69-6E-69-50-6C-61-6E-65-74-73-2F-48-6F-75-64-69-6E-69-50-6C-61-6E-65-74-2E-6E-69-66-00";
    private const string Xmpm = "01-00-07-00-64-65-66-61-75-6C-74-01-00-0D-00-70-6C-61-6E-65-74-5F-6D-65-73-68-3A-30-5B-23-49-84-6D-61-74-00-18-5A-EC-23";
    private const string Hnam = "00-00-00-00-03-00-00-00-4B-35-00-08-00-00-00-47-6C-20-37-30-32-42-00-00-00-00-00-00-00-00-00-09-00-00-00-36-2E-30-37-45-2B-32-32-00-07-00-00-00-42-61-72-72-65-6E-00-09-00-00-00-4E-61-72-69-6F-6E-20-42-00-00-00-00-00-4C-A6-0A-46-44-28-05-41-F2-D2-4D-62-10-58-14-40-29-5C-2F-40-00-00-E0-40-00-00-A8-41-00-00-00-00-00-00-00-00-00-00-61-43-FE-FF-FF-FF-00-80-B0-43-00-00-00-00-00-00-00-00-00-00-00-00-B0-09-17-99-00-00-00-00";

    [Fact]
    public void Hashes_AndSeed_AreUnsigned()
    {
        var bytes = Record("PNDT",
            Subrecord("BFCB", ModelComponentName),
            Subrecord("MODL", Modl),
            Subrecord("FLLD", "01-00-00-00"),
            Subrecord("XMPM", Xmpm),
            Subrecord("BFCE", ""),
            Subrecord("HNAM", Hnam));
        foreach (var planet in ReadBoth<IPlanetGetter>(bytes, f => Planet.CreateFromBinary(f), (s, p) => PlanetBinaryOverlay.PlanetFactory(s, p)))
        {
            var item = planet.Components.OfType<IPlanetModelComponentGetter>().Single().XMPM!.UnknownSubItems.Single();
            item.ResourceID.ShouldBe("planet_mesh:0");
            item.FileHash.ShouldBe(0x8449235Bu);
            item.Extension.ShouldBe("mat");
            item.FolderHash.ShouldBe(0x23EC5A18u);
            planet.Details!.RandomSeed.ShouldBe(0x991709B0u);
            var written = GameConstants.Starfield.MajorRecord(Write(w => planet.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("XMPM")).Content.ToArray().ShouldBe(Hex(Xmpm));
            written.FindSubrecord(new RecordType("HNAM")).Content.ToArray().ShouldBe(Hex(Hnam));
        }
    }
}
