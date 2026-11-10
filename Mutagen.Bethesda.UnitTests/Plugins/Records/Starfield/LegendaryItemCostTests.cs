using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// LGDI FNAM (re-roll costs), KNAM (pick costs) and JNAM (quality upgrade costs) are flat arrays of
// { u32 rank or tier, u32 count, FormID item } (xEdit wbLGDIRankSlotArray, no count prefix).
// Bytes are from DefaultLegendaryWeapons (15E76A:Starfield.esm) as overridden in SFBGS00D.esm, whose
// only master is Starfield.esm: 01033E3D = Y2_X-Tech_Misc (033E3D:SFBGS00D.esm), 0000000F = Credits
// (00000F:Starfield.esm). All 188 cost items in Starfield.esm and the 32 official plugins are MISC.
public class LegendaryItemCostTests
{
    private static readonly ModKey StarfieldKey = ModKey.FromNameAndExtension("Starfield.esm");
    private static readonly ModKey Sfbgs00dKey = ModKey.FromNameAndExtension("SFBGS00D.esm");
    private static readonly FormKey XTech = new(Sfbgs00dKey, 0x033E3D);
    private static readonly FormKey Credits = new(StarfieldKey, 0x00000F);

    private const string Fnam =
        "00000000 01000000 3D3E0301 00000000 A8610000 0F000000 " +
        "01000000 02000000 3D3E0301 01000000 50C30000 0F000000 " +
        "02000000 03000000 3D3E0301 02000000 A0860100 0F000000";

    private const string Knam =
        "00000000 03000000 3D3E0301 00000000 F8240100 0F000000 " +
        "01000000 06000000 3D3E0301 01000000 F0490200 0F000000 " +
        "02000000 09000000 3D3E0301 02000000 E0930400 0F000000";

    private const string Jnam =
        "01000000 10270000 0F000000 02000000 A8610000 0F000000 03000000 50C30000 0F000000 " +
        "04000000 F0490200 0F000000 04000000 03000000 3D3E0301 05000000 E0930400 0F000000 " +
        "05000000 05000000 3D3E0301";

