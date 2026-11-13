using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C167: two Starfield.esm recipes have resources in RequiredPerks: co_Outpost_BuilderTEST01 (2407D9:Starfield.esm) and
// co_suit_mod_T0_LINING_IMPACTLAYER (28BF0D:Starfield.esm), 3 links in all. It's the data, not an older layout: their
// RQPK is a byte-for-byte copy of their FVPA (component, count, curve table), in the same 12-byte struct every RQPK
// has (Perk, Rank, 4 bytes; xEdit wbDefinitionsSF1: Perk, Rank, Unused(4)). The other 420 recipes with an RQPK name
// perks. Mutagen reads what the record holds and writes it back unchanged.
public class ConstructibleRequiredPerksDataTests
{
    // co_suit_mod_T0_LINING_IMPACTLAYER (28BF0D:Starfield.esm), form version 581, record content as in the file without
    // its DESC (a localized string ID; this test reads without strings files)
    private const string Lining = "454449442200636F5F737569745F6D6F645F54305F4C494E494E475F494D504143544C4159455200424E414D0400FF570600465650411800217807000200000000000000C757000002000000000000005251504B1800217807000200000000000000C75700000200000000000000434E414D0400000000004E4E414D02000100534E414D040000000000544E414D0100004C524E4D01000344415441040000000000464E414D0400A8A02C005245434608000000000000000000";

    [Fact]
    public void RequiredPerksCopiedFromComponents_ReadAsStored_AndWrittenBack()
    {
        foreach (var cobj in ReadBoth<IConstructibleObjectGetter>(Record("COBJ", Hex(Lining)), f => ConstructibleObject.CreateFromBinary(f), (s, p) => ConstructibleObjectBinaryOverlay.ConstructibleObjectFactory(s, p)))
        {
            cobj.EditorID.ShouldBe("co_suit_mod_T0_LINING_IMPACTLAYER");
            var components = cobj.ConstructibleComponents.ShouldNotBeNull();
            var perks = cobj.RequiredPerks.ShouldNotBeNull();
            perks.Count.ShouldBe(2);
            perks.Select(x => x.Perk.FormKey.ID).ShouldBe(components.Select(x => x.Component.FormKey.ID));
            perks.Select(x => x.Rank).ShouldBe(new uint[] { 2, 2 });
            perks.Select(x => x.Rank).ShouldBe(components.Select(x => x.RequiredCount));
            GameConstants.Starfield.MajorRecord(Write(w => cobj.WriteToBinary(w))).Content.ToArray().ShouldBe(Hex(Lining));
        }
    }
}
