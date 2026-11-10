using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// The worldspace overlay component's SNAM and PNAM were raw bytes. xEdit: SNAM Star System ID and PNAM Planet ID,
// both s32 (the star system ID is a star's ID, not a FormID). In Starfield.esm the component is on 430 worldspaces
// and both are always 4 bytes: SNAM -1 on 206, 0 on 177, star IDs such as 77022 on the rest; PNAM -1 on 206, 0 on
// 153, 1 to 18 on the rest.
// Each test reads the component subrecords and bounds (always written) of a real worldspace with both readers and writes them back byte for byte.
public class WorldspaceOverlayIdTests
{
    private static IEnumerable<IWorldspaceGetter> Read(byte[] bytes)
        => ReadBoth<IWorldspaceGetter>(bytes, f => Worldspace.CreateFromBinary(f), (s, p) => WorldspaceBinaryOverlay.WorldspaceFactory(s, p));

    private static void WritesBack(IWorldspaceGetter world, byte[] bytes)
    {
        var written = Write(w => world.WriteToBinary(w));
        GameConstants.Starfield.MajorRecord(written).Content.ToArray()
            .ShouldBe(GameConstants.Starfield.MajorRecord(bytes).Content.ToArray());
    }

    private static byte[] Overlay(string snam, string pnam, string bnam, string nam0, string nam9)
        => Record("WRLD",
            Subrecord("BFCB", "424753576F726C6453706163654F7665726C61795F436F6D706F6E656E7400"),
            Subrecord("SNAM", snam),
            Subrecord("PNAM", pnam),
            Subrecord("BNAM", bnam),
            Subrecord("BFCE", ""),
            Subrecord("NAM0", nam0),
            Subrecord("NAM9", nam9));

    // LC030World (260A7C:Starfield.esm): star system 77022, planet 1, surface block OverlayBlockLC030World (260A7A:Starfield.esm)
    [Fact]
    public void StarSystemAndPlanet()
    {
        var bytes = Overlay("DE2C0100", "01000000", "7A0A2600", "0000C8C30000C8C3", "0000C8430000FA43");
        foreach (var world in Read(bytes))
        {
            var overlay = world.Components.OfType<IWorldSpaceOverlayComponentGetter>().Single();
            overlay.StarSystemId.ShouldBe(77022);
            overlay.PlanetId.ShouldBe(1);
            overlay.SurfaceBlock.FormKey.ID.ShouldBe(0x260A7Au);
            WritesBack(world, bytes);
        }
    }

    // OverlayPOIBiomeDenVolcanicRough01 (002D62:Starfield.esm): no star system or planet (-1),
    // surface block OverlayBlockOverlayPOIBiomeDenVolcanicRough01 (002FA1:Starfield.esm)
    [Fact]
    public void None()
    {
        var bytes = Overlay("FFFFFFFF", "FFFFFFFF", "A12F0000", "000048C3000048C3", "0000964300009643");
        foreach (var world in Read(bytes))
        {
            var overlay = world.Components.OfType<IWorldSpaceOverlayComponentGetter>().Single();
            overlay.StarSystemId.ShouldBe(-1);
            overlay.PlanetId.ShouldBe(-1);
            WritesBack(world, bytes);
        }
    }
}
