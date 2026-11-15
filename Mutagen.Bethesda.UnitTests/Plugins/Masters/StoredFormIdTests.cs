using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Masters;

// C203: StoredFormIdMixIn.GetStoredFormIds lists where a record's FormIDs sit and how each is stored, from the record
// definitions (the writer notes where it puts each FormID), for both readers.
public class StoredFormIdTests
{
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");

    private static byte[] Sub(string type, byte[] content) =>
        Encoding.ASCII.GetBytes(type).Concat(BitConverter.GetBytes((ushort)content.Length)).Concat(content).ToArray();

    private static byte[] Record(string type, uint flags, uint formId, byte[] content)
    {
        var s = new MemoryStream();
        s.Write(Encoding.ASCII.GetBytes(type));
        s.Write(BitConverter.GetBytes(content.Length));
        s.Write(BitConverter.GetBytes(flags));
        s.Write(BitConverter.GetBytes(formId));
        s.Write(Convert.FromHexString("0000000046020000"));
        s.Write(content);
        return s.ToArray();
    }

    private static byte[] Group(string label, params byte[][] records)
    {
        var content = records.SelectMany(r => r).ToArray();
        var s = new MemoryStream();
        s.Write("GRUP"u8);
        s.Write(BitConverter.GetBytes(24 + content.Length));
        s.Write(Encoding.ASCII.GetBytes(label));
        s.Write(BitConverter.GetBytes(0));
        s.Write(new byte[8]);
        s.Write(content);
        return s.ToArray();
    }

