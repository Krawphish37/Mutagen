using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C36: five hand-read subrecords fill no field (SFSearch found it in the IL): BFCB, TIFC, IDLC, XPPA and RACE's NAM3.
// Each is written back from something else (CustomLogicFillsTests holds the xml's writtenFrom against the code):
// BFCB from the component's class, TIFC and IDLC from their list's count, XPPA and NAM3 as empty markers.
// Census (Starfield.esm, the 32 official plugins and all 370 plugins in the Data folder, raw bytes): every BFCB is
// a ComponentType name with one closing zero; every TIFC (98,253) is 4 bytes and equals its TIFL's count; every IDLC
// (8 IDLM, 22 PACK) is 4 bytes and equals the next IDLA's count; every XPPA (9,009, all REFR) is empty and follows
// XPRD; every RACE (70) has exactly one NAM3, empty. So nothing is lost: these are real records read with both
// readers and written back byte for byte.
public class CustomLogicUnfilledSubrecordTests
{
    private static byte[] RecordOf(string type, string content)
    {
        // a record header (FormID 0x800, form version 555) around the record's own subrecords from Starfield.esm
        var body = Hex(content);
        var bytes = Record(type);
        var ret = new byte[bytes.Length + body.Length];
        bytes.CopyTo(ret, 0);
        body.CopyTo(ret, bytes.Length);
        BitConverter.GetBytes(body.Length).CopyTo(ret, 4);
        return ret;
    }

    private static byte[] Written(IMajorRecordGetter rec) => Write(w => ((IBinaryItem)rec).WriteToBinary(w));

    private static int Count(byte[] record, string sub) =>
        GameConstants.Starfield.MajorRecord(record).Count(s => s.RecordType == new RecordType(sub));

    // ResourceVents (299B38:Starfield.esm): EDID, BFCB "BGSScannable", BFCE, CNAM, TNAM, FNAM
    private const string Keyword =
        "454449440E005265736F7572636556656E747300424643420D004247535363616E6E61626C6500424643450000434E414D0400FFFFFF00" +
        "544E414D040034000000464E414D040000000000";

    [Fact]
    public void Bfcb_WrittenFromTheComponentClass()
    {
        var bytes = RecordOf("KYWD", Keyword);
        foreach (var kywd in ReadBoth<IKeywordGetter>(bytes, f => Mutagen.Bethesda.Starfield.Keyword.CreateFromBinary(f), (s, p) => KeywordBinaryOverlay.KeywordFactory(s, p)))
        {
            kywd.Components.Single().ShouldBeAssignableTo<IScannableComponentGetter>();
            Written(kywd).ShouldBe(bytes);
        }
    }

    // [no EditorID] (005734:Starfield.esm), a topic of COM_Quest_Barrett_Q02 (002FAE:Starfield.esm):
    // TIFL 005A42, 005A43; TIFC 2
    private const string Topic =
        "504E414D040000004842514E414D0400AE2F000044415441040000021100534E414D04005343454E5449464C0800425A0000435A0000" +
        "54494643040002000000";

    [Fact]
    public void Tifc_WrittenFromTheInfoListCount()
    {
        var bytes = RecordOf("DIAL", Topic);
        foreach (var dial in ReadBoth<IDialogTopicGetter>(bytes, f => DialogTopic.CreateFromBinary(f), (s, p) => DialogTopicBinaryOverlay.DialogTopicFactory(s, p)))
        {
            dial.TopicInfoList!.Select(l => l.FormKey.ID).ShouldBe(new uint[] { 0x005A42, 0x005A43 });
            Written(dial).ShouldBe(bytes);
            Count(Written(dial), "TIFC").ShouldBe(1);
        }
    }

    // NPCPointingForward (0192CA:Starfield.esm): IDLF 08, IDLC 1, IDLT 0, IDLA 019297
    private const string IdleMarker =
        "4544494413004E5043506F696E74696E67466F7277617264004F424E441800004046BF004046BF0000C0B40040463F0060A33F0020413E" +
        "4F44545904000000000049444C4601000849444C4304000100000049444C5404000000000049444C41040097920100464C4C4404000100" +
        "0000";

    [Fact]
    public void Idlc_WrittenFromTheAnimationCount()
    {
        var bytes = RecordOf("IDLM", IdleMarker);
        foreach (var idlm in ReadBoth<IIdleMarkerGetter>(bytes, f => Mutagen.Bethesda.Starfield.IdleMarker.CreateFromBinary(f), (s, p) => IdleMarkerBinaryOverlay.IdleMarkerFactory(s, p)))
        {
            idlm.Animations!.Select(l => l.FormKey.ID).ShouldBe(new uint[] { 0x019297 });
            Written(idlm).ShouldBe(bytes);
        }
    }

