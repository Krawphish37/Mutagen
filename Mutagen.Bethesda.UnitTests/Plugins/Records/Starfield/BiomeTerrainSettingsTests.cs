using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// BIOM BTPS was raw bytes (the field "BTPS"); it is xEdit's Terrain Settings: seven blocks (Base, Flow, FlatOuter,
// FlatInner, Talus, Solid, Path) of eight floats, read into the BiomeBTPS struct that already existed.
// BIOM BDFS (DistantView) was raw bytes too; it is read into the BiomeDistantView struct (eleven floats).
// The test reads a whole record from Starfield.esm with both readers and writes it back byte for byte.
public class BiomeTerrainSettingsTests
{
    // Gas_Giant_Template (17B861:Starfield.esm)
    private const string GasGiant = "4544494413004761735F4769616E745F54656D706C617465004D4E414D0100014A4E414D010000574E414D04000000803F594E414D04000000803F554E414D04000000803F4E4E414D04007EDE2300434E414D0400000000FF424D433104009EA2B5FF424D433204007F6B61FF424D43330400000000FF544E414D040006000000545831364300446174615C54657874757265735C6C616E6473636170655C67726F756E645C726F636B5C726F636B6772616E69746562726F6B656E30315F6E6F726D616C2E6464730042545053E0007D3F553F7D3F553F0000803F0000803F000000000000803F000000000000803F83C02A3F83C02A3F0000803F0000803F000000000000803F000000000000803F0000003F0000003F0000803F0000803F000000000000803F000000000000803FFA7EAA3EFA7EAA3E0000803F0000803F000000000000803F000000000000803F0C022B3E0C022B3E0000803F0000803F000000000000803F000000000000803F00000000000000000000803F0000803F000000000000803F000000000000803F0000000000000000000000000000000000000000000000000000000000000000424446532C00CDCC4C3ECDCCCC3DCCCC4C3BCCCC4C3B000000000000803F000000000000803F9A99193FCDCCCC3D0000803E4E414D3204003BDD2E004E414D3304003BDD2E004E414D3404003BDD2E00";

    [Fact]
    public void TerrainSettingsAndDistantView_Decoded()
    {
        var bytes = Record("BIOM", Hex(GasGiant));
        foreach (var biom in ReadBoth<IBiomeGetter>(bytes,
                     f => Biome.CreateFromBinary(f),
                     (s, p) => BiomeBinaryOverlay.BiomeFactory(s, p)))
        {
            biom.EditorID.ShouldBe("Gas_Giant_Template");

            var ts = biom.TerrainSettings;
            ts.Base.NoiseOffsetU.ShouldBe(0.833f);
            ts.Base.NoiseOffsetV.ShouldBe(0.833f);
            ts.Base.NoiseScaleU.ShouldBe(1f);
            ts.Base.NoiseScaleV.ShouldBe(1f);
            ts.Base.NoiseContrast.ShouldBe(0f);
            ts.Base.AltTextureCoverage.ShouldBe(1f);
            ts.Base.AlternateDataContributionMin.ShouldBe(0f);
            ts.Base.AlternateDataContributionMax.ShouldBe(1f);
            ts.Flow.NoiseOffsetU.ShouldBe(0.667f);
            ts.FlatOuter.NoiseOffsetV.ShouldBe(0.5f);
            ts.FlatInner.NoiseOffsetU.ShouldBe(0.333f);
            ts.Talus.NoiseOffsetU.ShouldBe(0.167f);
            ts.Solid.NoiseOffsetU.ShouldBe(0f);
            ts.Solid.NoiseScaleU.ShouldBe(1f);
            ts.Path.NoiseScaleU.ShouldBe(0f);
            ts.Path.AlternateDataContributionMax.ShouldBe(0f);

            var dv = biom.DistantView;
            dv.TextureOffsetU.ShouldBe(0.2f);
            dv.TextureOffsetV.ShouldBe(0.1f);
            dv.TextureScaleU.ShouldBe(0.003125f, 1e-9);
            dv.TextureScaleV.ShouldBe(0.003125f, 1e-9);
            dv.Unknown1.ShouldBe(0f);
            dv.Unknown2.ShouldBe(1f);
            dv.Unknown3.ShouldBe(0f);
            dv.Unknown4.ShouldBe(1f);
            dv.NormalStrength.ShouldBe(0.6f);
            dv.SlopeStart.ShouldBe(0.1f);
            dv.SlopeRange.ShouldBe(0.25f);

            var written = GameConstants.Starfield.MajorRecord(Write(w => biom.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(GasGiant));
        }
    }
}
