using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// - A dialog response's TROT entries hold xEdit's Voice Type (135,407 TROT in Starfield.esm, all VoiceType links);
//   the field was marked unknown although its name and link type were right.
// - A quest location alias's ALPS block (the ALPS marker, conditions and the LNAM keyword) is xEdit's Planet Content
//   Request (75 in 65 quests); the field was named after its signature.
// Each test reads a whole record from Starfield.esm with both readers and writes it back byte for byte (for the
// INFO, without NAM1: a localized string ID).
public class DialogQuestFieldNameTests
{
    private static byte[] Without(ReadOnlySpan<byte> content, RecordType skip)
    {
        var ret = new MemoryStream();
        var pos = 0;
        while (pos < content.Length)
        {
            var type = new RecordType(BitConverter.ToInt32(content.Slice(pos, 4)));
            var len = BitConverter.ToUInt16(content.Slice(pos + 4, 2));
            if (type != skip) ret.Write(content.Slice(pos, 6 + len));
            pos += 6 + len;
        }
        return ret.ToArray();
    }

    // [no EditorID] (22B3FA:Starfield.esm)
    private const string Info = "454E414D040000000000545244410C005D750D00596E5F000000000054524F540800C5CB2700BCBB8B404E414D310400C03C01004E414D322600596F75277265206120686973746F7269616E2C20676976696E672061206C6563747572652E004E414D330100004E414D340100004E414D3908000072243F035CD8014E414D30010000494E414D040001000000";

    // MQ_TempleQuest_SpawnTemple (032898:Starfield.esm)
    private const string TempleQuest = "454449441B004D515F54656D706C6551756573745F537061776E54656D706C6500564D41445F0006000200010013004D51537061776E54656D706C65536372697074000200140054656D706C654C6F636174696F6E506C6163656401010000000098280300150054656D706C65506C616365644D61704D61726B657201010000030098280300444E414D0C0000000000500000000000000051545950040000760400465459500400E14605004E4558540000414E414D040004000000414C4C53040000000000414C4944150054656D706C654C6F636174696F6E506C6163656400464E414D04000A000040414C4647040000000000414C505300004C4E414D04009FC52000414C504E0400FEFFFFFF414C45440000414C5354040003000000414C4944160054656D706C65506C616365644D61704D61726B657200464E414D040002000000414C4647040000000000414C4641040000000000414C525404001F2702005654434B040000000000414C45440000";

    [Fact]
    public void ResponseTrot_VoiceType()
    {
        foreach (var info in ReadBoth<IDialogResponsesGetter>(Record("INFO", Hex(Info)), f => DialogResponses.CreateFromBinary(f), (s, p) => DialogResponsesBinaryOverlay.DialogResponsesFactory(s, p)))
        {
            var trot = info.Responses.Single().TROTs.Single();
            // GenericFemale02 (27CBC5:Starfield.esm)
            trot.VoiceType.FormKey.ID.ShouldBe(0x27CBC5u);
            trot.Duration.ShouldBe(4.366f, 0.001f);
            var written = GameConstants.Starfield.MajorRecord(Write(w => info.WriteToBinary(w)));
            Without(written.Content, new RecordType("NAM1")).ShouldBe(Without(Hex(Info), new RecordType("NAM1")));
        }
    }

    [Fact]
    public void LocationAlias_PlanetContentRequest()
    {
        foreach (var qust in ReadBoth<IQuestGetter>(Record("QUST", Hex(TempleQuest)), f => Quest.CreateFromBinary(f), (s, p) => QuestBinaryOverlay.QuestFactory(s, p)))
        {
            var request = qust.Aliases!.OfType<IQuestLocationAliasGetter>().Select(a => a.PlanetContentRequest).Single(r => r != null);
            request.ShouldNotBeNull();
            var written = GameConstants.Starfield.MajorRecord(Write(w => qust.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(TempleQuest));
        }
    }
}
