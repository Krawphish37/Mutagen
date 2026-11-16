using System.Buffers.Binary;
using System.Collections;
using System.IO.Abstractions.TestingHelpers;
using System.Reflection;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Noggog;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

// C82: the plugin header (TES4) has value positions too, through its overlay and through MajorRecordReader
public class ValuePositionHeaderTests
{
    private readonly ITestOutputHelper _output;

    public ValuePositionHeaderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");
    private static readonly ModKey OtherEsm = ModKey.FromFileName("Other.esm");
    private static readonly ModKey Plugin = ModKey.FromFileName("Header.esm");

    private static byte[] Sub(string type, byte[] content) =>
        Encoding.ASCII.GetBytes(type).Concat(BitConverter.GetBytes((ushort)content.Length)).Concat(content).ToArray();

    private static byte[] Z(string s) => Encoding.ASCII.GetBytes(s + "\0");

    // TES4: flags 0x1 (master), FormID 0, version 0x11223344, form version 0x22F (559), Version2 0x0102
    private static readonly byte[] HeaderContent =
        Sub("HEDR", BitConverter.GetBytes(0.96f).Concat(BitConverter.GetBytes(3u)).Concat(BitConverter.GetBytes(0x804u)).ToArray())
            .Concat(Sub("CNAM", Z("Author")))
            .Concat(Sub("SNAM", Z("About")))
            .Concat(Sub("MAST", Z("Starfield.esm")))
            .Concat(Sub("DATA", BitConverter.GetBytes(0UL)))
            .Concat(Sub("MAST", Z("Other.esm")))
            .Concat(Sub("DATA", BitConverter.GetBytes(5UL)))
            .Concat(Sub("ONAM", BitConverter.GetBytes(0x00000ABCu).Concat(BitConverter.GetBytes(0x01000DEFu)).ToArray()))
            .Concat(Sub("INTV", BitConverter.GetBytes(7)))
            .Concat(Sub("INCC", BitConverter.GetBytes(2)))
            .ToArray();

    private static byte[] Header()
    {
        var s = new MemoryStream();
        s.Write("TES4"u8);
        s.Write(BitConverter.GetBytes(HeaderContent.Length));
        s.Write(BitConverter.GetBytes(1u));
        s.Write(BitConverter.GetBytes(0u));
        s.Write(BitConverter.GetBytes(0x11223344));
        s.Write(BitConverter.GetBytes((ushort)0x22F));
        s.Write(BitConverter.GetBytes((ushort)0x0102));
        s.Write(HeaderContent);
        return s.ToArray();
    }

    private static (MockFileSystem Fs, string Path) Make()
    {
        var fs = new MockFileSystem();
        var path = System.IO.Path.Combine("C:", "Data", Plugin.FileName);
        fs.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        fs.File.WriteAllBytes(path, Header());
        return (fs, path);
    }

