using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C157: KYWD FNAM was a UInt32, so the 4 keywords of darkstarpatch_bublumi.esm that store an empty (0-byte) FNAM threw
// ArgumentOutOfRangeException in both readers, and the plugin couldn't be imported. FNAM is now an object with a Break:
// null when absent (1,241 mod keywords), Versioning Break0 when empty, Type when it has 4 bytes (9,774 keywords).
public class KeywordEmptyFnamTests
{
    // Record contents from darkstarpatch_bublumi.esm: EDID, CNAM, TNAM, empty FNAM, FULL "Internal"
    private const string BubLumiInternalMod = "4544494420006D615F4461726B537461725F4275624C756D695F496E7465726E616C4D6F6400434E414D040000000000544E414D040005000000464E414D000046554C4C0900496E7465726E616C00";
    private const string BubLumiAutoInternalMod = "4544494424006D615F4461726B537461725F4275624C756D694175746F5F496E7465726E616C4D6F6400434E414D040000000000544E414D040005000000464E414D000046554C4C0900496E7465726E616C00";
    private const string ApBubLumiAutoInternalMod = "45444944240061705F4461726B537461725F4275624C756D694175746F5F496E7465726E616C4D6F6400434E414D040000000000544E414D040002000000464E414D000046554C4C0900496E7465726E616C00";
    private const string ApBubLumiInternalMod = "45444944200061705F4461726B537461725F4275624C756D695F496E7465726E616C4D6F6400434E414D040000000000544E414D040002000000464E414D000046554C4C0900496E7465726E616C00";
    // ma_BubLumi_Quality_MkII (KYWD 00080B in the same plugin): a normal 4-byte FNAM of zeros
    private const string BubLumiQualityMkII = "4544494418006D615F4275624C756D695F5175616C6974795F4D6B494900434E414D0400FFFFFF00544E414D040005000000464E414D040000000000";

    private static IEnumerable<IKeywordGetter> Read(string content) =>
        ReadBoth<IKeywordGetter>(Record("KYWD", Hex(content)), f => Keyword.CreateFromBinary(f), (s, p) => KeywordBinaryOverlay.KeywordFactory(s, p));

    private static byte[] Written(IKeywordGetter kywd) =>
        GameConstants.Starfield.MajorRecord(Write(w => kywd.WriteToBinary(w))).Content.ToArray();

    [Theory]
    [InlineData(BubLumiInternalMod, "ma_DarkStar_BubLumi_InternalMod")]
    [InlineData(BubLumiAutoInternalMod, "ma_DarkStar_BubLumiAuto_InternalMod")]
    [InlineData(ApBubLumiAutoInternalMod, "ap_DarkStar_BubLumiAuto_InternalMod")]
    [InlineData(ApBubLumiInternalMod, "ap_DarkStar_BubLumi_InternalMod")]
    public void EmptyFnam_ReadsAndWritesBack(string content, string editorId)
    {
        foreach (var kywd in Read(content))
        {
            kywd.EditorID.ShouldBe(editorId);
            kywd.FormLinkTarget.ShouldNotBeNull();
            kywd.FormLinkTarget!.Versioning.HasFlag(KeywordFormLinkTarget.VersioningBreaks.Break0).ShouldBeTrue();
            kywd.Name!.String.ShouldBe("Internal");
            Written(kywd).ShouldBe(Hex(content));
        }
    }

    [Fact]
    public void NormalFnam_ReadsAndWritesBack()
    {
        foreach (var kywd in Read(BubLumiQualityMkII))
        {
            kywd.FormLinkTarget!.Versioning.ShouldBe(default(KeywordFormLinkTarget.VersioningBreaks));
            kywd.FormLinkTarget!.Type.ShouldBe(0u);
            Written(kywd).ShouldBe(Hex(BubLumiQualityMkII));
        }
    }

    [Fact]
    public void NoFnam_StaysAbsent()
    {
        var content = BubLumiQualityMkII[..BubLumiQualityMkII.IndexOf("464E414D", StringComparison.Ordinal)];
        foreach (var kywd in Read(content))
        {
            kywd.FormLinkTarget.ShouldBeNull();
            Written(kywd).ShouldBe(Hex(content));
        }
    }

    [Fact]
    public void NewKeyword_WritesFourByteFnam()
    {
        var kywd = new Keyword(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield) { FormLinkTarget = new KeywordFormLinkTarget { Type = 47 } };
        var fnam = GameConstants.Starfield.MajorRecord(Write(w => kywd.WriteToBinary(w))).FindSubrecord(new RecordType("FNAM"));
        fnam.Content.ToArray().ShouldBe(new byte[] { 47, 0, 0, 0 });
    }
}
