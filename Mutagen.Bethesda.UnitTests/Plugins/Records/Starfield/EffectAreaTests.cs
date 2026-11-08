using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A Starfield effect's Area (EFIT) is a float (xEdit's Starfield definitions). Non-zero on 36 spell and 2
// enchantment effects in Starfield.esm; an Int32 read 1.0 as 1,065,353,216 and 5.0 as 1,084,227,584.
public class EffectAreaTests
{
    [Fact]
    public void Spell_EffectArea_ReadsAsFloat()
    {
        // ArtifactPowerCreateVacuum_Spell (SPEL 2C5390): magnitude 4, area 5, duration 15
        var bytes = Record("SPEL", Subrecord("EFID", "7F-5A-2C-00"), Subrecord("EFIT", "00-00-80-40-00-00-A0-40-0F-00-00-00"));
        foreach (var spell in ReadBoth<ISpellGetter>(bytes, f => Spell.CreateFromBinary(f), (s, p) => SpellBinaryOverlay.SpellFactory(s, p)))
        {
            var data = spell.Effects.Single().Data!;
            data.Magnitude.ShouldBe(4f);
            data.Area.ShouldBe(5f);
            data.Duration.ShouldBe(15);
        }
    }

    [Fact]
    public void EffectArea_Writes()
    {
        var spell = new Spell(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield);
        spell.Effects.Add(new Effect { Data = new EffectData { Area = 1f } });
        var written = GameConstants.Starfield.MajorRecord(Write(w => spell.WriteToBinary(w)));
        BitConverter.ToSingle(written.FindSubrecord(new RecordType("EFIT")).Content.Slice(4, 4)).ShouldBe(1f);
    }
}