    // One master (Starfield.esm, Full): a plugin of the given size whose FLST (header given) links to its own 000801
    // stored as given, and to Starfield.esm's 000ABC
    private static (MockFileSystem Fs, string Path, ModKey Plugin, LoadOrder<IModMasterStyledGetter> Lo) Make(
        MasterStyle size, uint headerFlags, uint listHeader, uint ownLink)
    {
        var plugin = ModKey.FromFileName("Stored.esm");
        var file = Record("TES4", headerFlags, 0,
                Sub("HEDR", Convert.FromHexString("8FC2753F0300000004080000"))
                    .Concat(Sub("MAST", Encoding.ASCII.GetBytes("Starfield.esm\0"))).ToArray())
            .Concat(Group("FLST",
                Record("FLST", 0, listHeader, Sub("EDID", Encoding.ASCII.GetBytes("List\0"))
                    .Concat(Sub("LNAM", BitConverter.GetBytes(ownLink)))
                    .Concat(Sub("LNAM", BitConverter.GetBytes(0x00000ABCu))).ToArray())))
            .ToArray();
        var fs = new MockFileSystem();
        var path = System.IO.Path.Combine("C:", "Data", plugin.FileName);
        fs.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        fs.File.WriteAllBytes(path, file);
        var lo = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
            new KeyedMasterStyle(plugin, size),
        });
        return (fs, path, plugin, lo);
    }

    private static void Check(MasterStyle size, uint headerFlags, uint listHeader, StoredFormIdForm headerForm,
        uint ownLink, StoredFormIdForm ownLinkForm)
    {
        var (fs, path, plugin, lo) = Make(size, headerFlags, listHeader, ownLink);
        var param = new BinaryReadParameters { FileSystem = fs, MasterFlagsLookup = lo };
        var full = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, param);
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, param);
        foreach (var mod in new IStarfieldModGetter[] { full, overlay })
        {
            var ids = mod.FormLists.Single().GetStoredFormIds(mod, lo);
            ids.Count.ShouldBe(3, string.Join(" | ", ids));
            ids[0].ShouldBe(new StoredFormIdPosition(null, 0, 0, listHeader, headerForm, true, new FormKey(plugin, 0x803),
                StoredFormIdLocation.Header));
            ids[1].ShouldBe(new StoredFormIdPosition(new RecordType("LNAM"), 0, 0, ownLink, ownLinkForm, true,
                new FormKey(plugin, 0x801), StoredFormIdLocation.Written));
            ids[2].ShouldBe(new StoredFormIdPosition(new RecordType("LNAM"), 1, 0, 0x00000ABC, StoredFormIdForm.Full, false,
                new FormKey(StarfieldEsm, 0xABC), StoredFormIdLocation.Written));
        }
    }

    [Fact]
    public void SmallPlugin_FeHeader_FullIndexLink() =>
        Check(MasterStyle.Small, 0x101, 0xFE000803, StoredFormIdForm.Small, 0x01000801, StoredFormIdForm.Full);

    [Fact]
    public void MediumPlugin_FullIndexHeader_FdLink() =>
        Check(MasterStyle.Medium, 0x401, 0x01000803, StoredFormIdForm.Full, 0xFD000801, StoredFormIdForm.Medium);

    [Fact]
    public void FullPlugin_LinkPastTheList() =>
        Check(MasterStyle.Full, 0x1, 0x01000803, StoredFormIdForm.Full, 0x02000801, StoredFormIdForm.PastTheList);

    [Fact]
    public void SmallPlugin_FdFormIsPastTheList()
    {
        // A Small plugin has no FD form of its own: FD + its medium index (0) reads as itself, past the list
        Check(MasterStyle.Small, 0x101, 0xFE000803, StoredFormIdForm.Small, 0xFD000801, StoredFormIdForm.PastTheList);
    }

    // Each subrecord's content in a record's bytes (header included), by type and turn
    private static Dictionary<(string Type, int Turn), (int Start, int Length)> Subrecords(byte[] record)
    {
        var ret = new Dictionary<(string, int), (int, int)>();
        var turns = new Dictionary<string, int>();
        int p = 24;
        while (p + 6 <= record.Length)
        {
            var type = Encoding.ASCII.GetString(record, p, 4);
            int length = BitConverter.ToUInt16(record, p + 4);
            var turn = turns.GetValueOrDefault(type);
            turns[type] = turn + 1;
            ret[(type, turn)] = (p + 6, length);
            p += 6 + length;
        }
        return ret;
    }

    private static byte[] RecordBytes(byte[] file, string type)
    {
        int p = 24 + BitConverter.ToInt32(file, 4);
        while (p + 24 <= file.Length)
        {
            var sig = Encoding.ASCII.GetString(file, p, 4);
            int size = BitConverter.ToInt32(file, p + 4);
            if (sig == "GRUP") { p += 24; continue; }
            if (sig == type) return file.AsSpan(p, 24 + size).ToArray();
            p += 24 + size;
        }
        throw new InvalidOperationException(type + " not written");
    }

    // perf-world: a record with many overflow-capable subrecords (a navmesh info map's NVMI, each written through its
    // own buffer and moved into the record) has every link placed in its own subrecord, at the bytes it was stored in,
    // for both readers; and none is stored in another form (GetOwnLinkFormsAsStored)
    [Theory, MutagenAutoData]
    public async Task ManyOverflowCapableSubrecords_EachLinkPlaced(IFileSystem fileSystem, DirectoryPath existingTempDir)
    {
        const int count = 300;
        var mod = new StarfieldMod(new ModKey("NaviPlaces", ModType.Master), StarfieldRelease.Starfield);
        var navi = new NavigationMeshInfoMap(mod) { NavMeshVersion = 15 };
        for (int i = 0; i < count; i++)
        {
            var info = new NavigationMapInfo();
            info.NavigationMesh.SetTo(new FormKey(mod.ModKey, 0x900 + (uint)i));
            info.MergedTo.Add(new FormLink<INavigationMeshGetter>(new FormKey(mod.ModKey, 0x2000 + (uint)i)));
            navi.MapInfos.Add(info);
        }
        mod.NavigationMeshInfoMaps.Add(navi);
        var path = new ModPath(mod.ModKey, System.IO.Path.Combine(existingTempDir, mod.ModKey.FileName));
        await mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().WithFileSystem(fileSystem).WriteAsync();
        var stored = RecordBytes(fileSystem.File.ReadAllBytes(path), "NAVI");
        var subrecords = Subrecords(stored);

        var full = StarfieldMod.Create(StarfieldRelease.Starfield).FromPath(path).WithNoLoadOrder().WithFileSystem(fileSystem).Mutable().Construct();
        using var overlay = StarfieldMod.Create(StarfieldRelease.Starfield).FromPath(path).WithNoLoadOrder().WithFileSystem(fileSystem).Construct();
        foreach (var read in new IStarfieldModGetter[] { full, overlay })
        {
            var record = read.NavigationMeshInfoMaps.Single();
            var ids = record.GetStoredFormIds(read);
            ids[0].Location.ShouldBe(StoredFormIdLocation.Header);
            var links = ids.Skip(1).ToList();
            links.Count.ShouldBe(record.EnumerateFormLinks().Count());
            links.ShouldAllBe(x => x.Location == StoredFormIdLocation.Written && x.Subrecord == new RecordType("NVMI"));
            for (int i = 0; i < count; i++)
            {
                var inTurn = links.Where(x => x.SubrecordIndex == i).ToList();
                inTurn.Where(x => !x.FormKey.IsNull).Select(x => x.FormKey).ShouldBe(new[] { new FormKey(mod.ModKey, 0x900 + (uint)i), new FormKey(mod.ModKey, 0x2000 + (uint)i) }, ignoreOrder: true);
                var (start, length) = subrecords[("NVMI", i)];
                foreach (var x in inTurn)
                {
                    (x.Offset + 4).ShouldBeLessThanOrEqualTo(length);
                    BitConverter.ToUInt32(stored, start + x.Offset).ShouldBe(x.Raw);
                }
            }
            record.GetOwnLinkFormsAsStored().ShouldBeNull();
        }
    }

    // A record that holds others (a cell) lists only its own FormIDs, not its children's; a record that holds none (a
    // placed object) still lists its own, now that its write runs to the end (perf-world)
    [Theory, MutagenAutoData]
    public async Task Cell_ChildrenNotListed(IFileSystem fileSystem, DirectoryPath existingTempDir)
    {
        var mod = new StarfieldMod(new ModKey("CellPlaces", ModType.Master), StarfieldRelease.Starfield);
        var keyword = mod.Keywords.AddNew("Base");
        var cell = new Cell(mod) { EditorID = "TestCell" };
        cell.Location.SetTo(new FormKey(mod.ModKey, 0xA00));
        var placed = new PlacedObject(mod) { EditorID = "TestPlaced" };
        placed.Base.SetTo(keyword.FormKey);
        cell.Temporary.Add(placed);
        mod.Cells.Add(new CellBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellBlock, SubBlocks = { new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = { cell } } } });
        var path = new ModPath(mod.ModKey, System.IO.Path.Combine(existingTempDir, mod.ModKey.FileName));
        await mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().WithFileSystem(fileSystem).WriteAsync();

        var full = StarfieldMod.Create(StarfieldRelease.Starfield).FromPath(path).WithNoLoadOrder().WithFileSystem(fileSystem).Mutable().Construct();
        using var overlay = StarfieldMod.Create(StarfieldRelease.Starfield).FromPath(path).WithNoLoadOrder().WithFileSystem(fileSystem).Construct();
        foreach (var read in new IStarfieldModGetter[] { full, overlay })
        {
            var readCell = read.Cells.Records.Single().SubBlocks.Single().Cells.Single();
            var cellIds = readCell.GetStoredFormIds(read);
            cellIds.Select(x => x.FormKey).ShouldContain(new FormKey(mod.ModKey, 0xA00));
            cellIds.Select(x => x.FormKey).ShouldNotContain(keyword.FormKey);
            cellIds.ShouldAllBe(x => x.Location != StoredFormIdLocation.NotFound);
            var placedIds = readCell.Temporary.Single().GetStoredFormIds(read);
            placedIds[0].FormKey.ShouldBe(placed.FormKey);
            placedIds.Single(x => x.FormKey == keyword.FormKey).Location.ShouldBe(StoredFormIdLocation.Written);
            readCell.GetOwnLinkFormsAsStored().ShouldBeNull();
            readCell.Temporary.Single().GetOwnLinkFormsAsStored().ShouldBeNull();
        }
    }
}
