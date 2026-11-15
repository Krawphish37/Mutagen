using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Records;
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
// C55/C56 (counts over all 370 plugins in the Data folder, Starfield.esm's in brackets):
// - CONT Navmesh - Ignore Erosion is bit 29 (0x2000_0000), not 0x1000_0000, which is Navmesh - Only Cut (set on 5 CONT [2],
//   e.g. Loot_Storage_BossChest_Creature_Rare 142546); no plugin sets bit 29 on a CONT.
// - KEYM and CLFM Non-Playable are bit 2 (0x04), as in xEdit, Skyrim's Key and every other Starfield Non-Playable; no
//   record of any type in any plugin sets 0x02. One KEYM sets 0x04 (kinggathcreations_spaceship.esm 020FDBF3).
// - SCOL Non Occluder is bit 4 (0x10), as STAT's; no SCOL sets a header flag.
// - COBJ List Contains Variants is bit 26 (0x0400_0000): 1,440 COBJ [401, e.g. co_SMC_Shields_Dogstar_104D_Guardian_lvl76
//   00455A; 376 of the 401 create a FLST]; none sets the old 0x0008_0000.
// - AACT Restricted is bit 15 (0x8000), as KYWD's (C54); no AACT sets a header flag.
// - New names for bits set in the data: STAT Navmesh - Ignore Erosion [137], TRNS Apply Translation [323], TERM Non
//   Occluder / Pack-In Use Only / Navmesh - Bounding Box / Ignore Erosion / Ground [28/28/4/129/65], MSTT Navmesh - Only
//   Cut [43], OMOD Property Collection [101], LCTN and WRLD Partial Form (63 and 20, in the official add-ons).
// - Key, Static, StaticCollection, LeveledBaseForm and Region had a MajorFlag enum but no MajorFlags property (Static and
//   StaticCollection's XML said majorFlags, the other three had no attribute); they have one now.
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
    // C55/C56: changed values
    [InlineData(typeof(Container.MajorFlag), nameof(Container.MajorFlag.NavMeshOnlyCut), 0x1000_0000)]
    [InlineData(typeof(Container.MajorFlag), nameof(Container.MajorFlag.NavMeshIgnoreErosion), 0x2000_0000)]
    [InlineData(typeof(Key.MajorFlag), nameof(Key.MajorFlag.NonPlayable), 0x04)]
    [InlineData(typeof(ColorRecord.MajorFlag), nameof(ColorRecord.MajorFlag.NonPlayable), 0x04)]
    [InlineData(typeof(StaticCollection.MajorFlag), nameof(StaticCollection.MajorFlag.NonOccluder), 0x10)]
    [InlineData(typeof(ConstructibleObject.MajorFlag), nameof(ConstructibleObject.MajorFlag.ListContainsVariants), 0x0400_0000)]
    [InlineData(typeof(ActionRecord.MajorFlag), nameof(ActionRecord.MajorFlag.Restricted), 0x0000_8000)]
    // C55: added members
    [InlineData(typeof(Static.MajorFlag), nameof(Static.MajorFlag.NavMeshGenerationIgnoreErosion), 0x2000_0000)]
    [InlineData(typeof(Transform.MajorFlag), nameof(Transform.MajorFlag.ApplyTranslation), 0x0002_0000)]
    [InlineData(typeof(Terminal.MajorFlag), nameof(Terminal.MajorFlag.NonOccluder), 0x10)]
    [InlineData(typeof(Terminal.MajorFlag), nameof(Terminal.MajorFlag.PackInUseOnly), 0x2000)]
    [InlineData(typeof(Terminal.MajorFlag), nameof(Terminal.MajorFlag.NavmeshBoundingBox), 0x0800_0000)]
    [InlineData(typeof(Terminal.MajorFlag), nameof(Terminal.MajorFlag.NavmeshIgnoreErosion), 0x2000_0000)]
    [InlineData(typeof(Terminal.MajorFlag), nameof(Terminal.MajorFlag.NavmeshGround), 0x4000_0000)]
    [InlineData(typeof(MoveableStatic.MajorFlag), nameof(MoveableStatic.MajorFlag.NavMeshGenerationOnlyCut), 0x1000_0000)]
    [InlineData(typeof(AObjectModification.MajorFlag), nameof(AObjectModification.MajorFlag.PropertyCollection), 0x100)]
    [InlineData(typeof(Location.MajorFlag), nameof(Location.MajorFlag.PartialForm), 0x4000)]
    [InlineData(typeof(Worldspace.MajorFlag), nameof(Worldspace.MajorFlag.PartialForm), 0x4000)]
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

    // Header flags set, then read with the full import and the overlay; each reads the expected members and writes the
    // same bytes back. The record is normalised once first (the writer adds a type's required subrecords).
    private static void CheckFlags<T, TFlag>(
        byte[] record,
        uint flags,
        Func<MutagenFrame, T> full,
        Func<OverlayStream, BinaryOverlayFactoryPackage, T> overlay,
        Func<T, TFlag> majorFlags,
        TFlag expected)
        where T : IMajorRecordGetter, IBinaryItem
    {
        var bytes = WithHeaderFlags(record, flags);
        bytes = Write(w => full(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta()))).WriteToBinary(w));
        BitConverter.ToUInt32(bytes, 8).ShouldBe(flags);
        foreach (var rec in ReadBoth(bytes, full, overlay))
        {
            rec.MajorRecordFlagsRaw.ShouldBe(unchecked((int)flags));
            majorFlags(rec).ShouldBe(expected);
            Write(w => rec.WriteToBinary(w)).ShouldBe(bytes);
        }
    }

    private static byte[] Edid(string sig, params byte[][] more) => Record(sig, new[] { Subrecord("EDID", "41-00") }.Concat(more).ToArray());

    [Fact]
    public void Container_IgnoreErosion_IsBit29()
    {
        CheckFlags<IContainerGetter, Container.MajorFlag>(Edid("CONT"), 0x2000_0000,
            f => Container.CreateFromBinary(f), (s, p) => ContainerBinaryOverlay.ContainerFactory(s, p),
            r => r.MajorFlags, Container.MajorFlag.NavMeshIgnoreErosion);
        // Bit 28 (2 CONT in Starfield.esm) is Only Cut alone, no longer Ignore Erosion too
        CheckFlags<IContainerGetter, Container.MajorFlag>(Edid("CONT"), 0x1000_0000,
            f => Container.CreateFromBinary(f), (s, p) => ContainerBinaryOverlay.ContainerFactory(s, p),
            r => r.MajorFlags, Container.MajorFlag.NavMeshOnlyCut);
    }

    [Fact]
    public void Key_NonPlayable_IsBit2()
    {
        // 0x2004, the header flags of kinggathcreations_spaceship.esm 020FDBF3
        CheckFlags<IKeyGetter, Key.MajorFlag>(Edid("KEYM"), 0x2004,
            f => Key.CreateFromBinary(f), (s, p) => KeyBinaryOverlay.KeyFactory(s, p),
            r => r.MajorFlags, Key.MajorFlag.NonPlayable | Key.MajorFlag.PackInUseOnly);
        CheckFlags<IKeyGetter, bool>(Edid("KEYM"), 0x02,
            f => Key.CreateFromBinary(f), (s, p) => KeyBinaryOverlay.KeyFactory(s, p),
            r => r.MajorFlags.HasFlag(Key.MajorFlag.NonPlayable), false);
    }

    [Fact]
    public void ColorRecord_NonPlayable_IsBit2()
    {
        var clfm = Edid("CLFM", Subrecord("CNAM", "11-22-33-00"), Subrecord("FNAM", "01-00-00-00"));
        CheckFlags<IColorRecordGetter, ColorRecord.MajorFlag>(clfm, 0x04,
            f => ColorRecord.CreateFromBinary(f), (s, p) => ColorRecordBinaryOverlay.ColorRecordFactory(s, p),
            r => r.MajorFlags, ColorRecord.MajorFlag.NonPlayable);
    }

    [Fact]
    public void StaticCollection_NonOccluder_IsBit4()
    {
        CheckFlags<IStaticCollectionGetter, StaticCollection.MajorFlag>(Edid("SCOL"), 0x10,
            f => StaticCollection.CreateFromBinary(f), (s, p) => StaticCollectionBinaryOverlay.StaticCollectionFactory(s, p),
            r => r.MajorFlags, StaticCollection.MajorFlag.NonOccluder);
    }

    [Fact]
    public void ConstructibleObject_ListContainsVariants_IsBit26()
    {
        CheckFlags<IConstructibleObjectGetter, ConstructibleObject.MajorFlag>(Edid("COBJ"), 0x0400_0000,
            f => ConstructibleObject.CreateFromBinary(f), (s, p) => ConstructibleObjectBinaryOverlay.ConstructibleObjectFactory(s, p),
            r => r.MajorFlags, ConstructibleObject.MajorFlag.ListContainsVariants);
    }

    [Fact]
    public void ActionRecord_Restricted_IsBit15()
    {
        CheckFlags<IActionRecordGetter, ActionRecord.MajorFlag>(Edid("AACT"), 0x8000,
            f => ActionRecord.CreateFromBinary(f), (s, p) => ActionRecordBinaryOverlay.ActionRecordFactory(s, p),
            r => r.MajorFlags, ActionRecord.MajorFlag.Restricted);
    }

    [Fact]
    public void Static_IgnoreErosion_Bit29()
    {
        CheckFlags<IStaticGetter, Static.MajorFlag>(Edid("STAT"), 0x2000_0000,
            f => Static.CreateFromBinary(f), (s, p) => StaticBinaryOverlay.StaticFactory(s, p),
            r => r.MajorFlags, Static.MajorFlag.NavMeshGenerationIgnoreErosion);
    }

    [Fact]
    public void Transform_ApplyTranslation_Bit17()
    {
        CheckFlags<ITransformGetter, Transform.MajorFlag>(Edid("TRNS"), 0x3_0000,
            f => Transform.CreateFromBinary(f), (s, p) => TransformBinaryOverlay.TransformFactory(s, p),
            r => r.MajorFlags, Transform.MajorFlag.AroundOrigin | Transform.MajorFlag.ApplyTranslation);
    }

    [Fact]
    public void Terminal_NewMembers()
    {
        CheckFlags<ITerminalGetter, Terminal.MajorFlag>(Edid("TERM"), 0x6800_2010,
            f => Terminal.CreateFromBinary(f), (s, p) => TerminalBinaryOverlay.TerminalFactory(s, p),
            r => r.MajorFlags,
            Terminal.MajorFlag.NonOccluder | Terminal.MajorFlag.PackInUseOnly | Terminal.MajorFlag.NavmeshBoundingBox
            | Terminal.MajorFlag.NavmeshIgnoreErosion | Terminal.MajorFlag.NavmeshGround);
    }

    [Fact]
    public void MoveableStatic_OnlyCut_Bit28()
    {
        // 0x1000_0100, the header flags of TRP_ExplodingTank_Cryo_Vertical 00A2FA
        CheckFlags<IMoveableStaticGetter, MoveableStatic.MajorFlag>(Edid("MSTT"), 0x1000_0100,
            f => MoveableStatic.CreateFromBinary(f), (s, p) => MoveableStaticBinaryOverlay.MoveableStaticFactory(s, p),
            r => r.MajorFlags, MoveableStatic.MajorFlag.MustUpdateAnims | MoveableStatic.MajorFlag.NavMeshGenerationOnlyCut);
    }

    [Fact]
    public void Location_PartialForm_Bit14()
    {
        CheckFlags<ILocationGetter, Location.MajorFlag>(Edid("LCTN"), 0x4000,
            f => Location.CreateFromBinary(f), (s, p) => LocationBinaryOverlay.LocationFactory(s, p),
            r => r.MajorFlags, Location.MajorFlag.PartialForm);
    }

    [Fact]
    [Trait("Category", "RealData")]
    public void RealData_StarfieldEsm_C55()
    {
        var data = Environment.GetEnvironmentVariable("MUTAGEN_STARFIELD_DATA");
        if (string.IsNullOrEmpty(data)) return; // needs the game's Data folder
        var path = Path.Combine(data, "Starfield.esm");
        using (var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield))
        {
            Check(overlay);
        }
        var full = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, importMask: new GroupMask(false)
        {
            Containers = true, Keys = true, Colors = true, StaticCollections = true, ConstructibleObjects = true,
            Actions = true, Statics = true, Transforms = true, Terminals = true, MoveableStatics = true,
            ObjectModifications = true,
        });
        Check(full);

        static void One<T>(T rec, string edid, int raw, Func<T, bool> check)
            where T : IMajorRecordGetter
        {
            rec.EditorID.ShouldBe(edid);
            rec.MajorRecordFlagsRaw.ShouldBe(raw);
            check(rec).ShouldBeTrue();
        }

        static void Check(IStarfieldModGetter mod)
        {
            One(mod.Containers[FormKey.Factory("142546:Starfield.esm")], "Loot_Storage_BossChest_Creature_Rare", 0x1000_0000,
                r => r.MajorFlags == Container.MajorFlag.NavMeshOnlyCut);
            One(mod.Statics[FormKey.Factory("018F5A:Starfield.esm")], "CatIndDeckSmSideToWalk01", 0x2000_0000,
                r => r.MajorFlags == Static.MajorFlag.NavMeshGenerationIgnoreErosion);
            One(mod.Transforms[FormKey.Factory("005179:Starfield.esm")], "Inv_DefaultTransform_Z015_ScienceGlassSmalls_Scaled52", 0x0002_0000,
                r => r.MajorFlags == Transform.MajorFlag.ApplyTranslation);
            One(mod.ConstructibleObjects[FormKey.Factory("00455A:Starfield.esm")], "co_SMC_Shields_Dogstar_104D_Guardian_lvl76", 0x0400_0000,
                r => r.MajorFlags == ConstructibleObject.MajorFlag.ListContainsVariants);
            One(mod.Terminals[FormKey.Factory("0029A4:Starfield.esm")], "LC017_RobotTurretControl", 0x6000_0000,
                r => r.MajorFlags == (Terminal.MajorFlag.NavmeshIgnoreErosion | Terminal.MajorFlag.NavmeshGround));
            One(mod.Terminals[FormKey.Factory("191E8F:Starfield.esm")], "CF04_Computer_DumbroskyCabin", 0x2010,
                r => r.MajorFlags == (Terminal.MajorFlag.NonOccluder | Terminal.MajorFlag.PackInUseOnly));
            One(mod.MoveableStatics[FormKey.Factory("00A2F9:Starfield.esm")], "TRP_ExplodingTank_Cryo_Horizontal", 0x1000_0000,
                r => r.MajorFlags == MoveableStatic.MajorFlag.NavMeshGenerationOnlyCut);
            One(mod.ObjectModifications[FormKey.Factory("050733:Starfield.esm")], "_Template_mod_Armor_Spacesuit_CarryWeight", 0x100,
                r => r.MajorFlags == AObjectModification.MajorFlag.PropertyCollection);

            // Counts of each flag across the type (from a raw header census of Starfield.esm)
            mod.Containers.Count(r => r.MajorFlags.HasFlag(Container.MajorFlag.NavMeshOnlyCut)).ShouldBe(2);
            mod.Containers.Count(r => r.MajorFlags.HasFlag(Container.MajorFlag.NavMeshIgnoreErosion)).ShouldBe(0);
            mod.Statics.Count(r => r.MajorFlags.HasFlag(Static.MajorFlag.NavMeshGenerationIgnoreErosion)).ShouldBe(137);
            mod.Transforms.Count(r => r.MajorFlags.HasFlag(Transform.MajorFlag.ApplyTranslation)).ShouldBe(323);
            mod.ConstructibleObjects.Count(r => r.MajorFlags.HasFlag(ConstructibleObject.MajorFlag.ListContainsVariants)).ShouldBe(401);
            mod.Terminals.Count(r => r.MajorFlags.HasFlag(Terminal.MajorFlag.NavmeshIgnoreErosion)).ShouldBe(129);
            mod.MoveableStatics.Count(r => r.MajorFlags.HasFlag(MoveableStatic.MajorFlag.NavMeshGenerationOnlyCut)).ShouldBe(43);
            mod.ObjectModifications.Count(r => r.MajorFlags.HasFlag(AObjectModification.MajorFlag.PropertyCollection)).ShouldBe(101);
            mod.Keys.Count(r => r.MajorFlags.HasFlag(Key.MajorFlag.PackInUseOnly)).ShouldBe(197);
            // No record of these types sets the corrected bit or the old one in Starfield.esm
            mod.Keys.Count(r => (r.MajorRecordFlagsRaw & 0x06) != 0).ShouldBe(0);
            mod.Colors.Count(r => (r.MajorRecordFlagsRaw & 0x06) != 0).ShouldBe(0);
            mod.StaticCollections.Count(r => (r.MajorRecordFlagsRaw & 0x14) != 0).ShouldBe(0);
            mod.Actions.Count(r => (r.MajorRecordFlagsRaw & 0x0008_8000) != 0).ShouldBe(0);
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