    private static byte[] Hex(string s)
    {
        s = s.Replace(" ", "");
        return Enumerable.Range(0, s.Length / 2).Select(i => Convert.ToByte(s.Substring(i * 2, 2), 16)).ToArray();
    }

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(
            Sfbgs00dKey,
            new[] { new MasterReference() { Master = StarfieldKey } }));
        return new ParsingMeta(GameConstants.Starfield, Sfbgs00dKey, masters);
    }

    private static void WriteSubrecord(MemoryStream stream, string type, byte[] content)
    {
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
    }

    private static byte[] MakeRecord()
    {
        var content = new MemoryStream();
        WriteSubrecord(content, "EDID", Encoding.ASCII.GetBytes("DefaultLegendaryWeapons\0"));
        // ODTY and the empty DATA are always written, so the source has them too
        WriteSubrecord(content, "ODTY", new byte[4]);
        WriteSubrecord(content, "DATA", Array.Empty<byte>());
        WriteSubrecord(content, "FNAM", Hex(Fnam));
        WriteSubrecord(content, "KNAM", Hex(Knam));
        WriteSubrecord(content, "JNAM", Hex(Jnam));
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("LGDI"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x0015E76A));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)555));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static IEnumerable<ILegendaryItemGetter> ReadBoth(byte[] bytes)
    {
        yield return LegendaryItem.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return LegendaryItemBinaryOverlay.LegendaryItemFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    private static byte[] Write(ILegendaryItemGetter item)
    {
        var masters = new MasterReferenceCollection(
            Sfbgs00dKey,
            new[] { new MasterReference() { Master = StarfieldKey } });
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
    public void Costs_ReadAsRankCountItem()
    {
        foreach (var item in ReadBoth(MakeRecord()))
        {
            item.RerollCosts.ShouldNotBeNull();
            item.RerollCosts.Select(c => (c.Rank, c.Count, c.Item.FormKey)).ShouldBe(new[]
            {
                (LegendaryItem.StarSlot.First, 1u, XTech),
                (LegendaryItem.StarSlot.First, 25000u, Credits),
                (LegendaryItem.StarSlot.Second, 2u, XTech),
                (LegendaryItem.StarSlot.Second, 50000u, Credits),
                (LegendaryItem.StarSlot.Third, 3u, XTech),
                (LegendaryItem.StarSlot.Third, 100000u, Credits),
            });
            item.PickCosts.ShouldNotBeNull();
            item.PickCosts.Select(c => (c.Rank, c.Count, c.Item.FormKey)).ShouldBe(new[]
            {
                (LegendaryItem.StarSlot.First, 3u, XTech),
                (LegendaryItem.StarSlot.First, 75000u, Credits),
                (LegendaryItem.StarSlot.Second, 6u, XTech),
                (LegendaryItem.StarSlot.Second, 150000u, Credits),
                (LegendaryItem.StarSlot.Third, 9u, XTech),
                (LegendaryItem.StarSlot.Third, 300000u, Credits),
            });
            item.QualityUpgradeCosts.ShouldNotBeNull();
            item.QualityUpgradeCosts.Select(c => (c.Tier, c.Count, c.Item.FormKey)).ShouldBe(new[]
            {
                (1u, 10000u, Credits),
                (2u, 25000u, Credits),
                (3u, 50000u, Credits),
                (4u, 150000u, Credits),
                (4u, 3u, XTech),
                (5u, 300000u, Credits),
                (5u, 5u, XTech),
            });
        }
    }

    [Fact]
    public void Costs_WriteByteIdentical()
    {
        var bytes = MakeRecord();
        foreach (var item in ReadBoth(bytes))
        {
            Write(item).ShouldBe(bytes);
            Write(item.DeepCopy()).ShouldBe(bytes);
        }
    }

    [Fact]
    public void Costs_EnumerateFormLinks_YieldsEveryItem()
    {
        foreach (var item in ReadBoth(MakeRecord()))
        {
            var links = item.EnumerateFormLinks().Select(l => l.FormKey).ToArray();
            links.Count(k => k == XTech).ShouldBe(3 + 3 + 2);
            links.Count(k => k == Credits).ShouldBe(3 + 3 + 5);
            item.EnumerateFormLinks().Where(l => l.FormKey == Credits || l.FormKey == XTech)
                .ShouldAllBe(l => l.Type == typeof(IItemGetter));
        }
    }

    [Fact]
    public void Costs_RemapLinks_ChangesEveryItem()
    {
        var newKey = new FormKey(Sfbgs00dKey, 0x000900);
        foreach (var item in ReadBoth(MakeRecord()))
        {
            var copy = (LegendaryItem)item.DeepCopy();
            copy.RemapLinks(new Dictionary<FormKey, FormKey> { { Credits, newKey } });
            copy.RerollCosts!.Where((_, i) => i % 2 == 1).ShouldAllBe(c => c.Item.FormKey == newKey);
            copy.RerollCosts!.Where((_, i) => i % 2 == 0).ShouldAllBe(c => c.Item.FormKey == XTech);
            copy.PickCosts!.Count(c => c.Item.FormKey == newKey).ShouldBe(3);
            copy.QualityUpgradeCosts!.Count(c => c.Item.FormKey == newKey).ShouldBe(5);
            copy.EnumerateFormLinks().ShouldNotContain(l => l.FormKey == Credits);

            // The remapped FormID is written in the cost slot: 01000900 (SFBGS00D.esm is master index 1)
            var written = Write(copy);
            var fnam = written.AsSpan().IndexOf(Encoding.ASCII.GetBytes("FNAM"));
            BitConverter.ToUInt32(written, fnam + 6 + 12 + 8).ShouldBe(0x01000900u);
        }
    }
}
