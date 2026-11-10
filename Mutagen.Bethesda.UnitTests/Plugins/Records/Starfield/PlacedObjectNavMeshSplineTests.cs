using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// REFR XNSE (navmesh spline data) is a Reflection object. Its NavMeshSplineExtraData::ChunkDataRef.RefID is a plain
// UInt32 that holds a REFR's FormID (21,587 in Starfield.esm, every one a placed reference): now a FormLink.
public class PlacedObjectNavMeshSplineTests
{
    private static void WritesBack(IStarfieldMajorRecordGetter rec, string hex)
    {
        var written = GameConstants.Starfield.MajorRecord(Write(w => rec.WriteToBinary(w)));
        written.Content.ToArray().ShouldBe(Hex(hex));
    }

    // (3E8CA3:Starfield.esm), a navmesh spline whose chunk references (3E8CA2:Starfield.esm)
    private const string SplineReferenceHex = "4E414D450400F901000058425344180000000000000040404036014090404C4098B4243F00ABABAB584E534547014245544808000000040000000700000053545254860000004E61764D65736853706C696E654578747261446174613A3A4368756E6B44617461005265666572656E636573004E61764D65736841726561466C61670053706C696E65436F6C6F725F5061636B6564004E61764D65736853706C696E654578747261446174613A3A4368756E6B446174615265660053706C696E655369646500526566494400545950450400000002000000434C4153300000000000000000000000000003002200000003FFFFFF000010002D00000002FFFFFF100002003D0000000DFFFFFF14000400434C4153240000005000000000000000000002007500000002FFFFFF00000100800000000DFFFFFF040004004F424A5415000000000000000B00507265666572506174680042332D004C4953541400000050000000010000000600537461727400A28C3E00444154411800DAE18043486BA942F0411241000000000000000000000000";

    [Fact]
    public void SplineReference()
    {
        foreach (var rec in ReadBoth<IPlacedObjectGetter>(Record("REFR", Hex(SplineReferenceHex)), f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            rec.NavMeshSplineExtraData.ShouldHaveFormIds(0x3E8CA2);
            rec.NavMeshSplineExtraData.RootObject().Class.Name.ShouldBe("NavMeshSplineExtraData::ChunkData");
            rec.EnumerateFormLinks().Select(l => l.FormKey.ID).ShouldContain(0x3E8CA2u);
            WritesBack(rec, SplineReferenceHex);
        }
    }

}
