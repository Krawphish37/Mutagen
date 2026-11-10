using System.Drawing;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Water DNAM (152 bytes in all 15 waters of Starfield.esm) was kept as raw bytes. It is xEdit's Visual Data:
// 37 floats and one byte colour (the underwater fog colour; its 4th byte is 0 in all 15).
// The FNAM flags get xEdit's bits 3 (Enable Flowmap) and 4 (Blend Normals); Starfield.esm only uses bit 0.
// Each test reads a whole water from Starfield.esm (FULL left out: it is a localized string ID) with both
// readers and writes it back byte for byte.
public class WaterVisualDataTests
{
    private static IEnumerable<IWaterGetter> Read(string hex)
        => ReadBoth<IWaterGetter>(Record("WATR", Hex(hex)), f => Water.CreateFromBinary(f), (s, p) => WaterBinaryOverlay.WaterFactory(s, p));

    private static void WritesBack(IWaterGetter water, string hex)
    {
        var written = GameConstants.Starfield.MajorRecord(Write(w => water.WriteToBinary(w)));
        written.Content.ToArray().ShouldBe(Hex(hex));
    }

    // WaterSulfuric (0017E9:Starfield.esm)
    private const string Sulfuric = "454449440E00576174657253756C667572696300414E414D01005D464E414D010000574153482800000000000000000000000000000000000000000000000000000000000000000000000000FBEB1F00584E414D0400BF4F0200594E414D04004F761F00444154410000444E414D9800CCCC5C409A99993E9A99993D0AD7233C00000000B4C89A410000A041000000008C8D2500CDCC4C3F000080BF000048440000803F62BC7F3FB1DE643F37557F3FCDCCCC3E0000003F9A99793F0000803FCDCC4C3D0000204200009C4200003243E3A59B3C3108AC3C0AD7A33CAA60143E32C4B13C7346943C008059440000114300000000BC7453452A18A545000000460000803FCDCC4C3D474E414D0C000000000000000000000000004E414D300C000AD7A33D0AD7A3BC000000004E414D310C00000000000000000000000000454E414D0400C53B2C00484E414D0400C43B2C004A4E414D0400C83B2C004C4E414D0400C73B2C004D4E414D0400C63B2C00514E414D040064991000554E414D0400CD3B2C00";

    // WaterLC165_MasadaIII_Corrosive (0FE097:Starfield.esm)
    private const string Corrosive = "454449441F0057617465724C433136355F4D61736164614949495F436F72726F7369766500414E414D010050464E414D010001574153482800000000000000000000000000000000000000000000000000000000000000000000000000FBEB1F00594E414D0400D66D1600444154410000444E414D980000004841CE8D293EF418C53DF8339C3DD6A32441E7FB5941333363400000000050584E000000803F000016C300009642BF0E0C3F0000803F9A99793F81767F3FCDCCCC3E0000003F9A99793F0000803FCDCC4C3D0000204200009C4200003243E3A59B3C3108AC3C0AD7A33C143F663F0000803FDF4F1D3F663A904200001C42000050410000C8420000C8420000C8420000803FCDCCCC3D474E414D0C000000000000000000000000004E414D300C000000000000000000000000004E414D310C00000000000000000000000000454E414D0400EDE31800484E414D0400EFE318004A4E414D0400EEE318004C4E414D0400F0E318004D4E414D0400D2E31800514E414D0400D1E31800554E414D0400D0E31800";