    private static BinaryReadParameters Params(MockFileSystem fs) => new()
    {
        FileSystem = fs,
        MasterFlagsLookup = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
            new KeyedMasterStyle(OtherEsm, MasterStyle.Full),
            new KeyedMasterStyle(Plugin, MasterStyle.Full),
        }),
    };

    private static ValuePosition One(ValuePositions positions, string path, ValuePositionKind kind = ValuePositionKind.Value)
    {
        var found = positions[path].Where(p => p.Kind == kind).ToList();
        found.Count.ShouldBe(1, $"{path} {kind}: {string.Join(" | ", positions.Positions.Select(p => $"{p.Path} {p.Kind}"))}");
        return found[0];
    }

    // Content starts (header 24, subrecord headers 6)
    private const int Hedr = 30, Cnam = 48, Snam = 61, Mast0 = 73, Data0 = 93, Mast1 = 107, Data1 = 123, Onam = 137, Intv = 151, Incc = 161;

    private static void Check(ModHeaderValuePositions positions)
    {
        positions.HeaderLength.ShouldBe(24);
        positions.Unplaced.ShouldBeEmpty();
        positions.Bytes.ToArray().ShouldBe(Header());
        positions.Subrecords.Select(s => s.Type.Type).ShouldBe(new[] { "HEDR", "CNAM", "SNAM", "MAST", "DATA", "MAST", "DATA", "ONAM", "INTV", "INCC" });

        // The record header's fields
        One(positions, "Flags").ShouldBe(new ValuePosition("Flags", ValuePositionKind.Value, 8, 4, null, -1, -1, 8, 1, 0));
        One(positions, "FormID").ShouldBe(new ValuePosition("FormID", ValuePositionKind.Value, 12, 4, null, -1, -1, 12, 1, 0));
        One(positions, "Version").ShouldBe(new ValuePosition("Version", ValuePositionKind.Value, 16, 4, null, -1, -1, 16, 1, 0));
        One(positions, "FormVersion").ShouldBe(new ValuePosition("FormVersion", ValuePositionKind.Value, 20, 2, null, -1, -1, 20, 1, 0));
        One(positions, "Version2").ShouldBe(new ValuePosition("Version2", ValuePositionKind.Value, 22, 2, null, -1, -1, 22, 1, 0));

        // HEDR
        One(positions, "Stats", ValuePositionKind.Object).Start.ShouldBe(Hedr);
        One(positions, "Stats.Version").ShouldBe(new ValuePosition("Stats.Version", ValuePositionKind.Value, Hedr, 4, new RecordType("HEDR"), 0, 0, 0, 1, 0));
        One(positions, "Stats.NumRecords").ShouldBe(new ValuePosition("Stats.NumRecords", ValuePositionKind.Value, Hedr + 4, 4, new RecordType("HEDR"), 0, 0, 4, 1, 0));
        One(positions, "Stats.NextFormID").ShouldBe(new ValuePosition("Stats.NextFormID", ValuePositionKind.Value, Hedr + 8, 4, new RecordType("HEDR"), 0, 0, 8, 1, 0));

        // Texts
        One(positions, "Author").ShouldBe(new ValuePosition("Author", ValuePositionKind.Value, Cnam, 7, new RecordType("CNAM"), 1, 0, 0, 1, 0));
        One(positions, "Description").ShouldBe(new ValuePosition("Description", ValuePositionKind.Value, Snam, 6, new RecordType("SNAM"), 2, 0, 0, 1, 0));

        // The masters: a list of MAST/DATA pairs
        var masters = One(positions, "MasterReferences", ValuePositionKind.List);
        (masters.Start, masters.SubrecordCount).ShouldBe((Mast0, 4));
        One(positions, "MasterReferences[0]", ValuePositionKind.Object).Start.ShouldBe(Mast0);
        One(positions, "MasterReferences[0].Master").ShouldBe(new ValuePosition("MasterReferences[0].Master", ValuePositionKind.Value, Mast0, 14, new RecordType("MAST"), 3, 0, 0, 1, 0));
        One(positions, "MasterReferences[0].FileSize").ShouldBe(new ValuePosition("MasterReferences[0].FileSize", ValuePositionKind.Value, Data0, 8, new RecordType("DATA"), 4, 0, 0, 1, 0));
        One(positions, "MasterReferences[1].Master").ShouldBe(new ValuePosition("MasterReferences[1].Master", ValuePositionKind.Value, Mast1, 10, new RecordType("MAST"), 5, 1, 0, 1, 0));
        One(positions, "MasterReferences[1].FileSize").ShouldBe(new ValuePosition("MasterReferences[1].FileSize", ValuePositionKind.Value, Data1, 8, new RecordType("DATA"), 6, 1, 0, 1, 0));

        // ONAM: the overridden forms, 4 bytes each
        One(positions, "OverriddenForms", ValuePositionKind.List).Start.ShouldBe(Onam);
        One(positions, "OverriddenForms[0]").ShouldBe(new ValuePosition("OverriddenForms[0]", ValuePositionKind.Value, Onam, 4, new RecordType("ONAM"), 7, 0, 0, 1, 0));
        One(positions, "OverriddenForms[1]").ShouldBe(new ValuePosition("OverriddenForms[1]", ValuePositionKind.Value, Onam + 4, 4, new RecordType("ONAM"), 7, 0, 4, 1, 0));

        One(positions, "INTV").ShouldBe(new ValuePosition("INTV", ValuePositionKind.Value, Intv, 4, new RecordType("INTV"), 8, 0, 0, 1, 0));
        One(positions, "InteriorCellCount").ShouldBe(new ValuePosition("InteriorCellCount", ValuePositionKind.Value, Incc, 4, new RecordType("INCC"), 9, 0, 0, 1, 0));

        // The header as read while recording
        var header = positions.Header.ShouldBeOfType<StarfieldModHeader>();
        header.MasterReferences.Select(m => m.Master).ShouldBe(new[] { StarfieldEsm, OtherEsm });
        header.OverriddenForms!.Select(f => f.FormKey).ShouldBe(new[] { new FormKey(StarfieldEsm, 0xABC), new FormKey(OtherEsm, 0xDEF) });
        header.InteriorCellCount.ShouldBe(2);
        positions.Slice(One(positions, "OverriddenForms[1]")).ToArray().ShouldBe(BitConverter.GetBytes(0x01000DEFu));
    }

    [Fact]
    public void Overlay()
    {
        var (fs, path) = Make();
        using var mod = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, Params(fs));
        Check(mod.ModHeader.GetValuePositions());
    }

    [Fact]
    public void MajorRecordReader_SamePositionsAsOverlay()
    {
        var (fs, path) = Make();
        using var reader = MajorRecordReader.FromPath(new ModPath(Plugin, path), GameRelease.Starfield, Params(fs));
        var read = reader.ReadModHeaderValuePositions();
        Check(read);
        using var mod = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, Params(fs));
        read.Positions.ShouldBe(mod.ModHeader.GetValuePositions().Positions);
    }

    [Fact]
    public void FullImportHeader_Throws()
    {
        var (fs, path) = Make();
        var mod = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, Params(fs));
        Should.Throw<ArgumentException>(() => mod.ModHeader.GetValuePositions());
    }

    /// <summary>
    /// Starfield.esm and an add-on with masters: every position of the header lies inside its bytes, in its subrecord, and
    /// each value position reads back the header's value; every field the header holds has a position
    /// </summary>
    [Fact]
    [Trait("Category", "RealData")]
    public void RealData_HeaderPositionsReadBack()
    {
        var data = Environment.GetEnvironmentVariable("MUTAGEN_STARFIELD_DATA");
        if (string.IsNullOrEmpty(data)) return; // needs the game's Data folder
        var styles = new Cache<IModMasterStyledGetter, ModKey>(x => x.ModKey);
        foreach (var f in Directory.EnumerateFiles(data, "*.esm"))
        {
            try
            {
                styles.Set(new KeyedMasterStyle(ModKey.FromFileName(Path.GetFileName(f)), ModHeaderFrame.FromPath(f, GameRelease.Starfield).MasterStyle));
            }
            catch (Exception e) when (e is IOException or ArgumentException or InvalidDataException)
            {
                // not a plugin Mutagen reads a header from
            }
        }
        var param = new BinaryReadParameters { MasterFlagsLookup = styles };
        foreach (var name in new[] { "Starfield.esm", "ShatteredSpace.esm", "SFBGS00D.esm" })
        {
            var path = Path.Combine(data, name);
            if (!File.Exists(path)) continue;
            var modKey = ModKey.FromFileName(name);
            using var reader = MajorRecordReader.FromPath(new ModPath(modKey, path), GameRelease.Starfield, param);
            var read = reader.ReadModHeaderValuePositions();
            using var mod = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, param);
            var overlay = mod.ModHeader.GetValuePositions();
            overlay.Positions.ShouldBe(read.Positions);
            var header = (StarfieldModHeader)read.Header;
            var checkedValues = CheckReadBack(read, header, modKey);
            // Every field the header holds has a position
            var expected = new List<string> { "Flags", "FormID", "Version", "FormVersion", "Version2", "Stats.Version", "Stats.NumRecords", "Stats.NextFormID" };
            if (header.Author != null) expected.Add("Author");
            if (header.Description != null) expected.Add("Description");
            if (header.TypeOffsets != null) expected.Add("TypeOffsets");
            if (header.Deleted != null) expected.Add("Deleted");
            if (header.Screenshot != null) expected.Add("Screenshot");
            if (header.Branch != null) expected.Add("Branch");
            if (header.INTV != null) expected.Add("INTV");
            if (header.InteriorCellCount != null) expected.Add("InteriorCellCount");
            if (header.CHGL != null) expected.Add("CHGL");
            var subs = read.Subrecords;
            var mast = 0;
            for (int i = 0; i < subs.Count; i++)
            {
                if (subs[i].Type.Type != "MAST") continue;
                expected.Add($"MasterReferences[{mast}].Master");
                // A master's DATA (its file size) is left out by some plugins
                if (i + 1 < subs.Count && subs[i + 1].Type.Type == "DATA") expected.Add($"MasterReferences[{mast}].FileSize");
                mast++;
            }
            mast.ShouldBe(header.MasterReferences.Count);
            for (int i = 0; i < (header.OverriddenForms?.Count ?? 0); i++) expected.Add($"OverriddenForms[{i}]");
            for (int i = 0; i < header.TransientTypes.Count; i++)
            {
                expected.Add($"TransientTypes[{i}].FormType");
                for (int j = 0; j < header.TransientTypes[i].Links.Count; j++) expected.Add($"TransientTypes[{i}].Links[{j}]");
            }
            foreach (var path2 in expected)
            {
                read[path2].Where(p => p.Kind == ValuePositionKind.Value).ShouldHaveSingleItem(path2);
            }
            _output.WriteLine($"{name}: {header.MasterReferences.Count} masters, {header.OverriddenForms?.Count ?? 0} overridden forms, " +
                              $"{read.Positions.Count} positions, {checkedValues} values read back equal");
        }
    }

    private static int CheckReadBack(ModHeaderValuePositions positions, StarfieldModHeader header, ModKey modKey)
    {
        var masters = header.MasterReferences.Select(m => m.Master).ToList();
        var count = 0;
        foreach (var p in positions.Positions)
        {
            (p.Start >= 0 && p.Start + p.Length <= positions.Bytes.Length).ShouldBeTrue(p.Path);
            if (p.Kind != ValuePositionKind.Value) continue;
            if (p.Subrecord != null)
            {
                (p.Offset + p.Length <= positions.Subrecords[p.SubrecordIndex].ContentLength).ShouldBeTrue(p.Path);
            }
            else
            {
                (p.Start + p.Length <= positions.HeaderLength).ShouldBeTrue(p.Path);
            }
            var bytes = positions.Slice(p).ToArray();
            var value = Resolve(header, p.Path);
            switch (value)
            {
                case string s:
                    Encoding.UTF8.GetString(bytes).TrimEnd('\0').ShouldBe(s, p.Path);
                    break;
                case ModKey m:
                    Encoding.ASCII.GetString(bytes).TrimEnd('\0').ToLowerInvariant().ShouldBe(m.FileName.String.ToLowerInvariant(), p.Path);
                    break;
                case float f:
                    BinaryPrimitives.ReadSingleLittleEndian(bytes).ShouldBe(f, p.Path);
                    break;
                case Enum e:
                    IntEq(bytes, Convert.ToInt64(e)).ShouldBeTrue($"{p.Path}: {e} vs {Convert.ToHexString(bytes)}");
                    break;
                case int or uint or ushort or short or long or byte:
                    IntEq(bytes, Convert.ToInt64(value)).ShouldBeTrue($"{p.Path}: {value} vs {Convert.ToHexString(bytes)}");
                    break;
                case ulong u:
                    BinaryPrimitives.ReadUInt64LittleEndian(bytes).ShouldBe(u, p.Path);
                    break;
                case IFormLinkGetter link:
                    var raw = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
                    var index = (int)(raw >> 24);
                    new FormKey(index < masters.Count ? masters[index] : modKey, raw & 0xFFFFFF).ShouldBe(link.FormKey, p.Path);
                    break;
                case MemorySlice<byte> slice:
                    bytes.ShouldBe(slice.ToArray(), p.Path);
                    break;
                case byte[] arr:
                    bytes.ShouldBe(arr, p.Path);
                    break;
                default:
                    throw new InvalidOperationException($"{p.Path}: no read-back for {value?.GetType().Name ?? "null"}");
            }
            count++;
        }
        return count;
    }

    // The bytes as the integer they store: signed or unsigned, whichever the value is
    private static bool IntEq(byte[] b, long v)
    {
        ulong r = 0;
        for (int i = b.Length - 1; i >= 0; i--) r = (r << 8) | b[i];
        var shift = 64 - 8 * b.Length;
        var signed = shift == 0 ? (long)r : ((long)(r << shift)) >> shift;
        return (long)r == v || signed == v;
    }

    private static object? Resolve(object root, string path)
    {
        object? cur = root;
        foreach (var token in path.Replace("[", ".[").Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith('['))
            {
                var i = int.Parse(token[1..^1]);
                cur = ((IEnumerable)cur!).Cast<object?>().ElementAt(i);
                continue;
            }
            cur = cur!.GetType().GetProperty(token, BindingFlags.Public | BindingFlags.Instance)!.GetValue(cur);
        }
        return cur;
    }
}
