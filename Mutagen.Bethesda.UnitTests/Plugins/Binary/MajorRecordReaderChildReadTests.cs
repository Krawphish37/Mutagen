using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Analysis;
using Mutagen.Bethesda.Plugins.Binary;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

/// <summary>
/// C58/W9: a holder read with its children group laid out and each child read from its header alone
/// (ChildRecordRead.Headers), as SFSearch got by cutting every child down to a 24-byte stub
/// </summary>
public class MajorRecordReaderChildReadTests
{
    private readonly ITestOutputHelper _output;

    public MajorRecordReaderChildReadTests(ITestOutputHelper output) => _output = output;

    [Theory, MutagenAutoData]
    public async Task Headers_LaysOutTheChildrenWithoutReadingThem(IFileSystem fileSystem, DirectoryPath existingTempDir)
    {
        var mod = new StarfieldMod(new ModKey("ChildRead", ModType.Master), StarfieldRelease.Starfield);
        var keyword = mod.Keywords.AddNew("TestKeyword");
        var cell = new Cell(mod) { EditorID = "TestCell" };
        var persistent = new PlacedObject(mod) { EditorID = "PersistentRef" };
        persistent.Base.SetTo(keyword.FormKey);
        var temporary = new PlacedObject(mod) { EditorID = "TemporaryRef" };
        temporary.Base.SetTo(keyword.FormKey);
        cell.Persistent.Add(persistent);
        cell.Temporary.Add(temporary);
        mod.Cells.Add(new CellBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellBlock, SubBlocks = { new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = { cell } } } });
        var world = mod.Worldspaces.AddNew("TestWorld");
        var worldCell = new Cell(mod) { EditorID = "WorldCell", Grid = new CellGrid { Point = new P2Int(1, 2) } };
        var worldRef = new PlacedObject(mod) { EditorID = "WorldRef" };
        worldRef.Base.SetTo(keyword.FormKey);
        worldCell.Temporary.Add(worldRef);
        world.SubCells.Add(new WorldspaceBlock
        {
            BlockNumberX = 0, BlockNumberY = 0, GroupType = GroupTypeEnum.ExteriorCellBlock,
            Items = { new WorldspaceSubBlock { BlockNumberX = 0, BlockNumberY = 0, GroupType = GroupTypeEnum.ExteriorCellSubBlock, Items = { worldCell } } }
        });
        var path = new ModPath(mod.ModKey, Path.Combine(existingTempDir, mod.ModKey.FileName));
        await mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().WithFileSystem(fileSystem).WriteAsync();

        var locations = RecordLocator.GetLocations(path, GameRelease.Starfield, loadOrder: null, fileSystem: fileSystem);
        using var reader = MajorRecordReader.FromPath(path, GameRelease.Starfield, new BinaryReadParameters { FileSystem = fileSystem });

        // A cell: both groups laid out, each reference a header (its FormKey, no fields)
        var cellPosition = locations[cell.FormKey].Min;
        var full = reader.ReadAt(cellPosition, ChildRecordRead.Full).ShouldBeOfType<Cell>();
        var headers = reader.ReadAt(cellPosition, ChildRecordRead.Headers).ShouldBeOfType<Cell>();
        headers.EditorID.ShouldBe("TestCell");
        headers.Persistent.Select(p => p.FormKey).ShouldBe(full.Persistent.Select(p => p.FormKey));
        headers.Temporary.Select(p => p.FormKey).ShouldBe(full.Temporary.Select(p => p.FormKey));
        headers.Persistent.Single().EditorID.ShouldBeNull();
        headers.Persistent.Single().ShouldBeOfType<PlacedObject>().Base.IsNull.ShouldBeTrue();
        full.Persistent.Single().EditorID.ShouldBe("PersistentRef");
        headers.Timestamp.ShouldBe(full.Timestamp);
        headers.PersistentTimestamp.ShouldBe(full.PersistentTimestamp);
        reader.ReadAt(cellPosition, ChildRecordRead.None).ShouldBeOfType<Cell>().Persistent.ShouldBeEmpty();

        // A worldspace: its blocks and cells laid out, each cell a header, and the cells' references left out
        var worldPosition = locations[world.FormKey].Min;
        var worldFull = reader.ReadAt(worldPosition, ChildRecordRead.Full).ShouldBeOfType<Worldspace>();
        var worldHeaders = reader.ReadAt(worldPosition, ChildRecordRead.Headers).ShouldBeOfType<Worldspace>();
        var cellsFull = worldFull.SubCells.SelectMany(b => b.Items).SelectMany(s => s.Items).ToArray();
        var cellsHeaders = worldHeaders.SubCells.SelectMany(b => b.Items).SelectMany(s => s.Items).ToArray();
        cellsHeaders.Select(c => c.FormKey).ShouldBe(cellsFull.Select(c => c.FormKey));
        cellsFull.Single().Temporary.Count.ShouldBe(1);
        cellsHeaders.Single().Temporary.ShouldBeEmpty();
        cellsHeaders.Single().EditorID.ShouldBeNull();
        worldHeaders.SubCells.Select(b => (b.BlockNumberX, b.BlockNumberY)).ShouldBe(worldFull.SubCells.Select(b => (b.BlockNumberX, b.BlockNumberY)));

        // The bytes form leaves the grandchildren out and keeps every record header as stored
        var bytes = reader.ReadBytesAt(worldPosition, ChildRecordRead.Headers);
        bytes.Length.ShouldBeLessThan(reader.ReadBytesAt(worldPosition, ChildRecordRead.Full).Length);
        reader.Read(bytes, worldPosition, ChildRecordRead.Headers).ShouldBeOfType<Worldspace>()
            .SubCells.Single().Items.Single().Items.Single().FormKey.ShouldBe(worldCell.FormKey);
    }

    /// <summary>
    /// Starfield.esm: for one holder of each kind (an interior cell with persistent and temporary references, a
    /// worldspace, a quest with topics, a topic with responses), the Headers read lays out the same children, in the same
    /// lists and order, as the full read, with the same group values
    /// </summary>
    [Fact]
    [Trait("Category", "RealData")]
    public void RealData_StarfieldEsm_HeadersLayOutWhatTheFullReadDoes()
    {
        var data = Environment.GetEnvironmentVariable("MUTAGEN_STARFIELD_DATA");
        if (string.IsNullOrEmpty(data)) return; // needs the game's Data folder
        var path = new ModPath(ModKey.FromFileName("Starfield.esm"), Path.Combine(data, "Starfield.esm"));
        var locations = RecordLocator.GetLocations(path, GameRelease.Starfield, loadOrder: null);
        using var reader = MajorRecordReader.FromPath(path, GameRelease.Starfield);
        var constants = Mutagen.Bethesda.Plugins.Meta.GameConstants.Starfield;

        IEnumerable<long> PositionsOf(string signature) =>
            locations.ListedRecords.Where(m => m.Value.Record.Type == signature).Select(m => m.Key);

        // The size of the children group that follows a record (0 when none does), from the group's header
        using var file = File.OpenRead(path.Path);
        long ChildrenSize(long position)
        {
            var next = position + reader.ReadBytesAt(position, ChildRecordRead.None).Length;
            var head = new byte[constants.GroupConstants.HeaderLength];
            if (next + head.Length > file.Length) return 0;
            file.Position = next;
            file.ReadExactly(head);
            var group = new Mutagen.Bethesda.Plugins.Binary.Headers.GroupHeader(constants, head);
            return group.IsGroup && !group.IsTopLevel ? group.TotalLength : 0;
        }

        // A holder with children: its bytes go past the record (the children group follows)
        long WithChildren(string signature, Func<long, bool>? also = null) => PositionsOf(signature)
            .First(p => ChildrenSize(p) > 0 && (also?.Invoke(p) ?? true));

        var cellPosition = WithChildren("CELL", p => reader.ReadAt(p, ChildRecordRead.Headers) is ICellGetter c && c.Persistent.Count > 0 && c.Temporary.Count > 0);
        var cellFull = (ICellGetter)reader.ReadAt(cellPosition, ChildRecordRead.Full);
        var cellHeaders = (ICellGetter)reader.ReadAt(cellPosition, ChildRecordRead.Headers);
        cellHeaders.Persistent.Select(x => x.FormKey).ShouldBe(cellFull.Persistent.Select(x => x.FormKey));
        cellHeaders.Temporary.Select(x => x.FormKey).ShouldBe(cellFull.Temporary.Select(x => x.FormKey));
        cellHeaders.NavigationMeshes.Select(x => x.FormKey).ShouldBe(cellFull.NavigationMeshes.Select(x => x.FormKey));
        (cellHeaders.Timestamp, cellHeaders.PersistentTimestamp, cellHeaders.TemporaryTimestamp)
            .ShouldBe((cellFull.Timestamp, cellFull.PersistentTimestamp, cellFull.TemporaryTimestamp));
        cellHeaders.Temporary.Select(x => x.MajorRecordFlagsRaw).ShouldBe(cellFull.Temporary.Select(x => x.MajorRecordFlagsRaw));
        cellHeaders.Temporary.All(x => x.EditorID == null).ShouldBeTrue();
        cellHeaders.Equals(cellFull, new Cell.TranslationMask(defaultOn: true) { Persistent = false, Temporary = false, NavigationMeshes = false })
            .ShouldBeTrue();

        // The worldspace with the smallest children group
        var worldPosition = PositionsOf("WRLD").Where(p => ChildrenSize(p) > 0).MinBy(ChildrenSize);
        var worldFull = (IWorldspaceGetter)reader.ReadAt(worldPosition, ChildRecordRead.Full);
        var worldHeaders = (IWorldspaceGetter)reader.ReadAt(worldPosition, ChildRecordRead.Headers);
        (worldHeaders.TopCell?.FormKey).ShouldBe(worldFull.TopCell?.FormKey);
        worldHeaders.SubCells.SelectMany(b => b.Items).SelectMany(s => s.Items).Select(c => c.FormKey)
            .ShouldBe(worldFull.SubCells.SelectMany(b => b.Items).SelectMany(s => s.Items).Select(c => c.FormKey));
        worldHeaders.SubCells.SelectMany(b => b.Items).SelectMany(s => s.Items).All(c => c.Temporary.Count == 0 && c.Persistent.Count == 0).ShouldBeTrue();
        worldHeaders.Equals(worldFull, new Worldspace.TranslationMask(defaultOn: true) { TopCell = false, SubCells = false }).ShouldBeTrue();

        var questPosition = WithChildren("QUST", p => reader.ReadAt(p, ChildRecordRead.Headers) is IQuestGetter q && q.DialogTopics.Count > 0 && q.Scenes.Count > 0);
        var questFull = (IQuestGetter)reader.ReadAt(questPosition, ChildRecordRead.Full);
        var questHeaders = (IQuestGetter)reader.ReadAt(questPosition, ChildRecordRead.Headers);
        // A quest's children group holds its topics, scenes and branches
        questHeaders.DialogTopics.Select(x => x.FormKey).ShouldBe(questFull.DialogTopics.Select(x => x.FormKey));
        questHeaders.Scenes.Select(x => x.FormKey).ShouldBe(questFull.Scenes.Select(x => x.FormKey));
        questHeaders.DialogBranches.Select(x => x.FormKey).ShouldBe(questFull.DialogBranches.Select(x => x.FormKey));
        questHeaders.DialogTopics.All(t => t.Responses.Count == 0).ShouldBeTrue();
        questHeaders.Equals(questFull, new Quest.TranslationMask(defaultOn: true) { DialogTopics = false, Scenes = false, DialogBranches = false })
            .ShouldBeTrue();

        var topicPosition = WithChildren("DIAL");
        var topicFull = (IDialogTopicGetter)reader.ReadAt(topicPosition, ChildRecordRead.Full);
        var topicHeaders = (IDialogTopicGetter)reader.ReadAt(topicPosition, ChildRecordRead.Headers);
        topicHeaders.Responses.Select(x => x.FormKey).ShouldBe(topicFull.Responses.Select(x => x.FormKey));
        topicHeaders.Equals(topicFull, new DialogTopic.TranslationMask(defaultOn: true) { Responses = false }).ShouldBeTrue();

        _output.WriteLine($"cell {cellFull.FormKey}: {cellFull.Persistent.Count} persistent, {cellFull.Temporary.Count} temporary; " +
                          $"world {worldFull.FormKey}; quest {questFull.FormKey}: {questFull.DialogTopics.Count} topics, {questFull.Scenes.Count} scenes, {questFull.DialogBranches.Count} branches; topic {topicFull.FormKey}: {topicFull.Responses.Count} responses");
    }
}
