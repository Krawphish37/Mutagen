using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// FFKW REFL is a Reflection object: its keywords are FormLinks. A whole record from Starfield.esm, both readers,
// written back byte for byte.
public class FormFolderKeywordListReflectionTests
{
    private static void WritesBack(IStarfieldMajorRecordGetter rec, string hex)
    {
        var written = GameConstants.Starfield.MajorRecord(Write(w => rec.WriteToBinary(w)));
        written.Content.ToArray().ShouldBe(Hex(hex));
    }

    // landscape_caves_mine (000AFA:Starfield.esm)
    private const string LandscapeCavesMineHex = "4544494415006C616E6473636170655F63617665735F6D696E65005245464CBC00424554480800000004000000060000005354525431000000424753466F726D466F6C6465724B6579776F72644C697374004B6579776F72647300466F726D466F6C6465725061746800545950450400000001000000434C4153240000000000000000000000000002001900000003FFFFFF400110002200000002FFFFFF500108004F424A541B0000000000000015004C616E6473636170655C43617665735C4D696E65004C4953541000000005FFFFFF010000000DFFFFFFF60A0000";

    [Fact]
    public void LandscapeCavesMine()
    {
        foreach (var rec in ReadBoth<IFormFolderKeywordListGetter>(Record("FFKW", Hex(LandscapeCavesMineHex)), f => FormFolderKeywordList.CreateFromBinary(f), (s, p) => FormFolderKeywordListBinaryOverlay.FormFolderKeywordListFactory(s, p)))
        {
            // Cave_Mine_Swap_Red (000AF6:Starfield.esm)
            rec.Reflection.ShouldHaveFormIds(0x000AF6);
            rec.EnumerateFormLinks().Select(l => l.FormKey.ID).ShouldBe(new[] { 0x000AF6u });
            WritesBack(rec, LandscapeCavesMineHex);
        }
    }

}
