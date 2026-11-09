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

// Enums xEdit names that Mutagen kept as plain numbers or raw bytes. Every subrecord is copied from Starfield.esm.
public class SmallEnumTests
{
    private static void WritesBack(IMajorRecordGetter rec, params (string Sig, string Hex)[] subs)
    {
        var written = GameConstants.Starfield.MajorRecord(Write(w => ((IBinaryItem)rec).WriteToBinary(w)));
        foreach (var (sig, hex) in subs)
        {
            written.FindSubrecord(new RecordType(sig)).Content.ToArray().ShouldBe(Hex(hex), sig);
        }
    }

    [Fact]
    public void PlanetContentManagerBranchNode_ChildSelection()
    {
        // NAM2 of a branch node set to Stacked (PCBN 02C4EA). The enum existed but the field was an Int32.
        var bytes = Record("PCBN", Subrecord("NAM1", "01-00-00-00"), Subrecord("NAM2", "01-00-00-00"));
        foreach (var pcbn in ReadBoth<IPlanetContentManagerBranchNodeGetter>(bytes, f => PlanetContentManagerBranchNode.CreateFromBinary(f), (s, p) => PlanetContentManagerBranchNodeBinaryOverlay.PlanetContentManagerBranchNodeFactory(s, p)))
        {
            pcbn.NodeType.ShouldBe(PlanetContentManagerBranchNode.NodeTypeOption.BranchNode);
            pcbn.ChildSelection.ShouldBe(PlanetContentManagerBranchNode.ChildSelectionOption.Stacked);
            WritesBack(pcbn, ("NAM2", "01-00-00-00"));
        }
    }

    [Fact]
    public void PlanetContentManagerTree_Fields()
    {
        // NAM1, NAM2, NAM5 of the tree 018BB1: Root, Stacked, false (all 5 trees in Starfield.esm have these values)
        var bytes = Record("PCMT", Subrecord("NAM1", "00-00-00-00"), Subrecord("NAM2", "01-00-00-00"), Subrecord("NAM5", "00"));
        foreach (var pcmt in ReadBoth<IPlanetContentManagerTreeGetter>(bytes, f => PlanetContentManagerTree.CreateFromBinary(f), (s, p) => PlanetContentManagerTreeBinaryOverlay.PlanetContentManagerTreeFactory(s, p)))
        {
            pcmt.NodeType.ShouldBe(PlanetContentManagerBranchNode.NodeTypeOption.Root);
            pcmt.ChildSelection.ShouldBe(PlanetContentManagerBranchNode.ChildSelectionOption.Stacked);
            pcmt.ConsumeRequestEvenOnFailure.ShouldBeFalse();
            WritesBack(pcmt, ("NAM1", "00-00-00-00"), ("NAM2", "01-00-00-00"), ("NAM5", "00"));
        }
    }

    [Fact]
    public void SpeechChallenge_Difficulty()
    {
        // DIFF of SPCH 018083: Hard
        foreach (var spch in ReadBoth<ISpeechChallengeGetter>(Record("SPCH", Subrecord("DIFF", "02-00")), f => SpeechChallenge.CreateFromBinary(f), (s, p) => SpeechChallengeBinaryOverlay.SpeechChallengeFactory(s, p)))
        {
            spch.Difficulty.ShouldBe(SpeechChallenge.DifficultyLevel.Hard);
            WritesBack(spch, ("DIFF", "02-00"));
        }
    }

    [Fact]
    public void SnapTemplateBehavior_Operation()
    {
        // The behavior of STBH 0DB02C: Foundation, with its payload and snap template node
        var bytes = Record("STBH", Subrecord("ENAM", "03-00-00-00"), Subrecord("PNAM", "2F-8E-39-00"), Subrecord("CITC", "00-00-00-00"), Subrecord("UNAM", "18-F8-23-00"), Subrecord("VNAM", "00-00-00-00"));
        foreach (var stbh in ReadBoth<ISnapTemplateBehaviorGetter>(bytes, f => SnapTemplateBehavior.CreateFromBinary(f), (s, p) => SnapTemplateBehaviorBinaryOverlay.SnapTemplateBehaviorFactory(s, p)))
        {
            stbh.Items.Single().Operation.ShouldBe(SnapTemplateBehavior.OperationType.Foundation);
            WritesBack(stbh, ("ENAM", "03-00-00-00"));
        }
    }

    [Fact]
    public void WwiseKeywordMapping_MappingTemplate()
    {
        // WMTI of WKMF 00DF23: Creature Anim
        foreach (var wkmf in ReadBoth<IWWiseKeywordMappingGetter>(Record("WKMF", Subrecord("WMTI", "03-00")), f => WWiseKeywordMapping.CreateFromBinary(f), (s, p) => WWiseKeywordMappingBinaryOverlay.WWiseKeywordMappingFactory(s, p)))
        {
            wkmf.MappingTemplate.ShouldBe(WWiseKeywordMapping.MappingTemplateType.CreatureAnim);
            WritesBack(wkmf, ("WMTI", "03-00"));
        }
    }

    [Fact]
    public void Traversal_Data()
    {
        // DNAM of TRAV 05988D: Vault, min height 0.6, max height 1.0, unknown 1
        const string dnam = "03-00-00-00-9A-99-19-3F-00-00-80-3F-01-00-00-00";
        foreach (var trav in ReadBoth<ITraversalGetter>(Record("TRAV", Subrecord("DNAM", dnam)), f => Traversal.CreateFromBinary(f), (s, p) => TraversalBinaryOverlay.TraversalFactory(s, p)))
        {
            trav.Type.ShouldBe(Traversal.TraversalType.Vault);
            trav.MinHeight.ShouldBe(0.6f);
            trav.MaxHeight.ShouldBe(1.0f);
            trav.Unknown.ShouldBe(1u);
            WritesBack(trav, ("DNAM", dnam));
        }
    }
}
