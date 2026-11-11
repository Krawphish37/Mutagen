using System.IO.Abstractions;
using System.IO.Compression;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A Mutagen-only rewrite of an unchanged Starfield plugin gives back the file it read, byte for byte. These cover the
// layout Mutagen didn't keep before (found by rewriting Starfield.esm, kgcdoom.esm, darkstar.esm and weap_starlight.esm):
// - C136: the empty top-level groups the Creation Kit writes (Starfield.esm NAVM, DIAL, INFO, DLBR, SCEN)
// - C137: a cell's temporary children group before its persistent one (1,511 Starfield.esm cells, e.g.
//   PackInShipPISMODHabTaiyoBattleStations2L2W1HTopBStorageCell (026804:Starfield.esm))
// - C138: navmeshes after placed objects in a temporary group (96 darkstar.esm cells)
// - C139: compressed records whose zlib stream Mutagen's compressor doesn't reproduce (13 Starfield.esm navmeshes,
//   e.g. 017F17:Starfield.esm; darkstar.esm's 1,017 NPC_, e.g. Combat_B_Generic_Naginata02_Template_AutopilotAI
//   (002549:Starfield.esm))
// - C134: a block editor metadata component without its DAT2 (OverlayBlockkgcDoomDR023World (00BA67:kgcdoom.esm))
// - C140: a new gameplay options group can be written
// Each kept layout has a public export/import, so a mod rebuilt from another form can be written back the same way.
public class ExactFileLayoutTests
{
    private const int HeaderLength = 24;

    private readonly record struct Grp(int Pos, int Size, uint Label, int Type);

    private static List<Grp> Groups(byte[] bytes, bool topLevelOnly = false)
    {
        var ret = new List<Grp>();
        Walk(bytes, HeaderLength + BitConverter.ToInt32(bytes, 4), bytes.Length, ret, topLevelOnly);
        return ret;
    }

    private static void Walk(byte[] bytes, int start, int end, List<Grp> ret, bool topLevelOnly)
    {
        var p = start;
        while (p < end)
        {
            var size = BitConverter.ToInt32(bytes, p + 4);
            if (Encoding.ASCII.GetString(bytes, p, 4) == "GRUP")
            {
                ret.Add(new Grp(p, size, BitConverter.ToUInt32(bytes, p + 8), BitConverter.ToInt32(bytes, p + 12)));
                if (!topLevelOnly) Walk(bytes, p + HeaderLength, p + size, ret, topLevelOnly);
                p += size;
            }
            else
            {
                p += HeaderLength + size;
            }
        }
    }

    // The records directly inside a group: (position, total length, type)
    private static List<(int Pos, int Length, string Type)> Records(byte[] bytes, Grp group)
    {
        var ret = new List<(int, int, string)>();
        var p = group.Pos + HeaderLength;
        while (p < group.Pos + group.Size)
        {
            var type = Encoding.ASCII.GetString(bytes, p, 4);
            var length = type == "GRUP" ? BitConverter.ToInt32(bytes, p + 4) : HeaderLength + BitConverter.ToInt32(bytes, p + 4);
            ret.Add((p, length, type));
            p += length;
        }
        return ret;
    }

    private static uint Label(string type) => BitConverter.ToUInt32(Encoding.ASCII.GetBytes(type));

    // Two neighbouring byte runs swapped: [a][b] becomes [b][a]
    private static byte[] Swap(byte[] bytes, int aPos, int aLength, int bLength)
    {
        var ret = bytes.ToArray();
        bytes.AsSpan(aPos + aLength, bLength).CopyTo(ret.AsSpan(aPos));
        bytes.AsSpan(aPos, aLength).CopyTo(ret.AsSpan(aPos + bLength));
        return ret;
    }

    private static byte[] Insert(byte[] bytes, int pos, byte[] insert)
    {
        return bytes.AsSpan(0, pos).ToArray().Concat(insert).Concat(bytes.AsSpan(pos).ToArray()).ToArray();
    }

    private static ModPath OutPath(IFileSystem fileSystem, ModPath filePath)
    {
        var dir = fileSystem.Path.Combine(fileSystem.Path.GetDirectoryName(filePath.Path)!, "out");
        fileSystem.Directory.CreateDirectory(dir);
        return new ModPath(filePath.ModKey, fileSystem.Path.Combine(dir, filePath.ModKey.FileName));
    }

