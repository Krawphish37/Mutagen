using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C42: resource rarity 5 and 6 were UniqueToHe3 and UniqueToH20; xEdit has Special and Everywhere. In Starfield.esm
// 5 is only on ResInorgCommonHelium3_G (0057F5:Starfield.esm), and 6 is on ResInorgCommonWater_L (0083EC:Starfield.esm)
// and on four primers that aren't water at all: ResSmallPrimer (2BC040:Starfield.esm), ResLargePrimer
// (2BC057:Starfield.esm), ResLaserPrimer (2C0D38:Starfield.esm) and ResMagneticPrimer (2C0D3D:Starfield.esm).
public class ResourceRarityTests
{
    [Theory]
    [InlineData("05000000", Resource.RarityEnum.Special)]    // Helium-3
    [InlineData("06000000", Resource.RarityEnum.Everywhere)] // Water, the primers
    [InlineData("04000000", Resource.RarityEnum.Unique)]
    public void Rarity(string snam, Resource.RarityEnum expected)
    {
        var bytes = Record("IRES", Subrecord("SNAM", snam));
        foreach (var res in ReadBoth<IResourceGetter>(bytes, f => Resource.CreateFromBinary(f), (s, p) => ResourceBinaryOverlay.ResourceFactory(s, p)))
        {
            res.Rarity.ShouldBe(expected);
            var written = GameConstants.Starfield.MajorRecord(Write(w => res.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("SNAM")).Content.ToArray().ShouldBe(Hex(snam));
        }
    }
}
