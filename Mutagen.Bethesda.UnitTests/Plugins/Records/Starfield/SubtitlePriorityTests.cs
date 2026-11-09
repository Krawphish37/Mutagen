using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield dialogue subtitle priority (INFO INAM) is Low 0, Normal 1, High 2, Force 3 (xEdit's Starfield
// definitions). Starfield.esm: 8,950 responses store 0, 117,311 store 1, 23 store 2 and 63 store 3.
public class SubtitlePriorityTests
{
    [Theory]
    [InlineData("00-00-00-00", DialogResponses.SubtitlePriorityLevel.Low)]    // INFO 31F6A0
    [InlineData("02-00-00-00", DialogResponses.SubtitlePriorityLevel.High)]   // INFO 0DD206
    [InlineData("03-00-00-00", DialogResponses.SubtitlePriorityLevel.Force)]  // INFO 295F51
    [InlineData("01-00-00-00", DialogResponses.SubtitlePriorityLevel.Normal)]
    public void SubtitlePriority_Reads(string inam, DialogResponses.SubtitlePriorityLevel expected)
    {
        var bytes = Record("INFO", Subrecord("INAM", inam));
        foreach (var info in ReadBoth<IDialogResponsesGetter>(bytes, f => DialogResponses.CreateFromBinary(f), (s, p) => DialogResponsesBinaryOverlay.DialogResponsesFactory(s, p)))
        {
            info.SubtitlePriority.ShouldBe(expected);
        }
    }

    [Theory]
    [InlineData(DialogResponses.SubtitlePriorityLevel.Low, 0)]
    [InlineData(DialogResponses.SubtitlePriorityLevel.Normal, 1)]
    [InlineData(DialogResponses.SubtitlePriorityLevel.Force, 3)]
    public void SubtitlePriority_Writes(DialogResponses.SubtitlePriorityLevel level, byte expected)
    {
        var info = new DialogResponses(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield) { SubtitlePriority = level };
        var written = GameConstants.Starfield.MajorRecord(Write(w => info.WriteToBinary(w)));
        written.FindSubrecord(new RecordType("INAM")).Content.ToArray().ShouldBe(new byte[] { expected, 0, 0, 0 });
    }
}
