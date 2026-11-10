using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Destructible data per xEdit (wbDefinitionsSF1 wbDEST):
// - A DAMC resistance's last 4 bytes are a Curve Table link (a FormID read as a UInt32 named Unknown; null in all 5
//   DAMC entries of Starfield.esm, so no value changes, but a mod's link would never have been remapped).
// - DSTD's last float is xEdit's Replacement Model Delay. All 2,336 DSTD in Starfield.esm are 24 bytes, without it,
//   but Mutagen always wrote it, so every destruction stage grew to 28 bytes on write. A Break before it keeps the
//   short form.
// The test reads a whole moveable static from Starfield.esm with both readers and writes it back byte for byte.
public class DestructibleFieldTests
{
    // StatIron (0217E7:Starfield.esm)
    private const string StatIron = "4544494409005374617449726F6E004F424E441800FFDF2ABF01C0E8BE034090BEFF3F053F0120A93E03008C3E4F4454590400000000004F504453500001000000000000000000000000008040020000000000803FCDCC4C3E0000000000000000000000000000000000000000DB0F49400000803F000000000000803F0000803F0000000000000000FFFF7F7F4D4F444C2E004C616E6473636170655C5265736F75726365735C4F72655C5265736F7572636549726F6E4F726530312E6E696600464C4C44040001000000444553540800640000000104000044414D430C00810A060000000000000000004453544418000000002600000000E617020000000000000000000000000044535446000044415441010004";

    [Fact]
    public void ResistanceCurveTable_ShortStageData()
    {
        foreach (var mstt in ReadBoth<IMoveableStaticGetter>(Record("MSTT", Hex(StatIron)), f => MoveableStatic.CreateFromBinary(f), (s, p) => MoveableStaticBinaryOverlay.MoveableStaticFactory(s, p)))
        {
            var dest = mstt.Destructible.ShouldNotBeNull();
            var resistance = dest.Resistances.ShouldNotBeNull().Single();
            resistance.CurveTable.IsNull.ShouldBeTrue();
            dest.Stages.Single().ReplacementModelDelay.ShouldBe(0f);
            var written = GameConstants.Starfield.MajorRecord(Write(w => mstt.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(StatIron));
        }
    }
}
