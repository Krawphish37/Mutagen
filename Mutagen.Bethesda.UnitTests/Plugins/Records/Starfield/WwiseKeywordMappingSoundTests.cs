using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Wwise keyword mapping sound IDs (WMSI). xEdit reads WMSI as a union picked by the record's Mapping Template (WMTI),
// but the four templates use disjoint values (Ship Engine 0-2, Cockpit 3, Grav Drive 4-7 and 9, Creature Anim 8),
// so one enum holds them all. Starfield.esm: 177 sound IDs in 79 mappings, each in its template's range.
// WMSS is the count of sound mappings (equal to it on all 79), and the WMSD pairs are Switch Group / Switch State.
public class WwiseKeywordMappingSoundTests
{
    // WKM_ShipGravDrive_Starborn_PC (07A16D:Starfield.esm): Grav Drive; Grav Jump Anim Started, Grav Jump Initiate (with one switch pair), Grav Jump Complete
    private const string GravDrive = "454449441E00574B4D5F536869704772617644726976655F53746172626F726E5F504300574D544902000200574D4B41080048A1070038FD0400574D5353040003000000574D534902000500574D53442C008141836A9224CA54E35EEBC0F7A949A100000000000000000000000000000000000000000000000000000000574D534902000900574D53442C00224BA55ABBFCF799BECFF6EBEACDF88DFB414517B8AB8D6A5E2B6FE2DF96DA97000000000000000000000000574D534902000600574D53442C004B4E8ECD262FBF4E049FCF7781D13FB500000000000000000000000000000000000000000000000000000000";

    // WKM_ShipEngine_Starborn_NPC_A (022CE1:Starfield.esm): Ship Engine; Interior, Main Loop, Afterburner, each with one switch pair
    private const string ShipEngine = "454449441E00574B4D5F53686970456E67696E655F53746172626F726E5F4E50435F4100574D544902000000574D4B410400401C1B00574D5353040003000000574D534902000100574D53444C000000000000000000000000000000000000000000000000000000000000000000000000000000000001000000D14619F559EA3C2607131674686332AD8541BD851F6726FC84735B3F64DE38A5574D534902000000574D53444C00164F06032E57C1F8604F6877F4D82F8400000000000000000000000000000000000000000000000001000000D14619F559EA3C2607131674686332AD8541BD851F6726FC84735B3F64DE38A5574D534902000200574D53444C000000000000000000000000000000000000000000000000000000000000000000000000000000000001000000D14619F559EA3C2607131674686332AD8541BD851F6726FC84735B3F64DE38A5";

    private static IEnumerable<IWWiseKeywordMappingGetter> Read(string hex)
        => ReadBoth<IWWiseKeywordMappingGetter>(Record("WKMF", Hex(hex)), f => WWiseKeywordMapping.CreateFromBinary(f), (s, p) => WWiseKeywordMappingBinaryOverlay.WWiseKeywordMappingFactory(s, p));

    [Fact]
    public void GravDrive_Reads()
    {
        foreach (var wkmf in Read(GravDrive))
        {
            wkmf.MappingTemplate.ShouldBe(WWiseKeywordMapping.MappingTemplateType.GravDrive);
            wkmf.Items.Select(i => i.SoundId).ShouldBe(new[]
            {
                WWiseKeywordMappingItem.SoundIdType.GravJumpAnimStarted,
                WWiseKeywordMappingItem.SoundIdType.GravJumpInitiate,
                WWiseKeywordMappingItem.SoundIdType.GravJumpComplete,
            });
            wkmf.Items[1].Sound!.SwitchData.Count.ShouldBe(0);
        }
    }

    [Fact]
    public void ShipEngine_Reads()
    {
        foreach (var wkmf in Read(ShipEngine))
        {
            wkmf.MappingTemplate.ShouldBe(WWiseKeywordMapping.MappingTemplateType.ShipEngine);
            wkmf.Items.Select(i => i.SoundId).ShouldBe(new[]
            {
                WWiseKeywordMappingItem.SoundIdType.Interior,
                WWiseKeywordMappingItem.SoundIdType.MainLoop,
                WWiseKeywordMappingItem.SoundIdType.Afterburner,
            });
            var pair = wkmf.Items[0].Sound!.SwitchData.Single();
            pair.SwitchGroup.ShouldNotBe(Guid.Empty);
            pair.SwitchState.ShouldNotBe(Guid.Empty);
        }
    }

    [Theory]
    [InlineData(GravDrive)]
    [InlineData(ShipEngine)]
    public void WritesSameBytes(string hex)
    {
        foreach (var wkmf in Read(hex))
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => wkmf.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(hex));
        }
    }

    [Fact]
    public void SoundId_Values()
    {
        ((ushort)WWiseKeywordMappingItem.SoundIdType.MainLoop).ShouldBe((ushort)0);
        ((ushort)WWiseKeywordMappingItem.SoundIdType.Cockpit).ShouldBe((ushort)3);
        ((ushort)WWiseKeywordMappingItem.SoundIdType.GravJumpWarmup).ShouldBe((ushort)7);
        ((ushort)WWiseKeywordMappingItem.SoundIdType.CreatureAnim).ShouldBe((ushort)8);
        ((ushort)WWiseKeywordMappingItem.SoundIdType.GravJumpInitiate).ShouldBe((ushort)9);
    }
}
