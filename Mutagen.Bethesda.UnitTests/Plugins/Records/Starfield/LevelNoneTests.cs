using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A Starfield quest alias's create-reference level (ALCL) can be None, stored as 4 (xEdit's Starfield
// definitions; 11 aliases in Starfield.esm).
public class LevelNoneTests
{
    [Fact]
    public void QuestAlias_LevelNone_WritesFourAndReadsBack()
    {
        var quest = new Quest(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield);
        quest.Aliases = new()
        {
            new QuestReferenceAlias
            {
                ID = 0,
                Name = "Created",
                CreateReferenceToObject = new CreateReferenceToObject { Level = Level.None },
            },
        };
        var bytes = Write(w => quest.WriteToBinary(w));
        GameConstants.Starfield.MajorRecord(bytes).FindSubrecord(new RecordType("ALCL")).Content.ToArray()
            .ShouldBe(new byte[] { 4, 0, 0, 0 });

        foreach (var read in ReadBoth<IQuestGetter>(bytes, f => Quest.CreateFromBinary(f), (s, p) => QuestBinaryOverlay.QuestFactory(s, p)))
        {
            var alias = read.Aliases!.OfType<IQuestReferenceAliasGetter>().Single();
            alias.CreateReferenceToObject!.Level.ShouldBe(Level.None);
        }
    }
}