    // [no EditorID] (0DABAB:Starfield.esm) in CityNewAtlantisWell (0F4ED4:Starfield.esm): NAME, XPRD 20.0, XPPA (empty),
    // INAM 0, PDTO, DATA
    private const string Reference =
        "4E414D450400969106005850524404000000A041585050410000494E414D0400000000005044544F08000000000000000000444154411800" +
        "34A89D43C32E20C3A1EAC24000000000000000806DA0B040";

    [Fact]
    public void Xppa_WrittenAsMarkerWithThePatrol()
    {
        var bytes = RecordOf("REFR", Reference);
        foreach (var refr in ReadBoth<IPlacedObjectGetter>(bytes, f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            refr.Patrol.ShouldNotBeNull();
            refr.Patrol!.IdleTime.ShouldBe(20f);
            Written(refr).ShouldBe(bytes);
            Count(Written(refr), "XPPA").ShouldBe(1);
        }
    }

    // AutopilotRace (025739:Starfield.esm), the smallest RACE of Starfield.esm: NAM3 (empty) after FLLD and before the
    // biped object names (NAME), with no ENAM or NAM4 around it (the one RACE of the official plugins laid out so).
    // One change from the file: DESC holds the empty text (1 byte) here, where localized Starfield.esm holds string ID 0
    // (4 bytes); this test reads the record as a plain, unlocalized plugin.
    private const string Race =
        "454449440E004175746F70696C6F74526163650044455343010000" + "4B53495A0400010000004B57444104003A57020050525053A800C202" +
        "00000000000000000000C30200000000000000000000C40200000000000000000000C50200000000000000000000C60200000000A04000000000" +
        "C70200000000000000000000C80200000000000000000000D40200000000484200000000E00200000000803F00000000DC020000000096430000" +
        "0000D70200000000000000000000D90200000000000000000000D80200000000000000000000214002000000000000000000474E414D04008134" +
        "000044415432DD000000803F0000803F4CA6AA3E4CA6AA3E4CA6AA3EABAAAA3EABAAAA3EABAAAA3E61890010214000000000803E0000803E0100" +
        "0000FFFFFFFFFFFFFFFF00000000FFFFFFFFFFFFFFFFFFFFFFFF0000F041000000000000803E0000344200000000000000000000000000000000" +
        "000000000000000000000000000096C300000000FFFFFFFF00000000000000000000000000000000000000000000B4420000B442000000000000" +
        "000000000000000000000000000000000000000000000300000000000000000000000000000000000000000000000000000000000000004D4E41" +
        "4D0000414E414D26004D61726B6572735C44756D6D794D61726B6572735C44756D6D79536D616C6C30312E6E696600464C4C4404000100000046" +
        "4E414D0000464C4C440400010000004D544E4D040057414C4B4D544E4D040052554E314D544E4D0400534E454B4D544E4D0400424C444F4D544E" +
        "4D04005357494D5654434B08001AC607002E000000504E414D04000000A040554E414D0400000040404E414D3100004D4E414D0000494E445804" +
        "0000000000464C4C44040001000000464E414D0000494E4458040000000000464C4C440400010000004E414D3300004E414D450100004E414D45" +
        "0100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100" +
        "004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E" +
        "414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D" +
        "450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D4501" +
        "00004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D45010000" +
        "4E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E41" +
        "4D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D450100004E414D45" +
        "0100004E414D450100004E414D450100004E414D450100004E414D45010000524250430001000000000000000000000000000000000000000000" +
        "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000" +
        "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000" +
        "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000" +
        "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000" +
        "000000564E414D0400FFFFFFFF514E414D0400423F0100514E414D0400433F0100514E414D0400AC6A0400514E414D040098560300554E575004" +
        "00F4010000574B4D560400823400004D4E414D0000464E414D000050544F500400000040404E544F500400000040414D4E414D0000464E414D00" +
        "00";

    [Fact]
    public void RaceNam3_WrittenAsMarker()
    {
        var bytes = RecordOf("RACE", Race);
        Count(bytes, "NAM3").ShouldBe(1);
        foreach (var race in ReadBoth<IRaceGetter>(bytes, f => Mutagen.Bethesda.Starfield.Race.CreateFromBinary(f), (s, p) => RaceBinaryOverlay.RaceFactory(s, p)))
        {
            race.EditorID.ShouldBe("AutopilotRace");
            Written(race).ShouldBe(bytes);
        }
    }
}
