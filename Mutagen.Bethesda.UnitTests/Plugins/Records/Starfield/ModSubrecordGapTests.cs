using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

/// <summary>
/// Subrecords written by the Creation Kit into mod plugins (form version 582) that the schema did not know,
/// so a lenient read dropped them and a strict read threw "Unexpected subrecord".
/// Layouts are taken from cryomancer_z.esm, kinggathcreations_spaceship.esm, darkstar.esm and kgcdoom.esm.
/// </summary>
public class ModSubrecordGapTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Starfield, TestModKey, masters);
    }

    private static FormKey Fk(uint id) => new(TestModKey, id);

    private static byte[] Sub(string type, byte[] content)
    {
        var ret = new MemoryStream();
        ret.Write(Encoding.ASCII.GetBytes(type));
        ret.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        ret.Write(content);
        return ret.ToArray();
    }

    private static byte[] Sub(string type, uint value) => Sub(type, BitConverter.GetBytes(value));

    private static byte[] ZString(string str) => Encoding.ASCII.GetBytes(str + "\0");

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(x => x).ToArray();

    private static byte[] MakeRecord(string type, params byte[][] subrecords)
    {
        var body = Concat(subrecords);
        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes(type));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)582));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static byte[] Write(IMajorRecordGetter record)
    {
        var masters = new MasterReferenceCollection(TestModKey);
        var bundle = new WritingBundle(GameConstants.Starfield)
        {
            MasterReferences = masters,
            SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(masters),
        };
        var memStream = new MemoryStream();
        using (var writer = new MutagenWriter(memStream, bundle, dispose: false))
        {
            ((IBinaryItem)record).WriteToBinary(writer);
        }
        return memStream.ToArray();
    }

    /// <summary>
    /// The record's subrecords, in order, as (type, content hex)
    /// </summary>
    private static List<(string Type, string Content)> Subrecords(byte[] record)
    {
        var ret = new List<(string, string)>();
        var len = BitConverter.ToInt32(record, 4);
        var pos = 24;
        while (pos < 24 + len)
        {
            var type = Encoding.ASCII.GetString(record, pos, 4);
            var size = BitConverter.ToUInt16(record, pos + 4);
            ret.Add((type, Convert.ToHexString(record, pos + 6, size)));
            pos += 6 + size;
        }
        return ret;
    }

    private static void ShouldKeep(byte[] original, byte[] written, params string[] types)
    {
        Subrecords(written).Where(x => types.Contains(x.Type))
            .ShouldBe(Subrecords(original).Where(x => types.Contains(x.Type)));
    }

    private static IEnumerable<T> ReadBoth<T>(
        byte[] bytes,
        Func<MutagenFrame, T> direct,
        Func<OverlayStream, BinaryOverlayFactoryPackage, T> overlay)
    {
        yield return direct(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return overlay(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    private static byte[] AttackData(float chance)
    {
        var ret = new byte[48];
        BitConverter.GetBytes(1f).CopyTo(ret, 0);
        BitConverter.GetBytes(chance).CopyTo(ret, 4);
        BitConverter.GetBytes(70f).CopyTo(ret, 20);
        return ret;
    }

    private static byte[] DestructionStageData(byte healthPercent, byte index)
    {
        var ret = new byte[24];
        ret[0] = healthPercent;
        ret[1] = index;
        return ret;
    }

    // GetStageDone (59) of quest 0x801, stage 900, run on subject
    private static readonly byte[] ConditionData = Convert.FromHexString(
        "00000000" + "0000803F" + "3B000000" + "01080000" + "84030000" + "00000000" + "00000000" + "FFFFFFFF");

    [Fact]
    public void NpcDestructibleAndAttacks()
    {
        var bytes = MakeRecord("NPC_",
            Sub("EDID", ZString("TestNpc")),
            Sub("OBND", new byte[24]),
            Sub("ODTY", new byte[4]),
            Sub("ACBS", new byte[16]),
            Sub("RNAM", 0x801),
            Sub("DEST", Convert.FromHexString("204E000002000000")),
            Sub("DSTD", DestructionStageData(95, 0)),
            Sub("DSTF", Array.Empty<byte>()),
            Sub("DSTD", DestructionStageData(90, 1)),
            Sub("DSTF", Array.Empty<byte>()),
            Sub("WNAM", 0x802),
            Sub("ATKD", AttackData(1f)),
            Sub("ATKE", ZString("MeleeAttack05")),
            Sub("ATKD", AttackData(2f)),
            Sub("ATKE", ZString("MeleeAttackPower01")),
            Sub("ATKS", 0x803));

        foreach (var npc in ReadBoth<INpcGetter>(bytes,
                     f => Npc.CreateFromBinary(f),
                     (s, p) => NpcBinaryOverlay.NpcFactory(s, p)))
        {
            npc.Destructible.ShouldNotBeNull();
            npc.Destructible.Data.ShouldNotBeNull();
            npc.Destructible.Data.Health.ShouldBe(20000);
            npc.Destructible.Data.DESTCount.ShouldBe((byte)2);
            npc.Destructible.Stages.Select(x => x.HealthPercent).ShouldBe(new byte[] { 95, 90 });
            npc.Skin.FormKey.ShouldBe(Fk(0x802));
            npc.Attacks.Count.ShouldBe(2);
            npc.Attacks.Select(x => x.AttackEvent).ShouldBe(new[] { "MeleeAttack05", "MeleeAttackPower01" });
            npc.Attacks[1].AttackData!.Chance.ShouldBe(2f);
            npc.Attacks[0].RequiredSlot.IsNull.ShouldBeTrue();
            npc.Attacks[1].RequiredSlot.FormKey.ShouldBe(Fk(0x803));

            ShouldKeep(bytes, Write(npc), "DEST", "DSTD", "DSTF", "ATKD", "ATKE", "ATKS");
        }
    }

    [Fact]
    public void PlacedNpcPackInBlueprintMaterialSwapsAndAttachRef()
    {
        var bytes = MakeRecord("ACHR",
            Sub("NAME", 0x801),
            Sub("XWPK", Array.Empty<byte>()),
            Sub("GNAM", 0x802),
            Sub("HNAM", 0x803),
            Sub("INAM", new byte[2]),
            Sub("JNAM", 0x802),
            Sub("LNAM", new byte[4]),
            Sub("XWPK", Array.Empty<byte>()),
            Sub("XBPO", 0x13),
            Sub("XLYR", 0x804),
            Sub("XLMS", 0x805),
            Sub("XATR", 0x806),
            Sub("DATA", new byte[24]));

        foreach (var achr in ReadBoth<IPlacedNpcGetter>(bytes,
                     f => PlacedNpc.CreateFromBinary(f),
                     (s, p) => PlacedNpcBinaryOverlay.PlacedNpcFactory(s, p)))
        {
            achr.GroupedPackIn.ShouldNotBeNull();
            achr.GroupedPackIn.GNAM.FormKey.ShouldBe(Fk(0x802));
            achr.GroupedPackIn.HNAM.FormKey.ShouldBe(Fk(0x803));
            achr.GroupedPackIn.JNAM.FormKey.ShouldBe(Fk(0x802));
            achr.BlueprintPartOrigin.ShouldBe(0x13u);
            achr.Layer.FormKey.ShouldBe(Fk(0x804));
            achr.LayeredMaterialSwaps.ShouldNotBeNull();
            achr.LayeredMaterialSwaps.Select(x => x.FormKey).ShouldBe(new[] { Fk(0x805) });
            achr.AttachRef.FormKey.ShouldBe(Fk(0x806));

            ShouldKeep(bytes, Write(achr), "XWPK", "GNAM", "HNAM", "INAM", "JNAM", "LNAM", "XBPO", "XLMS", "XATR");
        }
    }

    [Fact]
    public void LegendaryItemInam()
    {
        var bytes = MakeRecord("LGDI",
            Sub("EDID", ZString("TestLegendaryItem")),
            Sub("ODTY", new byte[4]),
            Sub("HNAM", Concat(BitConverter.GetBytes(1u), BitConverter.GetBytes(0u), BitConverter.GetBytes(0x801u))),
            Sub("INAM", Concat(
                BitConverter.GetBytes(1u), BitConverter.GetBytes(0u), BitConverter.GetBytes(0x802u),
                BitConverter.GetBytes(1u), BitConverter.GetBytes(1u), BitConverter.GetBytes(0x803u))));

        foreach (var lgdi in ReadBoth<ILegendaryItemGetter>(bytes,
                     f => LegendaryItem.CreateFromBinary(f),
                     (s, p) => LegendaryItemBinaryOverlay.LegendaryItemFactory(s, p)))
        {
            lgdi.QualityAllowedKeywords!.Select(x => x.Keyword.FormKey).ShouldBe(new[] { Fk(0x801) });
            lgdi.QualityDisallowedKeywords!.Select(x => x.Keyword.FormKey).ShouldBe(new[] { Fk(0x802), Fk(0x803) });
            lgdi.QualityDisallowedKeywords!.Select(x => x.ReferencedModIndex).ShouldBe(new[] { 0u, 1u });

            ShouldKeep(bytes, Write(lgdi), "HNAM", "INAM");
        }
    }

    [Fact]
    public void ImpactFootstepExplosion()
    {
        var bytes = MakeRecord("IPCT",
            Sub("EDID", ZString("TestImpact")),
            Sub("DATA", new byte[24]),
            Sub("NAM3", 0x801));

        foreach (var ipct in ReadBoth<IImpactGetter>(bytes,
                     f => Impact.CreateFromBinary(f),
                     (s, p) => ImpactBinaryOverlay.ImpactFactory(s, p)))
        {
            ipct.FootstepExplosion.FormKey.ShouldBe(Fk(0x801));

            ShouldKeep(bytes, Write(ipct), "NAM3");
        }
    }

    [Fact]
    public void RaceXalgAndMorphRace()
    {
        var bytes = MakeRecord("RACE",
            Sub("EDID", ZString("TestRace")),
            Sub("XALG", Convert.FromHexString("0004000000000000")),
            Sub("DESC", ZString(string.Empty)),
            Sub("NAM8", 0x801));

        foreach (var race in ReadBoth<IRaceGetter>(bytes,
                     f => Race.CreateFromBinary(f),
                     (s, p) => RaceBinaryOverlay.RaceFactory(s, p)))
        {
            race.XalgFlags.ShouldBe(XalgFlag.ReferencesAreMoveable);
            race.MorphRace.FormKey.ShouldBe(Fk(0x801));
        }

        // Written from the mutable class, as the overlay of this minimal record has no biped object names to write
        var mutable = Race.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        ShouldKeep(bytes, Write(mutable), "XALG", "NAM8");
    }

    [Fact]
    public void SoundEchoMarkerEchlAndConditions()
    {
        var echl = Convert.FromHexString(
            "1F4A5B69D8967137033563200C8FD48650481A7805851613814A5886FDCFCF950000000000000000");
        var bytes = MakeRecord("SECH",
            Sub("EDID", ZString("TestSoundEchoMarker")),
            Sub("OBND", new byte[24]),
            Sub("ODTY", new byte[4]),
            Sub("ECHL", echl),
            Sub("ECHD", Array.Empty<byte>()),
            Sub("ANAM", new byte[4]),
            Sub("BNAM", new byte[4]),
            Sub("CNAM", new byte[1]),
            Sub("DNAM", new byte[1]),
            Sub("ENAM", new byte[1]),
            Sub("CTDA", ConditionData),
            Sub("ECHE", Array.Empty<byte>()));

        foreach (var sech in ReadBoth<ISoundEchoMarkerGetter>(bytes,
                     f => SoundEchoMarker.CreateFromBinary(f),
                     (s, p) => SoundEchoMarkerBinaryOverlay.SoundEchoMarkerFactory(s, p)))
        {
            sech.ECHL.ShouldNotBeNull();
            sech.ECHL.Start.ShouldBe(new Guid(echl.AsSpan(0, 16)));
            sech.ECHL.Stop.ShouldBe(new Guid(echl.AsSpan(16, 16)));
            sech.DefaultStartMarker.ShouldNotBeNull();
            sech.DefaultStartMarker.Conditions.Count.ShouldBe(1);
            var data = sech.DefaultStartMarker.Conditions[0].Data.ShouldBeAssignableTo<IGetStageDoneConditionDataGetter>()!;
            data.FirstParameter.Link.FormKey.ShouldBe(Fk(0x801));
            data.SecondParameter.ShouldBe(900);

            ShouldKeep(bytes, Write(sech), "ECHL", "CTDA", "ECHD", "ECHE");
        }
    }

    [Fact]
    public void WeatherSettingConditions()
    {
        var bytes = MakeRecord("WTHS",
            Sub("EDID", ZString("TestWeatherSetting")),
            Sub("RFDP", 0x802),
            Sub("RDIF", new byte[8]),
            Sub("CTDA", ConditionData));

        foreach (var wths in ReadBoth<IWeatherSettingGetter>(bytes,
                     f => WeatherSetting.CreateFromBinary(f),
                     (s, p) => WeatherSettingBinaryOverlay.WeatherSettingFactory(s, p)))
        {
            wths.ReflectionParent.FormKey.ShouldBe(Fk(0x802));
            wths.Conditions.Count.ShouldBe(1);
            wths.Conditions[0].Data.ShouldBeAssignableTo<IGetStageDoneConditionDataGetter>();

            ShouldKeep(bytes, Write(wths), "RFDP", "RDIF", "CTDA");
        }
    }

    [Fact]
    public void WorldspaceLightLayers()
    {
        var bytes = MakeRecord("WRLD",
            Sub("EDID", ZString("TestWorldspace")),
            Sub("XEMP", ZString("Data\\Textures\\Cubemaps\\Test.dds")),
            Sub("XCLA", 0),
            Sub("XCLD", ZString("All Layers")),
            Sub("XCLA", 1),
            Sub("XCLD", ZString("Default Light Layer")));

        foreach (var wrld in ReadBoth<IWorldspaceGetter>(bytes,
                     f => Worldspace.CreateFromBinary(f),
                     (s, p) => WorldspaceBinaryOverlay.WorldspaceFactory(s, p)))
        {
            wrld.XCLAs.Select(x => x.XCLA).ShouldBe(new int?[] { 0, 1 });
            wrld.XCLAs.Select(x => x.XCLD).ShouldBe(new[] { "All Layers", "Default Light Layer" });

            ShouldKeep(bytes, Write(wrld), "XCLA", "XCLD");
        }
    }
}
