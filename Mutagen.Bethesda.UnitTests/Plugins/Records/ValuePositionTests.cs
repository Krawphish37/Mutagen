using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

// C60: ValuePositionMixIn says where in a record's bytes each value was read, from the full-import reader's own notes.
public class ValuePositionTests
{
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");
    private static readonly ModKey Plugin = ModKey.FromFileName("Positions.esm");

    private static byte[] Sub(string type, byte[] content) =>
        Encoding.ASCII.GetBytes(type).Concat(BitConverter.GetBytes((ushort)content.Length)).Concat(content).ToArray();

    private static byte[] Record(string type, uint flags, uint formId, byte[] content, int? storedLength = null)
    {
        var s = new MemoryStream();
        s.Write(Encoding.ASCII.GetBytes(type));
        s.Write(BitConverter.GetBytes(storedLength ?? content.Length));
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

    private static byte[] Le(params object[] values) => values.SelectMany(v => v switch
    {
        byte b => new[] { b },
        ushort u => BitConverter.GetBytes(u),
        int i => BitConverter.GetBytes(i),
        uint u => BitConverter.GetBytes(u),
        float f => BitConverter.GetBytes(f),
        byte[] a => a,
        _ => throw new ArgumentException(),
    }).ToArray();

    // A CTDA: compare "equal to" a float (1.5), GetWantBlocking (function 0) with parameters 11 and 22, run on the
    // subject, no reference, parameter 3 -1
    private static readonly byte[] Ctda = Le((byte)0x00, new byte[] { 1, 2, 3 }, 1.5f, (ushort)0, (ushort)7, 11, 22, 0, 0u, -1);

    // EDID, FULL, two LNAM (Starfield.esm 000ABC, own 000801), INAM 7 + CITC 1 + one CTDA, ANAM (own 000802)
    private static readonly byte[] FormListContent =
        Sub("EDID", Encoding.ASCII.GetBytes("List\0"))
            .Concat(Sub("FULL", Encoding.ASCII.GetBytes("Name\0")))
            .Concat(Sub("LNAM", Le(0x00000ABCu)))
            .Concat(Sub("LNAM", Le(0x01000801u)))
            .Concat(Sub("INAM", Le(7u)))
            .Concat(Sub("CITC", Le(1u)))
            .Concat(Sub("CTDA", Ctda))
            .Concat(Sub("ANAM", Le(0x01000802u)))
            .ToArray();

    private static byte[] FormListRecord(bool compressed)
    {
        if (!compressed) return Record("FLST", 0, 0x01000803, FormListContent);
        var zipped = new MemoryStream();
        using (var z = new ZLibStream(zipped, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(FormListContent);
        }
        var stored = BitConverter.GetBytes(FormListContent.Length).Concat(zipped.ToArray()).ToArray();
        return Record("FLST", 0x0004_0000, 0x01000803, stored);
    }

    private static (MockFileSystem Fs, string Path, long RecordAt) Make(bool compressed)
    {
        var header = Record("TES4", 0, 0,
            Sub("HEDR", Convert.FromHexString("8FC2753F0300000004080000"))
                .Concat(Sub("MAST", Encoding.ASCII.GetBytes("Starfield.esm\0"))).ToArray());
        var file = header.Concat(Group("FLST", FormListRecord(compressed))).ToArray();
        var fs = new MockFileSystem();
        var path = System.IO.Path.Combine("C:", "Data", Plugin.FileName);
        fs.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        fs.File.WriteAllBytes(path, file);
        return (fs, path, header.Length + 24);
    }

    private static BinaryReadParameters Params(MockFileSystem fs) => new()
    {
        FileSystem = fs,
        MasterFlagsLookup = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
            new KeyedMasterStyle(Plugin, MasterStyle.Full),
        }),
    };

    private static ValuePosition One(RecordValuePositions positions, string path, ValuePositionKind kind)
    {
        var found = positions[path].Where(p => p.Kind == kind).ToList();
        found.Count.ShouldBe(1, $"{path} {kind}: {string.Join(" | ", positions.Positions.Select(p => $"{p.Path} {p.Kind}"))}");
        return found[0];
    }

    // Each subrecord's content start in the record bytes (header 24, subrecord headers 6)
    private const int Edid = 30, Full = 41, Lnam0 = 52, Lnam1 = 62, Inam = 72, Citc = 82, CtdaContent = 92, Anam = 130;

