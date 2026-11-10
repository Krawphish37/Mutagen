using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// LGDI fields named after xEdit: MNAM 'Add Into Legendary', GNAM quality mods {tier, OMOD}, HNAM/INAM 'Quality
// Allowed/Disallowed Keyword' {tier, referenced mod index, keyword}, LNAM 'Condition Index Map' {rank, reference mod}.
public class LegendaryItemNamingTests
{
    private static readonly ModKey StarfieldKey = ModKey.FromNameAndExtension("Starfield.esm");
    private static readonly ModKey Sfta01Key = ModKey.FromNameAndExtension("sfta01.esm");
    private static readonly ModKey Sfbgs00dKey = ModKey.FromNameAndExtension("SFBGS00D.esm");

    // SFTA01_AddInto_DefaultLegendaryWeapons (002796:sfta01.esm). sfta01.esm is a medium master, so its own
    // FormIDs (FD01xxxx in the file) are written here in full-master form (01xxxxxx, Starfield.esm = 00).
    private const string AddIntoMnam = "6AE71500";
    private const string AddIntoGnam =
        "01000000 8C270001 02000000 8D270001 03000000 8E270001 04000000 94270001 05000000 95270001";
    private const string AddIntoHnam =
        "01000000 00000000 847D0001 02000000 00000000 847D0001 03000000 00000000 847D0001 " +
        "04000000 00000000 847D0001 05000000 00000000 847D0001";

    // DefaultLegendaryHuman (03B6C4:Starfield.esm) as overridden in SFBGS00D.esm: LNAM (12 entries) and the 12
    // CITC + CTDA blocks after it, unchanged.
    private const string HumanLnamBlock =
        "4C4E414D6000000000000100000000000000020000000000000003000000000000000400000001000000010000000100000002000000010000000300" +
        "000001000000040000000200000001000000020000000200000002000000030000000200000004000000434954430400020000004354444120002000" +
        "00000000803F3002F94470983100000000000000000000000000FFFFFFFF43544441200064000000C88E07015000F944000000000000000000000000" +
        "00000000FFFFFFFF43495443040002000000435444412000200000000000803F3002F94470983100000000000000000000000000FFFFFFFF43544441" +
        "200064000000CA8E07015000F94400000000000000000000000000000000FFFFFFFF43495443040002000000435444412000200000000000803F3002" +
        "F94470983100000000000000000000000000FFFFFFFF43544441200064000000B68E07015000F94400000000000000000000000000000000FFFFFFFF" +
        "43495443040002000000435444412000200000000000803F3002F94470983100000000000000000000000000FFFFFFFF43544441200064000000C28E" +
        "07015000F94400000000000000000000000000000000FFFFFFFF43495443040002000000435444412000200000000000803F3002F944709831000000" +
        "00000000000000000000FFFFFFFF43544441200064000000C48E07015000F94400000000000000000000000000000000FFFFFFFF4349544304000200" +
        "0000435444412000200000000000803F3002F94470983100000000000000000000000000FFFFFFFF43544441200064000000CC8E07015000F9440000" +
        "0000000000000000000000000000FFFFFFFF43495443040002000000435444412000200000000000803F3002F9447098310000000000000000000000" +
        "0000FFFFFFFF43544441200064000000BC8E07015000F94400000000000000000000000000000000FFFFFFFF43495443040002000000435444412000" +
        "200000000000803F3002F94470983100000000000000000000000000FFFFFFFF43544441200064000000C08E07015000F94400000000000000000000" +
        "000000000000FFFFFFFF43495443040002000000435444412000200000000000803F3002F94470983100000000000000000000000000FFFFFFFF4354" +
        "4441200064000000BA8E07015000F94400000000000000000000000000000000FFFFFFFF43495443040002000000435444412000200000000000803F" +
        "3002F94470983100000000000000000000000000FFFFFFFF43544441200064000000C68E07015000F94400000000000000000000000000000000FFFF" +
        "FFFF43495443040002000000435444412000200000000000803F3002F94470983100000000000000000000000000FFFFFFFF43544441200064000000" +
        "B98E07015000F94400000000000000000000000000000000FFFFFFFF43495443040002000000435444412000200000000000803F3002F94470983100" +
        "000000000000000000000000FFFFFFFF43544441200064000000BE8E07015000F94400000000000000000000000000000000FFFFFFFF";

    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", ""));

