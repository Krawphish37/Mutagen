using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A location reference type's TNAM is the keyword type (xEdit: itU32 with the keyword type enum, as on KYWD).
// It was raw bytes. All 3,053 LCRT in Starfield.esm store 0 (None); FNAM is unknown on both sides and stays raw.
public class LocationReferenceTypeTests
{
    // UC08_LOND_UndergroundAccessDoorINT_RefType (000816:Starfield.esm), whole record
    private const string Lond = "454449442B00554330385F4C4F4E445F556E64657267726F756E64416363657373446F6F72494E545F5265665479706500434E414D0400FFFFFF00544E414D040000000000464E414D040000000000";

    [Fact]
    public void Type_IsKeywordType()
    {
        var bytes = Record("LCRT", Hex(Lond));
        foreach (var lcrt in ReadBoth<ILocationReferenceTypeGetter>(bytes, f => LocationReferenceType.CreateFromBinary(f), (s, p) => LocationReferenceTypeBinaryOverlay.LocationReferenceTypeFactory(s, p)))
        {
            lcrt.Type.ShouldBe(Keyword.TypeEnum.None);
            var written = GameConstants.Starfield.MajorRecord(Write(w => lcrt.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(Lond));
        }
    }

    [Fact]
    public void Type_Writes()
    {
        var lcrt = new LocationReferenceType(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            Type = Keyword.TypeEnum.QuestTarget,
        };
        var written = GameConstants.Starfield.MajorRecord(Write(w => lcrt.WriteToBinary(w)));
        written.FindSubrecord(new RecordType("TNAM")).Content.ToArray().ShouldBe(Hex("0C-00-00-00"));
    }
}