    private static void CheckFormList(RecordValuePositions positions, bool compressed)
    {
        positions.Compressed.ShouldBe(compressed);
        positions.HeaderLength.ShouldBe(24);
        positions.Unplaced.ShouldBeEmpty();
        positions.Subrecords.Select(s => s.Type.Type).ShouldBe(new[] { "EDID", "FULL", "LNAM", "LNAM", "INAM", "CITC", "CTDA", "ANAM" });
        positions.Bytes.Length.ShouldBe(24 + FormListContent.Length);
        positions.Bytes.Span.Slice(24).ToArray().ShouldBe(FormListContent);

        // The record header's fields
        One(positions, "MajorRecordFlagsRaw", ValuePositionKind.Value).ShouldBe(
            new ValuePosition("MajorRecordFlagsRaw", ValuePositionKind.Value, 8, 4, null, -1, -1, 8, 1, 0));
        One(positions, "FormKey", ValuePositionKind.Value).ShouldBe(
            new ValuePosition("FormKey", ValuePositionKind.Value, 12, 4, null, -1, -1, 12, 1, 0));
        One(positions, "FormVersion", ValuePositionKind.Value).Start.ShouldBe(20);
        One(positions, "Version2", ValuePositionKind.Value).Start.ShouldBe(22);

        // Subrecord values: the content, without the subrecord header
        One(positions, "EditorID", ValuePositionKind.Value).ShouldBe(
            new ValuePosition("EditorID", ValuePositionKind.Value, Edid, 5, new RecordType("EDID"), 0, 0, 0, 1, 0));
        One(positions, "Name", ValuePositionKind.Value).ShouldBe(
            new ValuePosition("Name", ValuePositionKind.Value, Full, 5, new RecordType("FULL"), 1, 0, 0, 1, 0));
        One(positions, "AddToList", ValuePositionKind.Value).ShouldBe(
            new ValuePosition("AddToList", ValuePositionKind.Value, Anam, 4, new RecordType("ANAM"), 7, 0, 0, 1, 0));

        // A list and its items, by index and turn
        var items = One(positions, "Items", ValuePositionKind.List);
        items.Start.ShouldBe(Lnam0);
        items.SubrecordCount.ShouldBe(2);
        One(positions, "Items[0]", ValuePositionKind.Value).ShouldBe(
            new ValuePosition("Items[0]", ValuePositionKind.Value, Lnam0, 4, new RecordType("LNAM"), 2, 0, 0, 1, 0));
        One(positions, "Items[1]", ValuePositionKind.Value).ShouldBe(
            new ValuePosition("Items[1]", ValuePositionKind.Value, Lnam1, 4, new RecordType("LNAM"), 3, 1, 0, 1, 0));

        // Objects in a list, a counted list in them, the generated fields of a condition's data
        One(positions, "ConditionalEntries", ValuePositionKind.List).SubrecordCount.ShouldBe(3);
        One(positions, "ConditionalEntries[0]", ValuePositionKind.Object).Start.ShouldBe(Inam);
        One(positions, "ConditionalEntries[0].Index", ValuePositionKind.Value).Start.ShouldBe(Inam);
        One(positions, "ConditionalEntries[0].Conditions", ValuePositionKind.Count).ShouldBe(
            new ValuePosition("ConditionalEntries[0].Conditions", ValuePositionKind.Count, Citc, 4, new RecordType("CITC"), 5, 0, 0, 1, 0));
        One(positions, "ConditionalEntries[0].Conditions", ValuePositionKind.List).Start.ShouldBe(Citc);
        One(positions, "ConditionalEntries[0].Conditions[0]", ValuePositionKind.Object).Start.ShouldBe(CtdaContent);
        One(positions, "ConditionalEntries[0].Conditions[0].Unknown1", ValuePositionKind.Value).ShouldBe(
            new ValuePosition("ConditionalEntries[0].Conditions[0].Unknown1", ValuePositionKind.Value, CtdaContent + 1, 3,
                new RecordType("CTDA"), 6, 0, 1, 1, 0));
        One(positions, "ConditionalEntries[0].Conditions[0].FunctionParse", ValuePositionKind.HandRead).Offset.ShouldBe(4);

        // The bytes of a position are the value's
        positions.Slice(One(positions, "EditorID", ValuePositionKind.Value)).ToArray().ShouldBe(Encoding.ASCII.GetBytes("List\0"));
        positions.Slice(One(positions, "Items[1]", ValuePositionKind.Value)).ToArray().ShouldBe(Le(0x01000801u));
        positions.Record.ShouldBeOfType<FormList>().Items[1].FormKey.ShouldBe(new FormKey(Plugin, 0x801));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Overlay(bool compressed)
    {
        var (fs, path, _) = Make(compressed);
        using var mod = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, Params(fs));
        CheckFormList(mod.FormLists.Single().GetValuePositions(), compressed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MajorRecordReader_SamePositionsAsOverlay(bool compressed)
    {
        var (fs, path, at) = Make(compressed);
        using var reader = MajorRecordReader.FromPath(new ModPath(Plugin, path), GameRelease.Starfield, Params(fs));
        var read = reader.ReadValuePositionsAt(at);
        CheckFormList(read, compressed);
        using var mod = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, Params(fs));
        read.Positions.ShouldBe(mod.FormLists.Single().GetValuePositions().Positions);
    }

    [Fact]
    public void FullImportRecord_Throws()
    {
        var (fs, path, _) = Make(false);
        var mod = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, Params(fs));
        Should.Throw<ArgumentException>(() => mod.FormLists.Single().GetValuePositions());
    }

    [Fact]
    public void NothingIsNotedWhenNotAsked()
    {
        var (fs, path, _) = Make(false);
        var mod = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, Params(fs));
        mod.FormLists.Single().Items.Count.ShouldBe(2);
        ValuePositionRecorder.On.ShouldBeFalse();
        ValuePositionRecorder.Mark().ShouldBe(0);
    }
}
