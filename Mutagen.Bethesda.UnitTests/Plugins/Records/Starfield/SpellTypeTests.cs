using System.Reflection;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Spell.SpellType was marked [Flags] though each value is one type, so type 0 (Spell) printed and serialized as no flags
// at all. It is a plain enum now, with xEdit's names (6-9 added; 11 is VoicePower). Real records of Starfield.esm, read
// with both readers: the type reads, prints by its name, and SPIT writes back byte for byte.
public class SpellTypeTests
{
    public static IEnumerable<object[]> Spells() => new[]
    {
        // Env_HitSpell_AFFL_Chance_AAA (2ED9E2:Starfield.esm)
        new object[] { "5350454C8A00000000000000E2D92E00C134200045020400454449441D00456E765F4869745370656C6C5F4146464C5F4368616E63655F414141004F424E4418000000000000000000000000000000000000000000000000004F44545904000000000046554C4C04006B260000455459500400443F010044455343040000000000535049541B00000000000000000000000000000101000000000000000000000000", Spell.SpellType.Spell, "Spell" },
        // AbStarbornDeath (3A289F:Starfield.esm)
        new object[] { "5350454CA3000000000000009F283A00C134200045020100454449441000416253746172626F726E4465617468004F424E4418000000000000000000000000000000000000000000000000004F445459040000000000455459500400443F010044455343040000000000535049541B00000000000000340004000000000000000000000000000000000000454649440400A1283A00454649540C000000803F0000000000000000454649460400000000004D554944040001000000", Spell.SpellType.Ability, "Ability" },
        // Addiction_Stimulants (268D35:Starfield.esm)
        new object[] { "5350454CDC01000000000000358D2600C134200045020800454449441500416464696374696F6E5F5374696D756C616E7473004F424E4418000000000000000000000000000000000000000000000000004F44545904000000000046554C4C040012260000455459500400443F010044455343040000000000535049541B0000000000000000000A000000000000000000000000000000000000454649440400378D2600454649540C000000A0410000000000000000435444412000200108120000803FD6000000368D2600000000000100000000000000FFFFFFFF4D4E414D040023650600454649460400000000004D55494404000100000045464944040062AF1600454649540C000000A0410000000000000000435444412000200108120000803FD600F944368D2600000000000100000000000000FFFFFFFF4D4E414D040036650600454649460400000000004D55494404000500000045464944040090471000454649540C000000A04100000000000000004D4E414D040036650600454649460400000000004D5549440400060000004546494404004CA70200454649540C000000803F0000000000000000454649460400000000004D55494404000200000045464944040097B91E00454649540C000000803F0000000000000000454649460400000000004D554944040004000000", Spell.SpellType.Addiction, "Addiction" },
    };

    private static byte[] Spit(byte[] record) =>
        GameConstants.Starfield.MajorRecord(record).FindSubrecord(new RecordType("SPIT")).Content.ToArray();

    [Fact]
    public void SpellType_IsNotFlags()
    {
        typeof(Spell.SpellType).GetCustomAttribute<FlagsAttribute>().ShouldBeNull();
        ((Spell.SpellType)11).ShouldBe(Spell.SpellType.VoicePower);
        ((Spell.SpellType)8).ToString().ShouldBe("WortCraft");
    }

    [Theory]
    [MemberData(nameof(Spells))]
    public void RealSpells_TypeReadsAndWritesBack(string hex, Spell.SpellType type, string name)
    {
        var bytes = Hex(hex);
        foreach (var spell in ReadBoth<ISpellGetter>(bytes,
                     f => Spell.CreateFromBinary(f), (s, p) => SpellBinaryOverlay.SpellFactory(s, p)))
        {
            spell.Type.ShouldBe(type);
            spell.Type.ToString().ShouldBe(name);
            // The type is in SPIT (the record's texts are localized IDs, which this plugin-less read doesn't resolve)
            Spit(Write(w => spell.WriteToBinary(w))).ShouldBe(Spit(bytes));
        }
    }
}