    private static ParsingMeta Meta(ModKey mod)
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(
            mod, new[] { new MasterReference() { Master = StarfieldKey } }));
        return new ParsingMeta(GameConstants.Starfield, mod, masters);
    }

    private static byte[] Sub(string type, byte[] content) =>
        Encoding.ASCII.GetBytes(type).Concat(BitConverter.GetBytes(checked((ushort)content.Length))).Concat(content).ToArray();

    private static byte[] Record(uint formId, string edid, params byte[][] subs)
    {
        var body = new[]
        {
            Sub("EDID", Encoding.ASCII.GetBytes(edid + "\0")),
            Sub("ODTY", new byte[4]),
            Sub("DATA", Array.Empty<byte>()),
        }.Concat(subs).SelectMany(x => x).ToArray();
        return Encoding.ASCII.GetBytes("LGDI")
            .Concat(BitConverter.GetBytes(body.Length))
            .Concat(BitConverter.GetBytes(0))
            .Concat(BitConverter.GetBytes(formId))
            .Concat(BitConverter.GetBytes(0))
            .Concat(BitConverter.GetBytes((ushort)555))
            .Concat(BitConverter.GetBytes((ushort)0))
            .Concat(body).ToArray();
    }

    private static IEnumerable<ILegendaryItemGetter> ReadBoth(byte[] bytes, ModKey mod)
    {
        yield return LegendaryItem.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta(mod))));
        var meta = Meta(mod);
        yield return LegendaryItemBinaryOverlay.LegendaryItemFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    private static byte[] Write(ILegendaryItemGetter item, ModKey mod)
    {
        var masters = new MasterReferenceCollection(mod, new[] { new MasterReference() { Master = StarfieldKey } });
        var bundle = new WritingBundle(GameConstants.Starfield)
        {
            MasterReferences = masters,
            SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(masters),
        };
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(stream, bundle, dispose: false))
        {
            item.WriteToBinary(writer);
        }
        return stream.ToArray();
    }

    [Fact]
    public void AddIntoRecord_QualityFields()
    {
        var bytes = Record(0x01002796, "SFTA01_AddInto_DefaultLegendaryWeapons",
            Sub("MNAM", Hex(AddIntoMnam)), Sub("GNAM", Hex(AddIntoGnam)), Sub("HNAM", Hex(AddIntoHnam)));
        foreach (var item in ReadBoth(bytes, Sfta01Key))
        {
            item.AddIntoLegendary.FormKey.ShouldBe(new FormKey(StarfieldKey, 0x15E76A));
            item.QualityMods.ShouldNotBeNull();
            item.QualityMods.Select(m => (m.Tier, m.ObjectMod.FormKey.ID)).ShouldBe(new[]
            {
                (1u, 0x278Cu), (2u, 0x278Du), (3u, 0x278Eu), (4u, 0x2794u), (5u, 0x2795u),
            });
            item.QualityAllowedKeywords.ShouldNotBeNull();
            item.QualityAllowedKeywords.Select(k => k.Tier).ShouldBe(new[] { 1u, 2u, 3u, 4u, 5u });
            item.QualityAllowedKeywords.ShouldAllBe(k => k.ReferencedModIndex == 0u);
            item.QualityAllowedKeywords.ShouldAllBe(k => k.Keyword.FormKey == new FormKey(Sfta01Key, 0x7D84));
            item.QualityDisallowedKeywords.ShouldBeNull();
            Write(item, Sfta01Key).ShouldBe(bytes);
        }
    }

    [Fact]
    public void ConditionIndexMap_ReferenceMod()
    {
        var bytes = Record(0x0003B6C4, "DefaultLegendaryHuman", Hex(HumanLnamBlock));
        foreach (var item in ReadBoth(bytes, Sfbgs00dKey))
        {
            item.LnamEntries.ShouldNotBeNull();
            item.LnamEntries.Select(e => ((uint)e.Slot, e.ReferenceMod)).ShouldBe(
                Enumerable.Range(0, 3).SelectMany(r => Enumerable.Range(1, 4).Select(m => ((uint)r, (uint)m))));
            item.LnamEntries.ShouldAllBe(e => e.Conditions!.Count == 2);
            Write(item, Sfbgs00dKey).ShouldBe(bytes);
        }
    }

    // GNAM of DefaultLegendaryArmor (1336C3:Starfield.esm) as overridden in SFBGS00D.esm: 12 {tier, OMOD} entries,
    // tiers 1-4, every OMOD in Starfield.esm (e.g. mod_Armor_Spacesuit_Quality_02 (11E2BC:Starfield.esm)).
    // xEdit: wbStruct('Quality Mod', [wbLGDIQualityTier, wbFormIDCk('Object Modification', [OMOD])]).
    private const string ArmorGnam =
        "01000000BCE21100 0100000034AE1C00 01000000B6E21100 0200000035AE1C00 02000000B7E21100 02000000BAE21100 " +
        "0300000036AE1C00 03000000B8E21100 03000000B9E21100 040000007FAF0300 0400000080AF0300 040000007DAF0300";

    [Fact]
    public void QualityMods_TierAndObjectModLinks()
    {
        var bytes = Record(0x001336C3, "DefaultLegendaryArmor", Sub("GNAM", Hex(ArmorGnam)));
        var spacesuit02 = new FormKey(StarfieldKey, 0x11E2BC);
        var replacement = new FormKey(Sfbgs00dKey, 0x000900);
        foreach (var item in ReadBoth(bytes, Sfbgs00dKey))
        {
            item.QualityMods.ShouldNotBeNull();
            item.QualityMods.Select(m => m.Tier).ShouldBe(new[] { 1u, 1u, 1u, 2u, 2u, 2u, 3u, 3u, 3u, 4u, 4u, 4u });
            item.QualityMods[0].ObjectMod.FormKey.ShouldBe(spacesuit02);
            item.QualityMods.ShouldAllBe(m => m.ObjectMod.FormKey.ModKey == StarfieldKey);
            Write(item, Sfbgs00dKey).ShouldBe(bytes);

            var links = item.EnumerateFormLinks().ToArray();
            links.Length.ShouldBe(12);
            links.ShouldAllBe(l => l.Type == typeof(IAObjectModificationGetter));

            var copy = (LegendaryItem)item.DeepCopy();
            copy.RemapLinks(new Dictionary<FormKey, FormKey> { { spacesuit02, replacement } });
            copy.QualityMods![0].ObjectMod.FormKey.ShouldBe(replacement);
            copy.QualityMods[0].Tier.ShouldBe(1u);
            copy.QualityMods.Skip(1).ShouldAllBe(m => m.ObjectMod.FormKey != replacement);
            var written = Write(copy, Sfbgs00dKey);
            var gnam = written.AsSpan().IndexOf(Encoding.ASCII.GetBytes("GNAM"));
            BitConverter.ToUInt32(written, gnam + 6).ShouldBe(1u);
            BitConverter.ToUInt32(written, gnam + 6 + 4).ShouldBe(0x01000900u);
        }
    }
}
