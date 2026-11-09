using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A message's BNAM is the Index Chosen On Back Out, a signed Int32 (xEdit: itS32). It was an unknown UInt32, so the
// -1 stored by 1,148 of Starfield.esm's 1,149 messages read as 4294967295.
public class MessageBackOutIndexTests
{
    [Theory]
    // HelpPipBoyInventoryXbox (00016D:Starfield.esm): -1
    [InlineData("FF-FF-FF-FF", -1)]
    // TraitStarterHome_DoorMortgageMessage (0A47B6:Starfield.esm): 2, the only other value
    [InlineData("02-00-00-00", 2)]
    public void IndexChosenOnBackOut_IsSigned(string hex, int index)
    {
        var bytes = Record("MESG", Subrecord("DNAM", "00-00-00-00"), Subrecord("BNAM", hex));
        foreach (var msg in ReadBoth<IMessageGetter>(bytes, f => Message.CreateFromBinary(f), (s, p) => MessageBinaryOverlay.MessageFactory(s, p)))
        {
            msg.IndexChosenOnBackOut.ShouldBe(index);
            var written = GameConstants.Starfield.MajorRecord(Write(w => msg.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("BNAM")).Content.ToArray().ShouldBe(Hex(hex));
        }
    }
}
