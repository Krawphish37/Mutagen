using System.Drawing;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Lighting template DATA in Starfield's layout (xEdit): Fog Color Far comes straight after Fog Power. Mutagen used
// Fallout 4's layout with a 32-byte Unused there, so every later field was misplaced (a colour read from the float 10.0,
// High Near Scale 1.0 as an Int32). All 6 LGTM in Starfield.esm are 108 bytes, so both layouts round-trip.
public class LightingTemplateLayoutTests
{
    // DATA of LGTM 017588
    private const string Data =
        "00-00-00-00-00-00-00-00-06-1B-1D-00-00-00-70-41-00-00-02-44-00-00-00-00-00-00-00-00-00-00-80-3F-00-00-00-00-9A-99-99-3F-46-46-46-00-CD-CC-4C-3E-00-00-00-00-00-00-00-00-9F-07-00-00-00-00-20-C2-00-00-70-41-D8-E4-E9-00-57-67-6C-00-CD-CC-CC-3D-CD-CC-4C-3E-CD-CC-CC-3E-CD-CC-CC-3D-9A-99-19-3E-00-00-A0-C1-00-00-20-41-00-EA-A7-4C";

    private static IEnumerable<ILightingTemplateGetter> ReadTemplate() =>
        ReadBoth<ILightingTemplateGetter>(Record("LGTM", Subrecord("DATA", Data)), f => LightingTemplate.CreateFromBinary(f), (s, p) => LightingTemplateBinaryOverlay.LightingTemplateFactory(s, p));

    private static int Rgb(Color c) => c.R << 16 | c.G << 8 | c.B;

    [Fact]
    public void Fields_ReadWhereXEditHasThem()
    {
        foreach (var t in ReadTemplate())
        {
            t.FogPower.ShouldBe(1.2f, 1e-6f);
            Rgb(t.FogFarColor).ShouldBe(70 << 16 | 70 << 8 | 70);
            t.FogMax.ShouldBe(0.2f, 1e-6f);
            t.LightFadeStartDistance.ShouldBe(0f);
            t.LightFadeEndDistance.ShouldBe(0f);
            t.Unknown.ShouldBe(0x79F);
            t.NearHeightMid.ShouldBe(-40f);
            t.NearHeightRange.ShouldBe(15f);
            Rgb(t.FogColorHighNear).ShouldBe(216 << 16 | 228 << 8 | 233);
            Rgb(t.FogColorHighFar).ShouldBe(87 << 16 | 103 << 8 | 108);
            t.HighDensityScale.ShouldBe(0.1f, 1e-6f);
            t.FogNearScale.ShouldBe(0.2f, 1e-6f);
            t.FogFarScale.ShouldBe(0.4f, 1e-6f);
            t.HighNearScale.ShouldBe(0.1f, 1e-6f);
            t.HighFarScale.ShouldBe(0.15f, 1e-6f);
            t.FarHeightMid.ShouldBe(-20f);
            t.FarHeightRange.ShouldBe(10f);
            t.Unknown2.ToArray().ShouldBe(new byte[] { 0x00, 0xEA, 0xA7, 0x4C });
        }
    }

    [Fact]
    public void Data_WritesSameBytes()
    {
        foreach (var t in ReadTemplate())
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => t.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("DATA")).Content.ToArray().ShouldBe(Hex(Data));
        }
    }
}
