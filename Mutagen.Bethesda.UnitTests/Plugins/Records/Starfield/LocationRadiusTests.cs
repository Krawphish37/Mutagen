using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A Starfield location target's radius (PLDT on packages, PLVD on factions) is a float (xEdit's Starfield
// definitions). Starfield.esm has 2,957 non-zero package radii, e.g. 6.5 (bytes 00 00 D0 40), which a UInt32 read
// as 1,087,373,312.
public class LocationRadiusTests
{
    // PLDT of package 3E4F4F in Starfield.esm: type 6, target 3E4F4E, radius 6.5, collection index 0
    private const string Location = "06-00-00-00-4E-4F-3E-00-00-00-D0-40-00-00-00-00";

    [Fact]
    public void FactionVendorLocation_RadiusReadsAsFloat()
    {
        var bytes = Record("FACT", Subrecord("PLVD", Location));
        foreach (var faction in ReadBoth<IFactionGetter>(bytes, f => Faction.CreateFromBinary(f), (s, p) => FactionBinaryOverlay.FactionFactory(s, p)))
        {
            faction.VendorLocation!.Radius.ShouldBe(6.5f);
        }
    }

    [Fact]
    public void Package_RadiusWritesAndReadsBack()
    {
        var package = new Package(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield);
        package.Data.Add(0, new PackageDataLocation { Location = new LocationTargetRadius { Radius = 6.5f } });
        var bytes = Write(w => package.WriteToBinary(w));
        var pldt = GameConstants.Starfield.MajorRecord(bytes).FindSubrecord(new RecordType("PLDT")).Content;
        BitConverter.ToSingle(pldt.Slice(8, 4)).ShouldBe(6.5f);
        foreach (var read in ReadBoth<IPackageGetter>(bytes, f => Package.CreateFromBinary(f), (s, p) => PackageBinaryOverlay.PackageFactory(s, p)))
        {
            ((IPackageDataLocationGetter)read.Data[0]).Location.Radius.ShouldBe(6.5f);
        }
    }
}