    private static byte[] WriteMod(IStarfieldModGetter mod, ModPath filePath, IFileSystem fileSystem)
    {
        mod.BeginWrite
            .ToPath(filePath)
            .WithNoLoadOrder()
            .NoModKeySync()
            .WithFileSystem(fileSystem)
            .Write();
        return fileSystem.File.ReadAllBytes(filePath);
    }

    private static IStarfieldMod ReadMutable(byte[] bytes, ModPath filePath, IFileSystem fileSystem)
    {
        fileSystem.File.WriteAllBytes(filePath, bytes);
        return StarfieldMod.Create(StarfieldRelease.Starfield)
            .FromPath(filePath)
            .WithNoLoadOrder()
            .Mutable()
            .WithFileSystem(fileSystem)
            .Construct();
    }

    // The file read as an overlay and as a full import, each written back
    private static IEnumerable<(string Reader, byte[] Written)> RewriteBoth(byte[] bytes, ModPath filePath, IFileSystem fileSystem)
    {
        var outPath = OutPath(fileSystem, filePath);
        fileSystem.File.WriteAllBytes(filePath, bytes);
        using (var overlay = StarfieldMod.Create(StarfieldRelease.Starfield)
                   .FromPath(filePath)
                   .WithNoLoadOrder()
                   .WithFileSystem(fileSystem)
                   .Construct())
        {
            yield return ("overlay", WriteMod(overlay, outPath, fileSystem));
        }
        yield return ("full import", WriteMod(ReadMutable(bytes, filePath, fileSystem), outPath, fileSystem));
    }

    private static void ShouldRewriteIdentical(byte[] input, ModPath filePath, IFileSystem fileSystem)
    {
        foreach (var (reader, written) in RewriteBoth(input, filePath, fileSystem))
        {
            written.ShouldBe(input, reader);
        }
    }

    private static Cell AddCell(StarfieldMod mod, int persistent, int temporary, int navmeshes)
    {
        var cell = new Cell(mod) { EditorID = "TestCell" };
        for (int i = 0; i < persistent; i++) cell.Persistent.Add(new PlacedObject(mod) { EditorID = $"TestPersistent{i}" });
        for (int i = 0; i < temporary; i++) cell.Temporary.Add(new PlacedObject(mod) { EditorID = $"TestTemporary{i}" });
        for (int i = 0; i < navmeshes; i++) cell.NavigationMeshes.Add(new NavigationMesh(mod) { EditorID = $"TestNavmesh{i}" });
        mod.Cells.Add(new CellBlock
        {
            BlockNumber = 0,
            GroupType = GroupTypeEnum.InteriorCellBlock,
            SubBlocks =
            {
                new CellSubBlock
                {
                    BlockNumber = 0,
                    GroupType = GroupTypeEnum.InteriorCellSubBlock,
                    Cells = { cell },
                },
            },
        });
        return cell;
    }

    // C137: the temporary children group written before the persistent one
    private static byte[] TemporaryFirst(byte[] bytes)
    {
        var groups = Groups(bytes);
        var persistent = groups.Single(g => g.Type == (int)GroupTypeEnum.CellPersistentChildren);
        var temporary = groups.Single(g => g.Type == (int)GroupTypeEnum.CellTemporaryChildren);
        temporary.Pos.ShouldBe(persistent.Pos + persistent.Size);
        return Swap(bytes, persistent.Pos, persistent.Size, temporary.Size);
    }

