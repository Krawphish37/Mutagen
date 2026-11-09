using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Sound echo marker fields were raw (ANAM, CNAM, DNAM, ENAM as bytes) or named after their signatures (ECTE, BNAM).
// xEdit: ECTE Wwise GUID, ANAM Voice Count (u32), BNAM Duration (float), CNAM Action At Limit (u8: Steal, Skip),
// DNAM Use (u8: On Each Playback, On Limit Reached), ENAM Trigger Release (u8 bool).
// Starfield.esm, 96 markers: ANAM always 4 bytes (0, 1, 3, 6); CNAM 0 or 1; DNAM and ENAM always 0.
public class SoundEchoMarkerTests
{
    // AMB_Cydonia_Fracking_Rattles_Chair (1A0DB6:Starfield.esm): two echoes (3 voices, 120 s, Skip) and the default start marker
    private const string FrackingRattles = "454449442300414D425F4379646F6E69615F467261636B696E675F526174746C65735F4368616972004F424E44180000001ABF00000DBF00001ABF00001A3F0040383F00001A3F4F4454590400000000004543484F0000454354451000344A8B65D8335C5B1C93B2EC8ED881854543534828005B46FAF6A8A645BE0764E582849069B8000000000000000000000000000000000000000000000000414E414D040003000000424E414D04000000F042434E414D010001444E414D010000454E414D0100004543484500004543484F0000454354451000CE4A1E36CD330F272673CB36FD6616AE4543534828005B46FAF6A8A645BE0764E582849069B8000000000000000000000000000000000000000000000000414E414D040003000000424E414D04000000F042434E414D010001444E414D010000454E414D010000454348450000454348440000414E414D040000000000424E414D040000000000434E414D010000444E414D010000454E414D010000454348450000";

    [Fact]
    public void EchoFields_NamedAndTyped()
    {
        foreach (var sech in ReadBoth<ISoundEchoMarkerGetter>(Record("SECH", Hex(FrackingRattles)), f => SoundEchoMarker.CreateFromBinary(f), (s, p) => SoundEchoMarkerBinaryOverlay.SoundEchoMarkerFactory(s, p)))
        {
            sech.Markers.Count.ShouldBe(2);
            foreach (var echo in sech.Markers)
            {
                echo.VoiceCount.ShouldBe(3u);
                echo.Duration.ShouldBe(120f);
                echo.ActionAtLimit.ShouldBe(EchoMarker.ActionAtLimitType.Skip);
                echo.Use.ShouldBe(EchoMarker.UseType.OnEachPlayback);
                echo.TriggerRelease.ShouldBe(false);
                echo.WwiseGuid.ShouldNotBeNull();
            }
            var start = sech.DefaultStartMarker!;
            start.VoiceCount.ShouldBe(0u);
            start.Duration.ShouldBe(0f);
            start.ActionAtLimit.ShouldBe(EchoMarker.ActionAtLimitType.Steal);

            var written = GameConstants.Starfield.MajorRecord(Write(w => sech.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(FrackingRattles));
        }
    }

    [Fact]
    public void EchoFields_Set_WriteSameBytes()
    {
        var echo = new EchoMarker
        {
            VoiceCount = 6,
            Duration = 4f,
            ActionAtLimit = EchoMarker.ActionAtLimitType.Skip,
            Use = EchoMarker.UseType.OnLimitReached,
            TriggerRelease = true,
        };
        var sech = new SoundEchoMarker(new Mutagen.Bethesda.Plugins.FormKey(TestModKey, 0x800), StarfieldRelease.Starfield);
        sech.Markers.Add(echo);
        var written = GameConstants.Starfield.MajorRecord(Write(w => ((ISoundEchoMarkerGetter)sech).WriteToBinary(w)));
        written.FindSubrecord(new Mutagen.Bethesda.Plugins.RecordType("ANAM")).Content.ToArray().ShouldBe(Hex("06000000"));
        written.FindSubrecord(new Mutagen.Bethesda.Plugins.RecordType("BNAM")).Content.ToArray().ShouldBe(Hex("00008040"));
        written.FindSubrecord(new Mutagen.Bethesda.Plugins.RecordType("CNAM")).Content.ToArray().ShouldBe(Hex("01"));
        written.FindSubrecord(new Mutagen.Bethesda.Plugins.RecordType("DNAM")).Content.ToArray().ShouldBe(Hex("01"));
        written.FindSubrecord(new Mutagen.Bethesda.Plugins.RecordType("ENAM")).Content.ToArray().ShouldBe(Hex("01"));
    }
}
