using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A Starfield perk rank's PRUC is the Quantum Essence Upgrade Cost, a UInt32 (xEdit's Starfield definitions), not a
// link: all 1,231 perk ranks in Starfield.esm store 2. As a "link" to FormID 000002 it would be changed by FormID
// remapping.
public class PerkRankQuantumCostTests
{
    private static Perk PerkWithCost(uint cost)
    {
        var perk = new Perk(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield);
        perk.Ranks.Add(new PerkRank { QuantumEssenceUpgradeCost = cost });
        return perk;
    }

    [Fact]
    public void Cost_WritesAndReadsBack()
    {
        var bytes = Write(w => PerkWithCost(2).WriteToBinary(w));
        GameConstants.Starfield.MajorRecord(bytes).FindSubrecord(new RecordType("PRUC")).Content.ToArray()
            .ShouldBe(new byte[] { 2, 0, 0, 0 });
        foreach (var perk in ReadBoth<IPerkGetter>(bytes, f => Perk.CreateFromBinary(f), (s, p) => PerkBinaryOverlay.PerkFactory(s, p)))
        {
            perk.Ranks.Single().QuantumEssenceUpgradeCost.ShouldBe(2u);
        }
    }

    [Fact]
    public void Cost_IsNotALink()
    {
        PerkWithCost(2).EnumerateFormLinks().ShouldBeEmpty();
    }
}
