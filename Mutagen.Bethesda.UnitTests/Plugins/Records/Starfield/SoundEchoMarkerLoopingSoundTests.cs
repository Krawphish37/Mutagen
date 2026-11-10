using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// SECH ECHL is xEdit's "Looping Sound" (wbSoundReference). No official plugin sets it; the only one in the Data folder
// (fieldscan census of 370 plugins) is kgcDoom_OBJ_EmergencyAlarmSwitch_LP_2D_Play_ForceStop (0076F1:kgcdoom.esm).
// The test reads that record's subrecords with both readers and writes them back byte for byte. Its echo block
// (ECHD..ECHE) is left out: its condition points at the plugin's own FE/FD-style FormID, which this test's
// single-plugin master list can't express.
public class SoundEchoMarkerLoopingSoundTests
{
    // EDID, OBND, ODTY and ECHL of kgcDoom_OBJ_EmergencyAlarmSwitch_LP_2D_Play_ForceStop (0076F1:kgcdoom.esm)
    private const string Content =
        "4544494436006B6763446F6F6D5F4F424A5F456D657267656E6379416C61726D5377697463685F4C505F32445F506C61795F466F72636553746F7000" +
        "4F424E44180000001ABF00000DBF00001ABF00001A3F0040383F00001A3F" +
        "4F445459040000000000" +
        "4543484C28001F4A5B69D8967137033563200C8FD48650481A7805851613814A5886FDCFCF950000000000000000";

    [Fact]
    public void LoopingSound_ReadsAndWritesBack()
    {
        var echl = Hex("1F4A5B69D8967137033563200C8FD48650481A7805851613814A5886FDCFCF950000000000000000");
        foreach (var sech in ReadBoth<ISoundEchoMarkerGetter>(Record("SECH", Hex(Content)), f => SoundEchoMarker.CreateFromBinary(f), (s, p) => SoundEchoMarkerBinaryOverlay.SoundEchoMarkerFactory(s, p)))
        {
            sech.EditorID.ShouldBe("kgcDoom_OBJ_EmergencyAlarmSwitch_LP_2D_Play_ForceStop");
            sech.LoopingSound.ShouldNotBeNull();
            sech.LoopingSound.Start.ShouldBe(new Guid(echl.AsSpan(0, 16)));
            sech.LoopingSound.Stop.ShouldBe(new Guid(echl.AsSpan(16, 16)));
            sech.LoopingSound.Condition.IsNull.ShouldBeTrue();
            sech.LoopingSound.EventMapping.IsNull.ShouldBeTrue();
            var written = GameConstants.Starfield.MajorRecord(Write(w => sech.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(Content));
        }
    }
}