    [Fact]
    public void Sulfuric_VisualData()
    {
        foreach (var water in Read(Sulfuric))
        {
            water.EditorID.ShouldBe("WaterSulfuric");
            water.Flags.ShouldBe((Water.Flag)0);
            // WaterRadiationDrinking_OBSOLETE (024FBF:Starfield.esm)
            water.ConsumeSpell.FormKey.ID.ShouldBe(0x024FBFu);
            water.DepthAmount.ShouldBe(3.45f, 0.0001f);
            water.AbsorptionRangeRed.ShouldBe(0.3f, 0.0001f);
            water.AbsorptionRangeGreen.ShouldBe(0.075f, 0.0001f);
            water.AbsorptionRangeBlue.ShouldBe(0.01f, 0.0001f);
            water.Phytoplankton.ShouldBe(0f);
            water.Sediment.ShouldBe(19.348f, 0.0001f);
            water.YellowMatter.ShouldBe(20f);
            water.Oceanness.ShouldBe(0f);
            water.FogUnderwaterColor.ShouldBe(Color.FromArgb(0, 0x8C, 0x8D, 0x25));
            water.FogUnderwaterAmount.ShouldBe(0.8f, 0.0001f);
            water.FogUnderwaterNear.ShouldBe(-1f);
            water.FogUnderwaterFar.ShouldBe(800f);
            water.PhysicalNormalMagnitude.ShouldBe(1f);
            water.PhysicalShallowNormalFalloff.ShouldBe(0.999f, 0.001f);
            water.PhysicalDeepNormalFalloff.ShouldBe(0.894f, 0.001f);
            water.PhysicalSurfaceEffectFalloff.ShouldBe(0.9974f, 0.0001f);
            water.PhysicalDisplacementSimulatorForce.ShouldBe(0.4f, 0.0001f);
            water.PhysicalDisplacementSimulatorVelocity.ShouldBe(0.5f);
            water.PhysicalDisplacementSimulatorFalloff.ShouldBe(0.975f, 0.0001f);
            water.PhysicalDisplacementSimulatorDampener.ShouldBe(1f);
            water.PhysicalDisplacementSimulatorStartingSize.ShouldBe(0.05f, 0.0001f);
            water.NoiseLayerOneWindDirection.ShouldBe(40f);
            water.NoiseLayerTwoWindDirection.ShouldBe(78f);
            water.NoiseLayerThreeWindDirection.ShouldBe(178f);
            water.NoiseLayerOneWindSpeed.ShouldBe(0.019f, 0.0001f);
            water.NoiseLayerTwoWindSpeed.ShouldBe(0.021f, 0.0001f);
            water.NoiseLayerThreeWindSpeed.ShouldBe(0.02f, 0.0001f);
            water.NoiseLayerOneAmplitudeScale.ShouldBe(0.1449f, 0.0001f);
            water.NoiseLayerTwoAmplitudeScale.ShouldBe(0.0217f, 0.0001f);
            water.NoiseLayerThreeAmplitudeScale.ShouldBe(0.0181f, 0.0001f);
            water.NoiseLayerOneUvScale.ShouldBe(870f);
            water.NoiseLayerTwoUvScale.ShouldBe(145f);
            water.NoiseLayerThreeUvScale.ShouldBe(0f);
            water.NoiseLayerOneNoiseFalloff.ShouldBe(3383.2959f, 0.001f);
            water.NoiseLayerTwoNoiseFalloff.ShouldBe(5283.0205f, 0.001f);
            water.NoiseLayerThreeNoiseFalloff.ShouldBe(8192f);
            water.FlowmapScale.ShouldBe(1f);
            water.Roughness.ShouldBe(0.05f, 0.0001f);
            // Water_Absorption_River_New (2C3BC5:Starfield.esm)
            water.RiverAbsorptionCurve.FormKey.ID.ShouldBe(0x2C3BC5u);
            WritesBack(water, Sulfuric);
        }
    }

    [Fact]
    public void Corrosive_DangerousAndVisualData()
    {
        foreach (var water in Read(Corrosive))
        {
            water.Flags.ShouldBe(Water.Flag.Dangerous);
            water.Opacity.ShouldBe((byte)0x50);
            // ENV_DMG_Corrosive_CorrosiveLiquid_WaterContact_Spell_OBSOLETE (166DD6:Starfield.esm)
            water.ContactSpell.FormKey.ID.ShouldBe(0x166DD6u);
            water.DepthAmount.ShouldBe(12.5f);
            water.FogUnderwaterColor.ShouldBe(Color.FromArgb(0, 0x50, 0x58, 0x4E));
            water.FogUnderwaterNear.ShouldBe(-150f);
            water.FogUnderwaterFar.ShouldBe(75f);
            water.NoiseLayerThreeAmplitudeScale.ShouldBe(0.6145f, 0.0001f);
            water.NoiseLayerOneUvScale.ShouldBe(72.1141f, 0.0001f);
            water.NoiseLayerOneNoiseFalloff.ShouldBe(100f);
            water.Roughness.ShouldBe(0.1f, 0.0001f);
            WritesBack(water, Corrosive);
        }
    }

    [Fact]
    public void Flags_FlowmapAndBlendNormals()
    {
        ((int)Water.Flag.EnableFlowmap).ShouldBe(0x08);
        ((int)Water.Flag.BlendNormals).ShouldBe(0x10);
    }
}
