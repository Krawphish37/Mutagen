using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C54: record-header flag names whose value was one bit off from xEdit (RegisterRecordDef wbFlagsList) and the data.
// Header flags are read and written raw, so the bytes never changed; only the named member tested the wrong bit.
// Starfield.esm counts (the old value is set on no record of the type in Starfield.esm or the 32 official plugins):
// - WEAP High-Res 1st Person Only is bit 30 (0x4000_0000, 139 weapons, e.g. CreatureRangedAttack_Mouth1_Oxygen
//   3FA258), not 0x2000_0000. ArmorAddon's HiResFirstPersonOnly was already 0x4000_0000.
// - KYWD Restricted is bit 15 (0x8000, 192 keywords, e.g. Weapon_Reward_MQ105_Equalizer_Restricted 0019F4), not 0x8_0000.
// - OMOD Legendary Mod is bit 4 (0x10, 78, e.g. mod_Legendary_Armor_03_Incendiary 002983), not 0x08; Mod Collection is
//   bit 7 (0x80, 182, e.g. mod_Grendel_Quality_TiersAny 06288A), not 0x40.
// - TRNS Around Origin is bit 16 (0x1_0000, 51, e.g. Inv_DefaultTransform_X100_Y155_Z185_BootlegMoonshineThermometer
//   00CF83), not 0x8000.
public class MajorFlagValueTests
{
    private static byte[] WithHeaderFlags(byte[] record, uint flags)
    {
        BitConverter.GetBytes(flags).CopyTo(record, 8);
        return record;
    }

    [Theory]
    [InlineData(typeof(Weapon.MajorFlag), nameof(Weapon.MajorFlag.HighResFirstPersonOnly), 0x4000_0000)]
    [InlineData(typeof(Keyword.MajorFlag), nameof(Keyword.MajorFlag.Restricted), 0x0000_8000)]
    [InlineData(typeof(AObjectModification.MajorFlag), nameof(AObjectModification.MajorFlag.LegendaryMod), 0x10)]
    [InlineData(typeof(AObjectModification.MajorFlag), nameof(AObjectModification.MajorFlag.ModCollection), 0x80)]
    [InlineData(typeof(Transform.MajorFlag), nameof(Transform.MajorFlag.AroundOrigin), 0x0001_0000)]
    public void Values(Type enumType, string name, int expected)
    {
        Convert.ToInt32(Enum.Parse(enumType, name)).ShouldBe(expected);
    }

    [Fact]
    public void Weapon_HighResFirstPersonOnly_ReadsAndWritesBack()
    {
        // Non-Playable | High-Res 1st Person Only, the header flags of 3FA258. The writer adds WEAP's required subrecords
        // to a record that has only an EDID, so the byte-identical check is on a record already holding them.
        var bytes = WithHeaderFlags(Record("WEAP", Subrecord("EDID", "41-00")), 0x4000_0004);
        bytes = Write(w => ((IWeaponGetter)Weapon.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())))).WriteToBinary(w));
        BitConverter.ToUInt32(bytes, 8).ShouldBe(0x4000_0004u);
        foreach (var weap in ReadBoth<IWeaponGetter>(bytes, f => Weapon.CreateFromBinary(f), (s, p) => WeaponBinaryOverlay.WeaponFactory(s, p)))
        {
            weap.MajorRecordFlagsRaw.ShouldBe(0x4000_0004);
            weap.MajorFlags.ShouldBe(Weapon.MajorFlag.NonPlayable | Weapon.MajorFlag.HighResFirstPersonOnly);
            Write(w => weap.WriteToBinary(w)).ShouldBe(bytes);
        }
    }

    [Fact]
    public void Weapon_OldValue_IsNotHighResFirstPersonOnly()
    {
        var bytes = WithHeaderFlags(Record("WEAP", Subrecord("EDID", "41-00")), 0x2000_0000);
        foreach (var weap in ReadBoth<IWeaponGetter>(bytes, f => Weapon.CreateFromBinary(f), (s, p) => WeaponBinaryOverlay.WeaponFactory(s, p)))
        {
            weap.MajorFlags.HasFlag(Weapon.MajorFlag.HighResFirstPersonOnly).ShouldBeFalse();
        }
    }

    [Fact]
    public void Keyword_Restricted_ReadsAndWritesBack()
    {
        var bytes = WithHeaderFlags(Record("KYWD", Subrecord("EDID", "41-00")), 0x8000);
        foreach (var kywd in ReadBoth<IKeywordGetter>(bytes, f => Keyword.CreateFromBinary(f), (s, p) => KeywordBinaryOverlay.KeywordFactory(s, p)))
        {
            kywd.MajorFlags.ShouldBe(Keyword.MajorFlag.Restricted);
            var written = Write(w => kywd.WriteToBinary(w));
            BitConverter.ToUInt32(written, 8).ShouldBe(0x8000u);
        }
    }

    [Fact]
    public void Transform_AroundOrigin_ReadsAndWritesBack()
    {
        var bytes = WithHeaderFlags(Record("TRNS", Subrecord("EDID", "41-00")), 0x1_0000);
        foreach (var trns in ReadBoth<ITransformGetter>(bytes, f => Transform.CreateFromBinary(f), (s, p) => TransformBinaryOverlay.TransformFactory(s, p)))
        {
            trns.MajorFlags.ShouldBe(Transform.MajorFlag.AroundOrigin);
            var written = Write(w => trns.WriteToBinary(w));
            BitConverter.ToUInt32(written, 8).ShouldBe(0x1_0000u);
        }
    }

    [Fact]
    [Trait("Category", "RealData")]
    public void RealData_StarfieldEsm_3FA258()
    {
        var data = Environment.GetEnvironmentVariable("MUTAGEN_STARFIELD_DATA");
        if (string.IsNullOrEmpty(data)) return; // needs the game's Data folder
        var path = Path.Combine(data, "Starfield.esm");
        var key = FormKey.Factory("3FA258:Starfield.esm");
        using (var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield))
        {
            Check(overlay.Weapons[key]);
        }
        var full = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, importMask: new GroupMask(false) { Weapons = true });
        Check(full.Weapons[key]);

        static void Check(IWeaponGetter weap)
        {
            weap.EditorID.ShouldBe("CreatureRangedAttack_Mouth1_Oxygen");
            weap.MajorRecordFlagsRaw.ShouldBe(0x4000_0004);
            weap.MajorFlags.ShouldBe(Weapon.MajorFlag.NonPlayable | Weapon.MajorFlag.HighResFirstPersonOnly);
        }
    }
}
