using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// INFO ENAM response flags were a plain Int16, although DialogResponses.Flag has xEdit's names. On an INFO group the
// same bits are DialogResponses.InfoFlag (xEdit's union by record flag 0x40).
public class DialogResponseFlagTests
{
    // ENAM of INFO 22BC78: Random + Requires Player Activation, reset hours 0 (xEdit agrees)
    private const string Enam = "0A-00-00-00";

    private static IEnumerable<IDialogResponsesGetter> Read() =>
        ReadBoth<IDialogResponsesGetter>(Record("INFO", Subrecord("ENAM", Enam)), f => DialogResponses.CreateFromBinary(f), (s, p) => DialogResponsesBinaryOverlay.DialogResponsesFactory(s, p));

    [Fact]
    public void ResponseFlags_Read()
    {
        foreach (var info in Read())
        {
            info.Flags!.Flags.ShouldBe(DialogResponses.Flag.Random | DialogResponses.Flag.RequiresPlayerActivation);
        }
    }

    [Fact]
    public void GroupFlags_SameBits()
    {
        foreach (var info in Read())
        {
            ((DialogResponses.InfoFlag)(int)info.Flags!.Flags).ShouldBe(DialogResponses.InfoFlag.Random | DialogResponses.InfoFlag.ForceAllChildrenPlayerActivateOnly);
        }
    }

    [Fact]
    public void Enam_WritesSameBytes()
    {
        foreach (var info in Read())
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => info.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("ENAM")).Content.ToArray().ShouldBe(Hex(Enam));
        }
    }
}
