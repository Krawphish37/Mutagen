using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// ASPC ASDF is an 8-byte set of disable flags (xEdit: Environment Type 0x01 ... Affects Parented Sound Markers 0x80).
// Mutagen read it as the SoundLevel enum, so a value like 0xF7 had no name. All 322 ASDF in Starfield.esm use only
// these 8 bits.
public class AcousticSpaceDisableFlagTests
{
    // ASDF of Ext_City_Cydonia_FrackingContainment (ASPC 3EB8F9): every flag but Looping Sound
    private const string CydoniaFlags = "F7-00-00-00-00-00-00-00";
    // ASDF of ASPC 3FA24B: Looping Sound, Exterior/Weather Attenuation, Crowd Walla
    private const string LoopingFlags = "58-00-00-00-00-00-00-00";

    private static IEnumerable<IAcousticSpaceGetter> ReadAcousticSpace(string asdf) =>
        ReadBoth<IAcousticSpaceGetter>(Record("ASPC", Subrecord("ASDF", asdf)), f => AcousticSpace.CreateFromBinary(f), (s, p) => AcousticSpaceBinaryOverlay.AcousticSpaceFactory(s, p));

    [Fact]
    public void AllButLooping_Reads()
    {
        foreach (var space in ReadAcousticSpace(CydoniaFlags))
        {
            space.DisableFlags.ShouldBe(
                AcousticSpace.DisableFlag.EnvironmentType | AcousticSpace.DisableFlag.MusicType
                | AcousticSpace.DisableFlag.SoundDetectionLevel | AcousticSpace.DisableFlag.ExteriorWeatherAttenuation
                | AcousticSpace.DisableFlag.IsInterior | AcousticSpace.DisableFlag.CrowdWalla
                | AcousticSpace.DisableFlag.AffectsParentedSoundMarkers);
        }
    }

    [Fact]
    public void Looping_Reads()
    {
        foreach (var space in ReadAcousticSpace(LoopingFlags))
        {
            var flags = space.DisableFlags!.Value;
            flags.HasFlag(AcousticSpace.DisableFlag.LoopingSound).ShouldBeTrue();
            flags.HasFlag(AcousticSpace.DisableFlag.ExteriorWeatherAttenuation).ShouldBeTrue();
            flags.HasFlag(AcousticSpace.DisableFlag.CrowdWalla).ShouldBeTrue();
            flags.HasFlag(AcousticSpace.DisableFlag.IsInterior).ShouldBeFalse();
        }
    }

    [Fact]
    public void Flags_Write()
    {
        var space = new AcousticSpace(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            DisableFlags = AcousticSpace.DisableFlag.LoopingSound | AcousticSpace.DisableFlag.ExteriorWeatherAttenuation | AcousticSpace.DisableFlag.CrowdWalla,
        };
        var written = GameConstants.Starfield.MajorRecord(Write(w => space.WriteToBinary(w)));
        written.FindSubrecord(new RecordType("ASDF")).Content.ToArray().ShouldBe(Hex(LoopingFlags));
    }
}
