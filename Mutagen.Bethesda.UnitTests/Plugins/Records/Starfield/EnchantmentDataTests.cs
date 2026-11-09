using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Enchantment ENIT (27 bytes) per xEdit: Cost, Flags, Cast Type (1 byte), Amount, Target Type (1), Enchant Type (1),
// Charge Time, Base Enchantment [ENCH], Worn Restrictions [FLST]. Mutagen kept it as raw bytes, so the two FormIDs
// were hidden from FormID remapping.
public class EnchantmentDataTests
{
    // ENIT of _RENAME_TestGrenade_GravWave (ENCH 2CC1F3): cost 11, Fire and Forget, amount 11, Aimed, Base Enchantment itself
    private const string GravWave = "0B-00-00-00-00-00-00-00-01-0B-00-00-00-02-06-00-00-00-00-F3-C1-2C-00-00-00-00-00";
    // ENIT of Ench_SleepGas (ENCH 3E9942): Extend Duration On Recast, Fire and Forget, Touch
    private const string SleepGas = "00-00-00-00-04-00-00-00-01-00-00-00-00-01-06-00-00-00-00-00-00-00-00-00-00-00-00";

    private static IEnumerable<IObjectEffectGetter> Read(string enit) =>
        ReadBoth<IObjectEffectGetter>(Record("ENCH", Subrecord("ENIT", enit)), f => ObjectEffect.CreateFromBinary(f), (s, p) => ObjectEffectBinaryOverlay.ObjectEffectFactory(s, p));

    [Fact]
    public void GravWave_Reads()
    {
        foreach (var ench in Read(GravWave))
        {
            ench.EnchantmentCost.ShouldBe(11);
            ench.Flags.ShouldBe((ObjectEffect.Flag)0);
            ench.CastType.ShouldBe(CastType.FireAndForget);
            ench.EnchantmentAmount.ShouldBe(11);
            ench.TargetType.ShouldBe(TargetType.Aimed);
            ench.EnchantType.ShouldBe(ObjectEffect.EnchantTypeEnum.Enchantment);
            ench.ChargeTime.ShouldBe(0f);
            ench.BaseEnchantment.FormKey.ID.ShouldBe(0x2CC1F3u);
            ench.WornRestrictions.IsNull.ShouldBeTrue();
            ench.EnumerateFormLinks().Select(l => l.FormKey.ID).ShouldContain(0x2CC1F3u);
        }
    }

    [Fact]
    public void SleepGas_Reads()
    {
        foreach (var ench in Read(SleepGas))
        {
            ench.Flags.ShouldBe(ObjectEffect.Flag.ExtendDurationOnRecast);
            ench.TargetType.ShouldBe(TargetType.Touch);
            ench.BaseEnchantment.IsNull.ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData(GravWave)]
    [InlineData(SleepGas)]
    public void Enit_WritesSameBytes(string enit)
    {
        foreach (var ench in Read(enit))
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => ench.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("ENIT")).Content.ToArray().ShouldBe(Hex(enit));
        }
    }
}
