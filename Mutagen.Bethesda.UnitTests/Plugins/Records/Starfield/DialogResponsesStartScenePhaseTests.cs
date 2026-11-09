using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// INFO INTV was kept as raw bytes (unknown in xEdit too). It is always 4 bytes, and it is the index of the
// Start Scene Phase (NAM0) in the Start Scene's (TSCE) phase list: on all 6,363 INFOs in Starfield.esm that hold it,
// the phase at that index has the NAM0 name (ignoring case), and an INFO without it starts at phase 0 or has no phase.
// WED0 and the response RVSH get xEdit's name Sound. TIQS stays signed: -1 means "no stage" (4,863 On Begin values).
// Each test reads a whole INFO from Starfield.esm with both readers and writes it back byte for byte.
public class DialogResponsesStartScenePhaseTests
{
    private static IEnumerable<IDialogResponsesGetter> Read(string hex)
        => ReadBoth<IDialogResponsesGetter>(Record("INFO", Hex(hex)), f => DialogResponses.CreateFromBinary(f), (s, p) => DialogResponsesBinaryOverlay.DialogResponsesFactory(s, p));

    private static void WritesBack(IDialogResponsesGetter info, string hex)
    {
        var written = GameConstants.Starfield.MajorRecord(Write(w => info.WriteToBinary(w)));
        written.Content.ToArray().ShouldBe(Hex(hex));
    }

    // [no EditorID] (0D6F1D:Starfield.esm), starts CF05_0900_IntercomDialogue (0D6ED1:Starfield.esm) at phase 3
    private const string Intercom = "454E414D040000200000545244410C000E4F15003DA20E00ABAA0A4054524F5408001C922700EFEE06414E414D310400E7E801004E414D320100004E414D330100004E414D340100004E414D390800E0D9431CF6ACD701414E414D04001E922700545343450400D16E0D00494E545604000300000057454430280013420F779B3CE1BA4C6E3903E3B49692000000000000000000000000000000000000000000000000544951530400FFFF1B034E414D301400506C617965722043686F6963653A204C6F6F7000494E414D040001000000";

    // [no EditorID] (24EABA:Starfield.esm): a response with an RVSH sound, starts its scene at phase 6 ("REENTRY PHASE")
    private const string Reentry = "454E414D040000200000545244410C0064E818003750FC00CECCCC3F4E414D3104009DF501004E414D320100004E414D330100004E414D340100004E414D390800301CA7766661D601525653482800FE4FB655CBC296B7507D815E2730E2B5000000000000000000000000000000000000000000000000435444412000010000000000803F3B000000F8EE2400DE0000000000000000000000FFFFFFFF435444412000010000000000803F3B000000F8EE2400DF0000000000000000000000FFFFFFFF414E414D04009953010054534345040073EA2400494E545604000600000057454430280013420F779B3CE1BA4C6E3903E3B496920000000000000000000000000000000000000000000000004E414D300E005245454E54525920504841534500494E414D040001000000";

    [Fact]
    public void Intercom_PhaseIndexAndQuestStage()
    {
        foreach (var info in Read(Intercom))
        {
            info.StartScene.FormKey.ID.ShouldBe(0x0D6ED1u);
            info.StartScenePhase.ShouldBe("Player Choice: Loop");
            info.StartScenePhaseIndex.ShouldBe(3u);
            info.SetParentQuestStage!.OnBegin.ShouldBe((short)-1);
            info.SetParentQuestStage.OnEnd.ShouldBe((short)795);
            info.Sound.ShouldNotBeNull();
            info.PreviousTopic.IsNull.ShouldBeTrue();
            WritesBack(info, Intercom);
        }
    }

    [Fact]
    public void Reentry_ResponseSound()
    {
        foreach (var info in Read(Reentry))
        {
            info.StartScenePhase.ShouldBe("REENTRY PHASE");
            info.StartScenePhaseIndex.ShouldBe(6u);
            info.SetParentQuestStage.ShouldBeNull();
            info.Responses.Single().Sound.ShouldNotBeNull();
            info.Sound.ShouldNotBeNull();
            WritesBack(info, Reentry);
        }
    }

    [Fact]
    public void PreviousTopic_IsALink()
    {
        // No INFO in Starfield.esm has TPIC; xEdit reads it as a DIAL FormID (Previous Topic), as Mutagen's Fallout 4
        // and Skyrim INFO do (Topic). Written by hand: TPIC = 0x0D6ED0, then the required NAM0.
        var bytes = Hex("54504943" + "0400" + "D06E0D00" + "4E414D30" + "0100" + "00");
        foreach (var info in ReadBoth<IDialogResponsesGetter>(Record("INFO", bytes), f => DialogResponses.CreateFromBinary(f), (s, p) => DialogResponsesBinaryOverlay.DialogResponsesFactory(s, p)))
        {
            info.PreviousTopic.FormKey.ID.ShouldBe(0x0D6ED0u);
            info.EnumerateFormLinks().ShouldContain(l => l.FormKey.ID == 0x0D6ED0u);
        }
    }
}
