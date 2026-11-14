using System.Buffers.Binary;
using System.Xml.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Noggog;
using Shouldly;
using Xunit;
using TempFile = Noggog.IO.TempFile;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C186 group 4: the group structure Mutagen reads by hand, in attributes the generator ignores:
//   layoutGroup="T1/T2"           the field lives in the group of type T2 inside the group of type T1 that follows the
//                                 record (both labelled with its FormID); on a record or list field, that group's records
//   layoutGroup + layoutOffset    a value in that group's 24-byte header, at that offset
//   layoutSubrecord="XTV2" layoutOffset="after:Traversals" layoutEncoding="trailingZeroCount"
//                                 the count of zero bytes ending XTV2 after the last traversal
// Each test writes a mod, walks its groups, checks the values and records where the attributes say, and reads both ways.
public class HandReadGroupLayoutTests
{
    private static readonly XNamespace Loqui = "http://tempuri.org/LoquiSource.xsd";
    private static readonly ModKey TestModKey = ModKey.FromNameAndExtension("HandReadGroupLayout.esp");

    private static string RecordsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Mutagen.Bethesda.Starfield", "Records");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Mutagen.Bethesda.Starfield/Records not found above the test output");
    }

    private static readonly Lazy<Dictionary<string, XElement>> AnnotatedFields = new(() =>
    {
        var ret = new Dictionary<string, XElement>();
        foreach (var file in Directory.EnumerateFiles(RecordsDir(), "*.xml", SearchOption.AllDirectories))
        {
            foreach (var obj in XDocument.Load(file).Root!.Elements(Loqui + "Object"))
            {
                foreach (var field in obj.Descendants())
                {
                    if (field.Attribute("name") is not { } fieldName) continue;
                    if (field.Attribute("layoutGroup") == null
                        && (string?)field.Attribute("layoutEncoding") != "trailingZeroCount") continue;
                    ret.Add($"{obj.Attribute("name")!.Value}.{fieldName.Value}", field);
                }
            }
        }
        return ret;
    });

    private static string? Attr(string key, string attr) => (string?)AnnotatedFields.Value[key].Attribute(attr);

    [Fact]
    public void EveryAnnotatedFieldHasACheck()
    {
        string[] checkedFields =
        [
            "Cell.NumTraversalFluffBytes", "Cell.NavigationMeshes", "Cell.Timestamp", "Cell.UnknownGroupData",
            "Cell.PersistentTimestamp", "Cell.PersistentUnknownGroupData", "Cell.Persistent",
            "Cell.TemporaryTimestamp", "Cell.TemporaryUnknownGroupData", "Cell.Temporary",
            "PlacedObject.NumTraversalFluffBytes",
            "Worldspace.TopCell", "Worldspace.SubCellsTimestamp", "Worldspace.SubCellsUnknown", "Worldspace.SubCells",
            "Quest.Timestamp", "Quest.Unknown", "Quest.DialogBranches", "Quest.DialogTopics", "Quest.Scenes",
            "DialogTopic.Timestamp", "DialogTopic.Unknown", "DialogTopic.Responses",
        ];
        AnnotatedFields.Value.Keys.Order().ShouldBe(checkedFields.Order());
    }

    // ---- the file's groups ----

    private sealed record Group(int Type, uint Label, int Stamp, int Unknown, List<Group> Groups, List<uint> Records);

    private static List<Group> Walk(byte[] file, int pos, int end)
    {
        var ret = new List<Group>();
        while (pos < end)
        {
            var sig = System.Text.Encoding.ASCII.GetString(file, pos, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(pos + 4));
            if (sig == "GRUP")
            {
                var inner = Walk(file, pos + 24, pos + size);
                ret.Add(new Group(
                    BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(pos + 12)),
                    BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(pos + 8)),
                    BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(pos + 16)),
                    BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(pos + 20)),
                    inner.SelectMany(g => g.Type == -1 ? g.Groups : [g]).ToList(),
                    inner.Where(g => g.Type == -1).SelectMany(g => g.Records).ToList()));
                pos += size;
            }
            else
            {
                ret.Add(new Group(-1, 0, 0, 0, [], [BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(pos + 12))]));
                pos += 24 + size;
            }
        }
        return ret;
    }

    private static IEnumerable<Group> All(IEnumerable<Group> groups) =>
        groups.SelectMany(g => new[] { g }.Concat(All(g.Groups)));

    // The group a layoutGroup path names, for the record with this FormID
    private static Group Find(byte[] file, string path, FormKey owner)
    {
        var groups = Walk(file, 0, file.Length).Where(g => g.Type != -1).ToList();
        Group? current = null;
        foreach (var typeName in path.Split('/'))
        {
            var type = (int)Enum.Parse<GroupTypeEnum>(typeName);
            var pool = current == null ? All(groups) : current.Groups;
            current = typeName == "ExteriorCellBlock"
                ? pool.Single(g => g.Type == type)
                : pool.Single(g => g.Type == type && g.Label == owner.ID);
        }
        return current!;
    }

    private static int HeaderValue(Group group, string offset) => offset switch
    {
        "16" => group.Stamp,
        "20" => group.Unknown,
        _ => throw new ArgumentException(offset),
    };

    private static void CheckHeaderValue(byte[] file, FormKey owner, string key, int expected)
    {
        var group = Find(file, Attr(key, "layoutGroup")!, owner);
        HeaderValue(group, Attr(key, "layoutOffset")!).ShouldBe(expected, key);
    }

    private sealed class Written : IDisposable
    {
        private readonly TempFile _tmp;
        public byte[] Bytes { get; }
        public StarfieldMod Full { get; }
        public IStarfieldModDisposableGetter Overlay { get; }

        public Written(StarfieldMod mod)
        {
            _tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
            var path = new ModPath(mod.ModKey, _tmp.File.Path);
            mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();
            Bytes = File.ReadAllBytes(_tmp.File.Path);
            Full = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield);
            Overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield);
        }

        public IEnumerable<T> Both<T>(FormKey formKey) where T : IMajorRecordGetter =>
            new IStarfieldModGetter[] { Full, Overlay }
                .Select(m => (T)m.EnumerateMajorRecords().Single(r => r.FormKey == formKey));

        public void Dispose()
        {
            Overlay.Dispose();
            _tmp.Dispose();
        }
    }

    private static StarfieldMod NewMod() => new(TestModKey, StarfieldRelease.Starfield);

    private static void AddInteriorCell(StarfieldMod mod, Cell cell) =>
        mod.Cells.Add(new CellBlock
        {
            BlockNumber = 0,
            GroupType = GroupTypeEnum.InteriorCellBlock,
            SubBlocks = { new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = { cell } } },
        });

    private static TraversalReference Traversal() => new()
    {
        Unknown1 = 1,
        From = new P3Float(1, 2, 3),
        To = new P3Float(4, 5, 6),
    };

    // ---- the checks ----

    [Fact]
    public void CellChildrenGroupsHoldTheTimestampsAndTheChildren()
    {
        var mod = NewMod();
        var persistent = new PlacedObject(mod) { EditorID = "Persistent" };
        var temporary = new PlacedObject(mod) { EditorID = "Temporary" };
        var navmesh = new NavigationMesh(mod) { EditorID = "Navmesh" };
        var cell = new Cell(mod)
        {
            EditorID = "TestCell",
            Timestamp = 0x11, UnknownGroupData = 0x12,
            PersistentTimestamp = 0x21, PersistentUnknownGroupData = 0x22,
            TemporaryTimestamp = 0x31, TemporaryUnknownGroupData = 0x32,
        };
        cell.Persistent.Add(persistent);
        cell.Temporary.Add(temporary);
        cell.NavigationMeshes.Add(navmesh);
        AddInteriorCell(mod, cell);
        using var written = new Written(mod);

        var values = new Dictionary<string, int>
        {
            ["Cell.Timestamp"] = 0x11, ["Cell.UnknownGroupData"] = 0x12,
            ["Cell.PersistentTimestamp"] = 0x21, ["Cell.PersistentUnknownGroupData"] = 0x22,
            ["Cell.TemporaryTimestamp"] = 0x31, ["Cell.TemporaryUnknownGroupData"] = 0x32,
        };
        foreach (var (key, value) in values)
        {
            CheckHeaderValue(written.Bytes, cell.FormKey, key, value);
        }
        Find(written.Bytes, Attr("Cell.Persistent", "layoutGroup")!, cell.FormKey).Records.ShouldBe([persistent.FormKey.ID]);
        Find(written.Bytes, Attr("Cell.Temporary", "layoutGroup")!, cell.FormKey).Records.ShouldContain(temporary.FormKey.ID);
        Find(written.Bytes, Attr("Cell.NavigationMeshes", "layoutGroup")!, cell.FormKey).Records.ShouldContain(navmesh.FormKey.ID);

        foreach (var read in written.Both<ICellGetter>(cell.FormKey))
        {
            foreach (var (key, value) in values)
            {
                typeof(ICellGetter).GetProperty(key.Split('.')[1])!.GetValue(read).ShouldBe(value, key);
            }
            read.Persistent.Select(p => p.FormKey).ShouldBe([persistent.FormKey]);
            read.Temporary.Select(p => p.FormKey).ShouldBe([temporary.FormKey]);
            read.NavigationMeshes.Select(p => p.FormKey).ShouldBe([navmesh.FormKey]);
        }
    }

    [Fact]
    public void WorldChildrenGroupHoldsTheTopCellTheTimestampAndTheBlocks()
    {
        var mod = NewMod();
        var topCell = new Cell(mod) { EditorID = "TopCell" };
        var exterior = new Cell(mod) { EditorID = "Exterior", Grid = new CellGrid { Point = new P2Int(0, 0) } };
        var world = new Worldspace(mod)
        {
            EditorID = "TestWorld",
            TopCell = topCell,
            SubCellsTimestamp = 0x41,
            SubCellsUnknown = 0x42,
        };
        world.SubCells.Add(new WorldspaceBlock
        {
            GroupType = GroupTypeEnum.ExteriorCellBlock,
            Items =
            {
                new WorldspaceSubBlock { GroupType = GroupTypeEnum.ExteriorCellSubBlock, Items = { exterior } },
            },
        });
        mod.Worldspaces.Add(world);
        using var written = new Written(mod);

        CheckHeaderValue(written.Bytes, world.FormKey, "Worldspace.SubCellsTimestamp", 0x41);
        CheckHeaderValue(written.Bytes, world.FormKey, "Worldspace.SubCellsUnknown", 0x42);
        var children = Find(written.Bytes, Attr("Worldspace.TopCell", "layoutGroup")!, world.FormKey);
        children.Records.ShouldBe([topCell.FormKey.ID]);
        var block = Find(written.Bytes, Attr("Worldspace.SubCells", "layoutGroup")!, world.FormKey);
        // the path names the block group inside the world's children group
        All([block]).SelectMany(g => g.Records).ShouldContain(exterior.FormKey.ID);

        foreach (var read in written.Both<IWorldspaceGetter>(world.FormKey))
        {
            read.SubCellsTimestamp.ShouldBe(0x41);
            read.SubCellsUnknown.ShouldBe(0x42);
            read.TopCell!.FormKey.ShouldBe(topCell.FormKey);
            read.SubCells.SelectMany(b => b.Items).SelectMany(s => s.Items).Select(c => c.FormKey)
                .ShouldBe([exterior.FormKey]);
        }
    }

    [Fact]
    public void QuestAndTopicChildrenGroupsHoldTheirRecords()
    {
        var mod = NewMod();
        var response = new DialogResponses(mod) { EditorID = "Response" };
        var topic = new DialogTopic(mod) { EditorID = "Topic", Timestamp = 0x61, Unknown = 0x62 };
        topic.Responses.Add(response);
        var branch = new DialogBranch(mod) { EditorID = "Branch" };
        var scene = new Scene(mod) { EditorID = "Scene" };
        var quest = new Quest(mod) { EditorID = "TestQuest", Timestamp = 0x51, Unknown = 0x52 };
        quest.DialogTopics.Add(topic);
        quest.DialogBranches.Add(branch);
        quest.Scenes.Add(scene);
        mod.Quests.Add(quest);
        using var written = new Written(mod);

        CheckHeaderValue(written.Bytes, quest.FormKey, "Quest.Timestamp", 0x51);
        CheckHeaderValue(written.Bytes, quest.FormKey, "Quest.Unknown", 0x52);
        CheckHeaderValue(written.Bytes, topic.FormKey, "DialogTopic.Timestamp", 0x61);
        CheckHeaderValue(written.Bytes, topic.FormKey, "DialogTopic.Unknown", 0x62);
        foreach (var (key, child) in new (string, IMajorRecordGetter)[]
                 {
                     ("Quest.DialogBranches", branch), ("Quest.DialogTopics", topic), ("Quest.Scenes", scene),
                 })
        {
            Find(written.Bytes, Attr(key, "layoutGroup")!, quest.FormKey).Records.ShouldContain(child.FormKey.ID, key);
        }
        var topicChildren = Find(written.Bytes, Attr("DialogTopic.Responses", "layoutGroup")!, topic.FormKey);
        topicChildren.Records.ShouldBe([response.FormKey.ID]);
        Find(written.Bytes, "QuestChildren", quest.FormKey).Groups
            .Any(g => g.Type == (int)GroupTypeEnum.TopicChildren && g.Label == topic.FormKey.ID).ShouldBeTrue();

        foreach (var read in written.Both<IQuestGetter>(quest.FormKey))
        {
            read.Timestamp.ShouldBe(0x51);
            read.Unknown.ShouldBe(0x52);
            read.DialogBranches.Select(r => r.FormKey).ShouldBe([branch.FormKey]);
            read.Scenes.Select(r => r.FormKey).ShouldBe([scene.FormKey]);
            var readTopic = read.DialogTopics.Single();
            readTopic.FormKey.ShouldBe(topic.FormKey);
            readTopic.Timestamp.ShouldBe(0x61);
            readTopic.Unknown.ShouldBe(0x62);
            readTopic.Responses.Select(r => r.FormKey).ShouldBe([response.FormKey]);
        }
    }

    [Fact]
    public void TraversalFluffBytesAreTheZerosEndingXtv2()
    {
        foreach (var key in new[] { "Cell.NumTraversalFluffBytes", "PlacedObject.NumTraversalFluffBytes" })
        {
            Attr(key, "layoutSubrecord").ShouldBe("XTV2");
            Attr(key, "layoutOffset").ShouldBe("after:Traversals");
        }
        var mod = NewMod();
        // Whole zeroed traversals: a partial one (fewer than 52 bytes) reads as an extra traversal in the full reader
        // (it reads past XTV2's end), while the overlay counts it as fluff; reported with C186, not covered here.
        var placed = new PlacedObject(mod) { EditorID = "Placed", NumTraversalFluffBytes = 104 };
        placed.Traversals = new ExtendedList<TraversalReference> { Traversal() };
        var cell = new Cell(mod) { EditorID = "TestCell", NumTraversalFluffBytes = 52 };
        cell.Traversals = new ExtendedList<TraversalReference> { Traversal(), Traversal() };
        cell.Temporary.Add(placed);
        AddInteriorCell(mod, cell);
        using var written = new Written(mod);

        foreach (var (record, count, fluff) in new (IMajorRecordGetter, int, int)[] { (cell, 2, 52), (placed, 1, 104) })
        {
            var xtv2 = Xtv2(written.Bytes, record.FormKey);
            // a traversal without a FormID is 52 bytes
            xtv2.Length.ShouldBe(count * 52 + fluff);
            xtv2.AsSpan(count * 52).ToArray().ShouldAllBe(b => b == 0);
        }
        var readers = new[] { "full", "overlay" };
        foreach (var (read, reader) in written.Both<ICellGetter>(cell.FormKey).Zip(readers))
        {
            $"{reader} {read.Traversals!.Count} {read.NumTraversalFluffBytes}".ShouldBe($"{reader} 2 52");
        }
        foreach (var (read, reader) in written.Both<IPlacedObjectGetter>(placed.FormKey).Zip(readers))
        {
            $"{reader} {read.Traversals!.Count} {read.NumTraversalFluffBytes}".ShouldBe($"{reader} 1 104");
        }
    }

    private static byte[] Xtv2(byte[] file, FormKey formKey)
    {
        var pos = 0;
        while (pos < file.Length)
        {
            var sig = System.Text.Encoding.ASCII.GetString(file, pos, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(pos + 4));
            if (sig == "GRUP")
            {
                pos += 24;
                continue;
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(pos + 12)) == formKey.ID)
            {
                var sub = pos + 24;
                var end = sub + size;
                while (sub < end)
                {
                    var subSig = System.Text.Encoding.ASCII.GetString(file, sub, 4);
                    var len = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(sub + 4));
                    if (subSig == "XTV2") return file.AsSpan(sub + 6, len).ToArray();
                    sub += 6 + len;
                }
                throw new KeyNotFoundException("XTV2");
            }
            pos += 24 + size;
        }
        throw new KeyNotFoundException(formKey.ToString());
    }
}
