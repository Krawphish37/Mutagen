using System.IO.Abstractions;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Mutagen.Bethesda.UnitTests.Plugins.Masters;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// LGDI CNAM/DNAM entries are { u32 slot, u32 index, FormID keyword }.  The middle u32 is a small
// index (0-10 in all Bethesda plugins) into the record's LegendaryMods (BNAM) for that slot, not
// a FormID, so it must not be master-index remapped, relinked or enumerated as a link.
public class LegendaryFilterTests
{
    private static readonly ModKey TestModKey = ModKey.FromNameAndExtension("Test.esm");
    private static readonly ModKey OtherModKey = ModKey.FromNameAndExtension("Other.esm");
    private static readonly ModKey BaseModKey = ModKey.FromNameAndExtension("Base.esm");

    private static ParsingMeta Meta()
    {
        // Test.esm masters: [Other.esm, Base.esm]
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(
            TestModKey,
            new[]
            {
                new MasterReference() { Master = OtherModKey },
                new MasterReference() { Master = BaseModKey },
            }));
        return new ParsingMeta(GameConstants.Starfield, TestModKey, masters);
    }

    private static void WriteSubrecord(MemoryStream stream, string type, byte[] content)
    {
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
    }

    private static byte[] Filter(uint slot, uint index, uint keyword)
    {
        return new[] { slot, index, keyword }.SelectMany(BitConverter.GetBytes).ToArray();
    }

    private static byte[] MakeLegendaryItemBytes()
    {
        var content = new MemoryStream();
        WriteSubrecord(content, "EDID", Encoding.ASCII.GetBytes("TestLegendaryItem\0"));
        // Include filter: slot 0, index 3, keyword 000801:Base.esm
        WriteSubrecord(content, "CNAM", Filter(0, 3, 0x01000801));
        // Exclude filter: slot 1, index 10, keyword 000802:Other.esm
        WriteSubrecord(content, "DNAM", Filter(1, 10, 0x00000802));
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("LGDI"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x02000800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)555));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static ILegendaryItemGetter ReadDirect(byte[] bytes)
    {
        return LegendaryItem.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
    }

    private static ILegendaryItemGetter ReadOverlay(byte[] bytes)
    {
        var meta = Meta();
        return LegendaryItemBinaryOverlay.LegendaryItemFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    private static IEnumerable<ILegendaryItemGetter> ReadBoth(byte[] bytes)
    {
        yield return ReadDirect(bytes);
        yield return ReadOverlay(bytes);
    }

    private static uint ReadFilterIndex(byte[] plugin, string type)
    {
        var marker = Encoding.ASCII.GetBytes(type);
        var pos = plugin.AsSpan().IndexOf(marker);
        pos.ShouldBeGreaterThan(0);
        BitConverter.ToUInt16(plugin, pos + 4).ShouldBe((ushort)12);
        return BitConverter.ToUInt32(plugin, pos + 6 + 4);
    }

    [Fact]
    public void ReferencedModIndex_ReadsRawInteger()
    {
        foreach (var item in ReadBoth(MakeLegendaryItemBytes()))
        {
            var include = item.IncludeFilters.ShouldHaveSingleItem();
            include.Slot.ShouldBe(LegendaryItem.StarSlot.First);
            include.ReferencedModIndex.ShouldBe(3u);
            include.Keyword.FormKey.ShouldBe(new FormKey(BaseModKey, 0x801));

            var exclude = item.ExcludeFilters.ShouldHaveSingleItem();
            exclude.Slot.ShouldBe(LegendaryItem.StarSlot.Second);
            exclude.ReferencedModIndex.ShouldBe(10u);
            exclude.Keyword.FormKey.ShouldBe(new FormKey(OtherModKey, 0x802));
        }
    }

    [Fact]
    public void EnumerateFormLinks_OnlyYieldsKeywords()
    {
        foreach (var item in ReadBoth(MakeLegendaryItemBytes()))
        {
            item.EnumerateFormLinks().Select(x => x.FormKey).ShouldBe(
                new[] { new FormKey(BaseModKey, 0x801), new FormKey(OtherModKey, 0x802) },
                ignoreOrder: true);
        }
    }

    [Theory, MutagenAutoData]
    public void Write_ReorderedMasters_KeepsReferencedModIndex(
        IFileSystem fileSystem,
        DirectoryPath existingFolder)
    {
        foreach (var item in ReadBoth(MakeLegendaryItemBytes()))
        {
            WriteAndCheck(item, fileSystem, existingFolder, remap: false);
        }
    }

    [Theory, MutagenAutoData]
    public void RemapLinks_KeepsReferencedModIndex(
        IFileSystem fileSystem,
        DirectoryPath existingFolder)
    {
        foreach (var item in ReadBoth(MakeLegendaryItemBytes()))
        {
            WriteAndCheck(item, fileSystem, existingFolder, remap: true);
        }
    }

    private static void WriteAndCheck(
        ILegendaryItemGetter item,
        IFileSystem fileSystem,
        DirectoryPath existingFolder,
        bool remap)
    {
        {
            var patchKey = ModKey.FromNameAndExtension("Patch.esm");
            var patch = new StarfieldMod(patchKey, StarfieldRelease.Starfield);
            var copy = (LegendaryItem)item.DeepCopy();
            if (remap)
            {
                // Relinking whatever an index would look like as a FormKey must not touch it
                copy.RemapLinks(new Dictionary<FormKey, FormKey>()
                {
                    { new FormKey(OtherModKey, 3), new FormKey(patchKey, 0x900) },
                    { new FormKey(OtherModKey, 10), new FormKey(patchKey, 0x901) },
                });
            }
            patch.LegendaryItems.Add(copy);

            // Base.esm becomes master index 0, Other.esm index 1 (the reverse of the source)
            var lo = new[]
            {
                MastersTestUtil.GetFlags(BaseModKey, MasterStyle.Full),
                MastersTestUtil.GetFlags(OtherModKey, MasterStyle.Full),
                MastersTestUtil.GetFlags(TestModKey, MasterStyle.Full),
            };
            var modPath = Path.Combine(existingFolder, patchKey.FileName);
            patch.BeginWrite
                .ToPath(modPath)
                .WithLoadOrder(lo)
                .WithFileSystem(fileSystem)
                .Write();

            var bytes = fileSystem.File.ReadAllBytes(modPath);
            ReadFilterIndex(bytes, "CNAM").ShouldBe(3u);
            ReadFilterIndex(bytes, "DNAM").ShouldBe(10u);

            using var reimport = StarfieldMod.Create(StarfieldRelease.Starfield)
                .FromPath(modPath)
                .WithLoadOrder(lo)
                .WithFileSystem(fileSystem)
                .Construct();
            reimport.ModHeader.MasterReferences.Select(m => m.Master)
                .ShouldBe(new[] { BaseModKey, OtherModKey, TestModKey });
        }
    }
}
