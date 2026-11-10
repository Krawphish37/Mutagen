using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A destructible's DSDL was raw bytes (the field "DSDL"; its XML named SecondaryDamageList as refName, which Loqui
// ignores on a ByteArray). It is xEdit's Secondary Damage List, a link to an SDLT record, so the FormID was never
// remapped. Both DSDL in Starfield.esm point at SDLT records. The tests read real records with both readers and
// write them back byte for byte.
public class DestructibleSecondaryDamageListTests
{
    // IndIntRmSmWallMid_PlugA03_PanelLock01 (205F0F:Starfield.esm), whole record
    private const string PanelLock = "454449442600496E64496E74526D536D57616C6C4D69645F506C75674130335F50616E656C4C6F636B3031004F424E4418008EAC4A3ECD8C5EBEEC151DBE47E6A53E9AC9843E144A393E4F4454590400000000004D4F444C55004172636869746563747572655C496E647573747269616C4B69745C496E746572696F72735C506C7567735C496E64496E74526D536D57616C6C4D69645F506C75674130335F50616E656C4C6F636B30312E6E696600464C4C4404000100000044455354080001000000010400004453444C0400270B2000445354441800000000000000000000000000000000000000000000000000445354460000505250530C004D7C1F000000C84200000000504E414D0400CC4C3300464E414D020000004A4E414D02006801";

    // HzTest_Test_Barrel_Explode_Fire (208DCF:Starfield.esm), every subrecord except FULL, which is a localized
    // string ID in Starfield.esm (the test reader is not localized)
    private const string Barrel = "454449442000487A546573745F546573745F42617272656C5F4578706C6F64655F4669726500564D41448F0106000200020014005369676E616C52656365697665725363726970740001000700526573756C7473110103000000030000000A00466F726D506172616D3101010000FFFFC88D200006005369676E616C01010000FFFFC98D20000E00526573756C7454797065456E756D030101000000030000000E00526573756C7454797065456E756D03010B00000006005369676E616C01010000FFFFC98D20000A00466F726D506172616D3101010000FFFFC78D2000040000000E00526573756C7454797065456E756D0301020000000A00466F726D506172616D3101010000FFFFE502000006005369676E616C01010000FFFFC98D20000B0056616C7565506172616D3104010000000019005369676E616C53656E6465725F4F6E4163746F7256616C75650004001000526563656976657254797065456E756D0301040000000800546172676574415601010000FFFF8FED22000B0054617267657456616C756504010000C6420D005369676E616C73546F53656E641101010000000100000006005369676E616C01010000FFFFC98D20004F424E441800DB1F9ABE09209ABE003073BA23209A3EF51F9A3EE495803F4F4454590400000000004D4F444C2D005365744472657373696E675C436F6E7461696E65725C576173746542617272656C4472756D5F30312E6E696600464C4C44040001000000444553540800640000000204000044414D431800CD8D20000000000000000000820A060000000000000000004453444C0400CA8D200044535444180046000000000000004F570E000000000000000000000000004453544600004453544418000001000000000000000000000000000000000000000000004453544600005052505318008FED22000000C84200000000CB8D20000000C8420000000044415441010004";

    [Fact]
    public void Activator_SecondaryDamageListIsALink()
    {
        foreach (var acti in ReadBoth<IActivatorGetter>(Record("ACTI", Hex(PanelLock)),
                     f => Mutagen.Bethesda.Starfield.Activator.CreateFromBinary(f),
                     (s, p) => ActivatorBinaryOverlay.ActivatorFactory(s, p)))
        {
            acti.EditorID.ShouldBe("IndIntRmSmWallMid_PlugA03_PanelLock01");
            // TRP_SDL_AllDamageTypes (200B27:Starfield.esm)
            acti.Destructible!.SecondaryDamageList.FormKey.ID.ShouldBe(0x200B27u);
            acti.Destructible.SecondaryDamageList.FormKey.ModKey.ShouldBe(TestModKey);
            acti.EnumerateFormLinks().Select(l => l.FormKey.ID).ShouldContain(0x200B27u);
            var written = GameConstants.Starfield.MajorRecord(Write(w => acti.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(PanelLock));
        }
    }

    [Fact]
    public void MoveableStatic_SecondaryDamageListIsALink()
    {
        foreach (var mstt in ReadBoth<IMoveableStaticGetter>(Record("MSTT", Hex(Barrel)),
                     f => MoveableStatic.CreateFromBinary(f),
                     (s, p) => MoveableStaticBinaryOverlay.MoveableStaticFactory(s, p)))
        {
            mstt.EditorID.ShouldBe("HzTest_Test_Barrel_Explode_Fire");
            // HzTest_2DL_Test_Barrel (208DCA:Starfield.esm)
            mstt.Destructible!.SecondaryDamageList.FormKey.ID.ShouldBe(0x208DCAu);
            var written = GameConstants.Starfield.MajorRecord(Write(w => mstt.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(Barrel));
        }
    }
}