    // C138: the temporary group's last navmesh moved after the placed object that follows it (Mutagen writes the
    // navmeshes first: NAVM NAVM REFR REFR becomes NAVM REFR NAVM REFR)
    private static byte[] NavmeshAfterFirstTemporary(byte[] bytes)
    {
        var temporary = Groups(bytes).Single(g => g.Type == (int)GroupTypeEnum.CellTemporaryChildren);
        var records = Records(bytes, temporary);
        var i = records.FindLastIndex(r => r.Type == "NAVM");
        records[i + 1].Type.ShouldBe("REFR");
        return Swap(bytes, records[i].Pos, records[i].Length, records[i + 1].Length);
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void Cell_TemporaryGroupFirst_WrittenBackAsRead(IFileSystem fileSystem, ModPath filePath, StarfieldMod mod)
    {
        AddCell(mod, persistent: 2, temporary: 1, navmeshes: 1);
        var input = TemporaryFirst(WriteMod(mod, filePath, fileSystem));

        ShouldRewriteIdentical(input, filePath, fileSystem);
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void Cell_NavmeshAmongPlacedObjects_WrittenBackAsRead(IFileSystem fileSystem, ModPath filePath, StarfieldMod mod)
    {
        // REFR, NAVM, REFR: the navmesh between two placed objects
        AddCell(mod, persistent: 1, temporary: 2, navmeshes: 1);
        var input = NavmeshAfterFirstTemporary(WriteMod(mod, filePath, fileSystem));

        ShouldRewriteIdentical(input, filePath, fileSystem);
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void Cell_BothLayouts_WrittenBackAsRead(IFileSystem fileSystem, ModPath filePath, StarfieldMod mod)
    {
        AddCell(mod, persistent: 1, temporary: 2, navmeshes: 2);
        var input = TemporaryFirst(NavmeshAfterFirstTemporary(WriteMod(mod, filePath, fileSystem)));

        ShouldRewriteIdentical(input, filePath, fileSystem);
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void Cell_ChangedChildren_WrittenInMutagensLayout(IFileSystem fileSystem, ModPath filePath, StarfieldMod mod)
    {
        AddCell(mod, persistent: 1, temporary: 1, navmeshes: 1);
        var input = TemporaryFirst(WriteMod(mod, filePath, fileSystem));
        var read = ReadMutable(input, filePath, fileSystem);
        var cell = read.EnumerateMajorRecords<ICell>().Single();
        cell.Temporary.Add(new PlacedObject(read) { EditorID = "Added" });

        var groups = Groups(WriteMod(read, OutPath(fileSystem, filePath), fileSystem));
        var persistent = groups.Single(g => g.Type == (int)GroupTypeEnum.CellPersistentChildren);
        var temporary = groups.Single(g => g.Type == (int)GroupTypeEnum.CellTemporaryChildren);
        temporary.Pos.ShouldBe(persistent.Pos + persistent.Size);
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void Cell_ChildGroupLayout_ExportAndImport(IFileSystem fileSystem, ModPath filePath, StarfieldMod mod)
    {
        AddCell(mod, persistent: 1, temporary: 2, navmeshes: 2);
        var mutagenLayout = WriteMod(mod, filePath, fileSystem);
        var input = TemporaryFirst(NavmeshAfterFirstTemporary(mutagenLayout));
        var read = ReadMutable(input, filePath, fileSystem);
        var outPath = OutPath(fileSystem, filePath);

        // A copy rebuilt without the layout writes Mutagen's; given the exported value, the file as read
        var cell = read.EnumerateMajorRecords<ICell>().Single();
        var layout = cell.GetChildGroupLayout().ShouldNotBeNull();
        cell.SetChildGroupLayout(null);
        cell.GetChildGroupLayout().ShouldBeNull();
        WriteMod(read, outPath, fileSystem).ShouldBe(mutagenLayout);
        cell.SetChildGroupLayout(layout);
        WriteMod(read, outPath, fileSystem).ShouldBe(input);

        Should.Throw<FormatException>(() => cell.SetChildGroupLayout("not a layout"));

        // A cell written in Mutagen's layout has none to export
        ReadMutable(mutagenLayout, filePath, fileSystem).EnumerateMajorRecords<ICellGetter>().Single()
            .GetChildGroupLayout().ShouldBeNull();
    }

    // C136: an empty top-level group as the Creation Kit writes it (the header's last 8 bytes as read)
    private static byte[] EmptyGroup(string label)
    {
        var ret = new byte[HeaderLength];
        Encoding.ASCII.GetBytes("GRUP").CopyTo(ret, 0);
        BitConverter.GetBytes(HeaderLength).CopyTo(ret, 4);
        Encoding.ASCII.GetBytes(label).CopyTo(ret, 8);
        BitConverter.GetBytes(0x00211234).CopyTo(ret, 16);
        return ret;
    }

    private static byte[] WithEmptyGroups(byte[] bytes)
    {
        var top = Groups(bytes, topLevelOnly: true);
        var keywords = top.Single(g => g.Label == Label("KYWD"));
        // One after the KYWD group, two at its end in a row, and one before every group
        var ret = Insert(bytes, keywords.Pos + keywords.Size, EmptyGroup("NAVM").Concat(EmptyGroup("DIAL")).ToArray());
        return Insert(ret, top[0].Pos, EmptyGroup("SCEN"));
    }

    private static StarfieldMod ModWithGroups(StarfieldMod mod)
    {
        mod.Keywords.AddNew("TestKeyword");
        mod.Globals.AddNew("TestGlobal");
        return mod;
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void EmptyTopLevelGroups_WrittenBackInTheirPlaces(IFileSystem fileSystem, ModPath filePath, StarfieldMod mod)
    {
        var input = WithEmptyGroups(WriteMod(ModWithGroups(mod), filePath, fileSystem));

        ShouldRewriteIdentical(input, filePath, fileSystem);
        WriteMod(((StarfieldMod)ReadMutable(input, filePath, fileSystem)).DeepCopy(), OutPath(fileSystem, filePath), fileSystem)
            .ShouldBe(input, "a copy");
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void EmptyTopLevelGroups_AfterARemovedGroup_LeftOut(IFileSystem fileSystem, ModPath filePath, StarfieldMod mod)
    {
        var input = WithEmptyGroups(WriteMod(ModWithGroups(mod), filePath, fileSystem));
        var read = ReadMutable(input, filePath, fileSystem);
        read.Keywords.Clear();

        var labels = Groups(WriteMod(read, OutPath(fileSystem, filePath), fileSystem), topLevelOnly: true)
            .Select(g => g.Label)
            .ToArray();
        labels.ShouldBe(new[] { Label("SCEN"), Label("GLOB") });
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void EmptyTopLevelGroups_ExportAndImport(IFileSystem fileSystem, ModPath filePath, StarfieldMod mod)
    {
        var mutagenLayout = WriteMod(ModWithGroups(mod), filePath, fileSystem);
        var input = WithEmptyGroups(mutagenLayout);
        var read = ReadMutable(input, filePath, fileSystem);
        var outPath = OutPath(fileSystem, filePath);

        var groups = read.GetEmptyTopLevelGroups().ShouldNotBeNull();
        read.SetEmptyTopLevelGroups(null);
        read.GetEmptyTopLevelGroups().ShouldBeNull();
        WriteMod(read, outPath, fileSystem).ShouldBe(mutagenLayout);

        // A mod rebuilt from scratch, given the exported value
        var fresh = new StarfieldMod(read.ModKey, StarfieldRelease.Starfield);
        fresh.DeepCopyIn(read);
        fresh.SetEmptyTopLevelGroups(null);
        WriteMod(fresh, outPath, fileSystem).ShouldBe(mutagenLayout);
        fresh.SetEmptyTopLevelGroups(groups);
        WriteMod(fresh, outPath, fileSystem).ShouldBe(input);

        Should.Throw<FormatException>(() => fresh.SetEmptyTopLevelGroups("not groups"));
        mod.GetEmptyTopLevelGroups().ShouldBeNull();
    }

    // C139: the KYWD record's zlib stream replaced by another encoding of the same bytes (zlib's fastest level, 78 01),
    // which Mutagen's compressor (best level, 78 DA) doesn't produce
    private static byte[] OtherCompression(byte[] bytes, out byte[] originalContent, out byte[] otherContent)
    {
        var keywords = Groups(bytes, topLevelOnly: true).Single(g => g.Label == Label("KYWD"));
        var record = Records(bytes, keywords).Single();
        var contentLength = BitConverter.ToInt32(bytes, record.Pos + 4);
        originalContent = bytes.AsSpan(record.Pos + HeaderLength, contentLength).ToArray();
        originalContent[4].ShouldBe((byte)0x78);
        originalContent[5].ShouldBe((byte)0xDA);

        var uncompressed = new MemoryStream();
        using (var z = new ZLibStream(new MemoryStream(originalContent, 4, contentLength - 4), CompressionMode.Decompress))
        {
            z.CopyTo(uncompressed);
        }
        var recompressed = new MemoryStream();
        recompressed.Write(originalContent, 0, 4);
        using (var z = new ZLibStream(recompressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            z.Write(uncompressed.ToArray());
        }
        otherContent = recompressed.ToArray();
        otherContent[5].ShouldNotBe((byte)0xDA);

        var delta = otherContent.Length - contentLength;
        var ret = bytes.AsSpan(0, record.Pos + HeaderLength).ToArray()
            .Concat(otherContent)
            .Concat(bytes.AsSpan(record.Pos + HeaderLength + contentLength).ToArray())
            .ToArray();
        BitConverter.GetBytes(otherContent.Length).CopyTo(ret, record.Pos + 4);
        BitConverter.GetBytes(keywords.Size + delta).CopyTo(ret, keywords.Pos + 4);
        return ret;
    }

    private static StarfieldMod ModWithCompressedKeyword(StarfieldMod mod)
    {
        var keyword = mod.Keywords.AddNew("TestKeywordWithALongerEditorIdToCompress");
        keyword.Name = "A keyword whose name is long enough that compressing it does something";
        keyword.IsCompressed = true;
        return mod;
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void CompressedRecord_OtherEncoder_WrittenBackAsRead(IFileSystem fileSystem, ModPath filePath, StarfieldMod mod)
    {
        var input = OtherCompression(WriteMod(ModWithCompressedKeyword(mod), filePath, fileSystem), out _, out _);

        ShouldRewriteIdentical(input, filePath, fileSystem);
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void CompressedRecord_Changed_CompressedAnew(IFileSystem fileSystem, ModPath filePath, StarfieldMod mod)
    {
        var input = OtherCompression(WriteMod(ModWithCompressedKeyword(mod), filePath, fileSystem), out _, out _);
        var read = ReadMutable(input, filePath, fileSystem);
        read.Keywords.Single().EditorID = "Changed";

        var written = WriteMod(read, OutPath(fileSystem, filePath), fileSystem);
        var keywords = Groups(written, topLevelOnly: true).Single(g => g.Label == Label("KYWD"));
        var record = Records(written, keywords).Single();
        written[record.Pos + HeaderLength + 5].ShouldBe((byte)0xDA);
        StarfieldMod.Create(StarfieldRelease.Starfield)
            .FromPath(OutPath(fileSystem, filePath))
            .WithNoLoadOrder()
            .WithFileSystem(fileSystem)
            .Mutable()
            .Construct()
            .Keywords.Single().EditorID.ShouldBe("Changed");
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void CompressedRecord_ExportAndImport(IFileSystem fileSystem, ModPath filePath, StarfieldMod mod)
    {
        var mutagenLayout = WriteMod(ModWithCompressedKeyword(mod), filePath, fileSystem);
        var input = OtherCompression(mutagenLayout, out _, out var otherContent);
        var read = ReadMutable(input, filePath, fileSystem);
        var outPath = OutPath(fileSystem, filePath);
        var keyword = read.Keywords.Single();

        var data = keyword.GetOriginalCompressedData().ShouldNotBeNull();
        data.ShouldBe(otherContent);
        keyword.SetOriginalCompressedData(null);
        keyword.GetOriginalCompressedData().ShouldBeNull();
        WriteMod(read, outPath, fileSystem).ShouldBe(mutagenLayout);

        // A record rebuilt from scratch, given the exported value
        var fresh = new StarfieldMod(read.ModKey, StarfieldRelease.Starfield);
        var freshKeyword = fresh.Keywords.AddNew(keyword.FormKey);
        freshKeyword.DeepCopyIn(keyword);
        freshKeyword.SetOriginalCompressedData(null);
        WriteMod(fresh, outPath, fileSystem).ShouldBe(mutagenLayout);
        freshKeyword.SetOriginalCompressedData(data);
        WriteMod(fresh, outPath, fileSystem).ShouldBe(input);

        Should.Throw<FormatException>(() => freshKeyword.SetOriginalCompressedData(new byte[] { 1, 2, 3 }));

        // The overlay keeps it too
        fileSystem.File.WriteAllBytes(filePath, input);
        using var overlay = StarfieldMod.Create(StarfieldRelease.Starfield)
            .FromPath(filePath)
            .WithNoLoadOrder()
            .WithFileSystem(fileSystem)
            .Construct();
        overlay.Keywords.Single().GetOriginalCompressedData().ShouldBe(otherContent);
    }

    // C134: OverlayBlockkgcDoomDR023World (00BA67:kgcdoom.esm), form version 582. Its BGSBlockEditorMetaData_Component
    // is an empty BFCB..BFCE block; Mutagen added a 49-byte DAT2 of zeros
    private const string KgcDoomBlock = "454449441E004F7665726C6179426C6F636B6B6763446F6F6D4452303233576F726C6400424643422100424753426C6F636B456469746F724D657461446174615F436F6D706F6E656E7400424643450000414E414D2300446174615C5445525241494E5C6B6763446F6F6D4452303233576F726C642E62746400444E414D08000400000004000000454E414D08000000FAC300007A44464E414D20009C558E55D2559C55DF5519564056BE55B655E9553858E155AE55D9565D56ED55474E414D010000484E414D02000000494E414D0100004A4E414D0100004B4E414D010000574847540400FFFF7FFF4E414D310D004F7665726C6179426C6F636B004E414D32080000000000000000004E414D33080000000000000000004E414D34080000000000000000004E414D35040000000000";

    private static byte[] Sfbk(byte[] content, ushort formVersion)
    {
        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("SFBK"));
        record.Write(BitConverter.GetBytes(content.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(formVersion));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(content);
        return record.ToArray();
    }

    [Fact]
    public void BlockEditorMetaData_WithoutDat2_WrittenBackWithout()
    {
        var content = Hex(KgcDoomBlock);
        foreach (var block in ReadBoth<ISurfaceBlockGetter>(Sfbk(content, 582), f => SurfaceBlock.CreateFromBinary(f), (s, p) => SurfaceBlockBinaryOverlay.SurfaceBlockFactory(s, p)))
        {
            block.EditorID.ShouldBe("OverlayBlockkgcDoomDR023World");
            var component = block.Components.OfType<IBlockEditorMetaDataComponentGetter>().ShouldHaveSingleItem();
            component.DAT2DataTypeState.HasFlag(BlockEditorMetaDataComponent.DAT2DataType.Absent).ShouldBeTrue();
            GameConstants.Starfield.MajorRecord(Write(w => block.WriteToBinary(w))).Content.ToArray().ShouldBe(content);

            // Once it is no longer marked absent, it is written
            var copy = block.DeepCopy();
            var copied = copy.Components.OfType<BlockEditorMetaDataComponent>().Single();
            copied.DAT2DataTypeState.HasFlag(BlockEditorMetaDataComponent.DAT2DataType.Absent).ShouldBeTrue();
            copied.DAT2DataTypeState &= ~BlockEditorMetaDataComponent.DAT2DataType.Absent;
            copied.SourceEditorID = "Source";
            var written = GameConstants.Starfield.MajorRecord(Write(w => copy.WriteToBinary(w)));
            written.TryFindSubrecord(new RecordType("DAT2")).ShouldNotBeNull();
        }
    }

    [Fact]
    public void BlockEditorMetaData_New_WritesItsDat2()
    {
        var block = new SurfaceBlock(new FormKey(TestModKey, 0x123456), StarfieldRelease.Starfield);
        block.Components.Add(new BlockEditorMetaDataComponent());
        var written = GameConstants.Starfield.MajorRecord(Write(w => block.WriteToBinary(w)));
        written.TryFindSubrecord(new RecordType("DAT2")).ShouldNotBeNull();
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void GameplayOptionsGroup_New_IsWritten(IFileSystem fileSystem, ModPath filePath, StarfieldMod mod)
    {
        mod.GameplayOptionsGroups.AddNew("TestOptionsGroup");
        var read = ReadMutable(WriteMod(mod, filePath, fileSystem), filePath, fileSystem);

        var group = read.GameplayOptionsGroups.Single();
        group.EditorID.ShouldBe("TestOptionsGroup");
        group.Options.ShouldBeOfType<GameplayOptionsGroupLeaf>();
    }
}
