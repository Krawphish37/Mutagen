using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield light flags (DAT2) per xEdit's Starfield definitions: Off by Default 0x10, Disable Specular 0x20, Disable
// Distance Attenuation 0x40, Is Direct Spotlight 0x80, Use PBR Value 0x100, Focus Spotlight Beam 0x200, Externally
// Controlled 0x1000. In Starfield.esm 0x200 is set on 187 lights, 181 of them named "...Spot...".
public class LightFlagTests
{
    // DAT2 of LGT_Generic_DSpot_DS_01K_DirectSpot (LIGH 2CF376): flags 0x02C1
    private const string DirectSpotData =
        "FF-FF-FF-FF-00-00-80-3F-FF-FE-FB-00-C1-02-00-00-00-00-80-3F-50-61-1D-43-0A-D7-23-3C-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-50-14-00-00-E8-03-00-00-01-00-00-00-00-00-00-00-00-00-80-C0-00-00-60-41-00-00-80-40";
    // DAT2 of LIGH 01ED2D: flags 0x0051
    private const string OffByDefaultData =
        "FF-FF-FF-FF-00-00-C8-41-E1-EF-F7-00-51-00-00-00-C3-F5-A8-3E-00-00-48-42-CD-CC-4C-3E-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-04-42-00-00-00-00-E8-03-00-00-01-00-01-00-00-00-00-00-00-00-40-40-00-00-10-41-00-00-80-40";

    private static IEnumerable<ILightGetter> ReadLight(string dat2) =>
        ReadBoth<ILightGetter>(Record("LIGH", Subrecord("DAT2", dat2)), f => Light.CreateFromBinary(f), (s, p) => LightBinaryOverlay.LightFactory(s, p));

    [Fact]
    public void DirectSpotlight_Reads()
    {
        foreach (var light in ReadLight(DirectSpotData))
        {
            var flags = light.Flags;
            flags.HasFlag(Light.Flag.DisableDistanceAttenuation).ShouldBeTrue();
            flags.HasFlag(Light.Flag.IsDirectSpotlight).ShouldBeTrue();
            flags.HasFlag(Light.Flag.FocusSpotlightBeam).ShouldBeTrue();
            flags.HasFlag(Light.Flag.UsePbrValue).ShouldBeFalse();
            flags.HasFlag(Light.Flag.OffByDefault).ShouldBeFalse();
        }
    }

    [Fact]
    public void OffByDefault_Reads()
    {
        foreach (var light in ReadLight(OffByDefaultData))
        {
            light.Flags.HasFlag(Light.Flag.OffByDefault).ShouldBeTrue();
            light.Flags.HasFlag(Light.Flag.DisableDistanceAttenuation).ShouldBeTrue();
            light.Flags.HasFlag(Light.Flag.DisableSpecular).ShouldBeFalse();
        }
    }

    [Theory]
    [InlineData(Light.Flag.OffByDefault, 0x10u)]
    [InlineData(Light.Flag.FocusSpotlightBeam, 0x200u)]
    [InlineData(Light.Flag.ExternallyControlled, 0x1000u)]
    public void Flag_Writes(Light.Flag flag, uint expected)
    {
        var light = new Light(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield) { Flags = flag };
        var written = GameConstants.Starfield.MajorRecord(Write(w => light.WriteToBinary(w)));
        BitConverter.ToUInt32(written.FindSubrecord(new RecordType("DAT2")).Content.Slice(12, 4)).ShouldBe(expected);
    }
}
