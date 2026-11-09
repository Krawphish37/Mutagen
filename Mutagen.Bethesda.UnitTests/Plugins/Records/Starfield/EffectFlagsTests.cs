using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Effect EFIF is xEdit's Flags (Value as Ratio 0x1, Always Show in Item Card 0x2) and MUID its Index UID (UInt32).
// Mutagen kept both as unknown Int32s. Starfield.esm, EFIF always 4 bytes: ENCH 227x 0, 18x 1, 2x 3, 1x 2;
// SPEL 1340x 0, 104x 1; ALCH 1378x 0, 30x 2, 20x 1.
public class EffectFlagsTests
{
    // Ench_Legendary_Helmet_FortifyOxygen (0690A6:Starfield.esm): one effect, EFIF 3, MUID 1
    private const string FortifyOxygen = "454449442400456E63685F4C6567656E646172795F48656C6D65745F466F72746966794F787967656E004F424E4418000000000000000000000000000000000000000000000000004F44545904000000000046554C4C0400802D0300454E49541B0000000000000000000000000000000600000000000000000000000045464944040082A81400454649540C00CDCC4C3E0000000000000000435444412000000108120000803F45030000B0BA1B00000000000000000000000000FFFFFFFF454649460400030000004D554944040001000000";

    private static IEnumerable<IObjectEffectGetter> Read(byte[] bytes)
        => ReadBoth<IObjectEffectGetter>(bytes, f => ObjectEffect.CreateFromBinary(f), (s, p) => ObjectEffectBinaryOverlay.ObjectEffectFactory(s, p));

    [Fact]
    public void FortifyOxygen_Reads()
    {
        foreach (var ench in Read(Record("ENCH", Hex(FortifyOxygen))))
        {
            var effect = ench.Effects.Single();
            effect.Flags.ShouldBe(Effect.Flag.ValueAsRatio | Effect.Flag.AlwaysShowInItemCard);
            effect.IndexUid.ShouldBe(1u);
        }
    }

    [Fact]
    public void FortifyOxygen_WritesSameBytes()
    {
        foreach (var ench in Read(Record("ENCH", Hex(FortifyOxygen))))
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => ench.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(FortifyOxygen));
        }
    }

    [Fact]
    public void AlwaysShowInItemCard()
    {
        // The 16th effect of Ench_Clothing_Common (13DDE0:Starfield.esm): EFIF 2, MUID 0x11 (condition left out)
        var bytes = Record("ENCH",
            Subrecord("EFID", "F3-DD-13-00"),
            Subrecord("EFIT", "00-00-A0-40-00-00-00-00-00-00-00-00"),
            Subrecord("MNAM", "D3-DD-13-00"),
            Subrecord("EFIF", "02-00-00-00"),
            Subrecord("MUID", "11-00-00-00"));
        foreach (var ench in Read(bytes))
        {
            var effect = ench.Effects.Single();
            effect.Flags.ShouldBe(Effect.Flag.AlwaysShowInItemCard);
            effect.IndexUid.ShouldBe(0x11u);
        }
    }
}
