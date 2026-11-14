using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C188: a Medium plugin can store its records in FD form and their child-group labels in full-index form
// (RoleplayersAlternateStart.esm: CELL FD001DC3 with cell, persistent and temporary children labelled 02001DC3; 336 such
// labels). The labels are kept as stored while they name the same parent, so the plugin writes back byte for byte; a
// new cell's groups get its header's form.
public class GroupLabelFormTests
{
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");
    private static readonly ModKey Plugin = ModKey.FromFileName("LabelForms.esm");

    private static byte[] Sub(string type, byte[] content) =>
        Encoding.ASCII.GetBytes(type).Concat(BitConverter.GetBytes((ushort)content.Length)).Concat(content).ToArray();

    private static byte[] Record(string type, uint formId, byte[] content)
    {
        var s = new MemoryStream();
        s.Write(Encoding.ASCII.GetBytes(type));
        s.Write(BitConverter.GetBytes(content.Length));
        s.Write(BitConverter.GetBytes(0));
        s.Write(BitConverter.GetBytes(formId));
        s.Write(Convert.FromHexString("0000000046020000"));
        s.Write(content);
        return s.ToArray();
    }

    private static byte[] Group(uint label, int type, params byte[][] content)
    {
        var body = content.SelectMany(r => r).ToArray();
        var s = new MemoryStream();
        s.Write("GRUP"u8);
        s.Write(BitConverter.GetBytes(24 + body.Length));
        s.Write(BitConverter.GetBytes(label));
        s.Write(BitConverter.GetBytes(type));
        s.Write(new byte[8]);
        s.Write(body);
        return s.ToArray();
    }

    // A Medium master with one Full master: own records FD00xxxx, own full index 01. Written by Mutagen (cell FD000801
    // holding REFR FD000802), then its cell and temporary children labels set to the full-index form 01000801, as
    // RoleplayersAlternateStart.esm stores them
    private static readonly byte[] File = MakeFile();

    private static byte[] MakeFile()
    {
        var mod = new StarfieldMod(Plugin, StarfieldRelease.Starfield) { IsMediumMaster = true };
        var cell = new Cell(new FormKey(Plugin, 0x801), StarfieldRelease.Starfield) { EditorID = "LabelCell" };
        cell.Temporary.Add(new PlacedObject(new FormKey(Plugin, 0x802), StarfieldRelease.Starfield)
        {
            Base = new FormKey(StarfieldEsm, 0xABC).ToNullableLink<IPlaceableObjectGetter>()
        });
        mod.Cells.Records.Add(new CellBlock
        {
            BlockNumber = 1,
            GroupType = GroupTypeEnum.InteriorCellBlock,
            SubBlocks =
            {
                new CellSubBlock { BlockNumber = 1, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = { cell } },
            },
        });
        var fs = new MockFileSystem();
        var path = System.IO.Path.Combine("C:", "Make", Plugin.FileName);
        fs.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        mod.WriteToBinary(path, new BinaryWriteParameters
        {
            FileSystem = fs,
            MasterFlagsLookup = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
            {
                new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
                new KeyedMasterStyle(Plugin, MasterStyle.Medium),
            }),
        });
        var bytes = fs.File.ReadAllBytes(path);
        int p = 24 + BitConverter.ToInt32(bytes, 4), patched = 0;
        while (p + 24 <= bytes.Length)
        {
            if (Encoding.ASCII.GetString(bytes, p, 4) == "GRUP")
            {
                var type = BitConverter.ToInt32(bytes, p + 12);
                if (type is 6 or 9)
                {
                    BitConverter.ToUInt32(bytes, p + 8).ShouldBe(0xFD000801u);
                    BitConverter.GetBytes(0x01000801u).CopyTo(bytes, p + 8);
                    patched++;
                }
                p += 24;
                continue;
            }
            p += 24 + BitConverter.ToInt32(bytes, p + 4);
        }
        patched.ShouldBe(2);
        return bytes;
    }

    private sealed record Setup(MockFileSystem Fs, string Path, LoadOrder<IModMasterStyledGetter> Lo);

    private static Setup Make()
    {
        var fs = new MockFileSystem();
        var path = System.IO.Path.Combine("C:", "Data", Plugin.FileName);
        fs.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        fs.File.WriteAllBytes(path, File);
        var lo = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
            new KeyedMasterStyle(Plugin, MasterStyle.Medium),
        });
        return new Setup(fs, path, lo);
    }

    private static BinaryReadParameters ReadParams(Setup s) => new() { FileSystem = s.Fs, MasterFlagsLookup = s.Lo };

    private static byte[] Write(Setup s, IStarfieldModGetter mod)
    {
        var outPath = System.IO.Path.Combine("C:", "Out", Plugin.FileName);
        s.Fs.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath)!);
        mod.WriteToBinary(outPath, new BinaryWriteParameters { FileSystem = s.Fs, MasterFlagsLookup = s.Lo });
        return s.Fs.File.ReadAllBytes(outPath);
    }

    // Labels of the cell child groups (types 6, 8, 9), in file order
    private static List<uint> ChildLabels(byte[] file)
    {
        var ret = new List<uint>();
        int p = 24 + BitConverter.ToInt32(file, 4);
        while (p + 24 <= file.Length)
        {
            var sig = Encoding.ASCII.GetString(file, p, 4);
            if (sig == "GRUP")
            {
                var type = BitConverter.ToInt32(file, p + 12);
                if (type is 6 or 8 or 9) ret.Add(BitConverter.ToUInt32(file, p + 8));
                p += 24;
                continue;
            }
            p += 24 + BitConverter.ToInt32(file, p + 4);
        }
        return ret;
    }

    [Fact]
    public void LabelsInFullIndexForm_UnderFdRecords_WriteBackByteForByte_BothReaders()
    {
        var s = Make();
        Same(Write(s, StarfieldMod.CreateFromBinary(s.Path, StarfieldRelease.Starfield, ReadParams(s))));
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(s.Path, StarfieldRelease.Starfield, ReadParams(s));
        Same(Write(s, overlay));
        Same(Write(s, StarfieldModMixIn.DeepCopy(overlay)));
    }

    private static void Same(byte[] written)
    {
        var i = 0;
        while (i < written.Length && i < File.Length && written[i] == File[i]) i++;
        if (i == written.Length && i == File.Length) return;
        throw new Xunit.Sdk.XunitException($"first difference at {i} (lengths {written.Length} / {File.Length}): written {Convert.ToHexString(written.AsSpan(Math.Max(0, i - 8), Math.Min(32, written.Length - Math.Max(0, i - 8))))} file {Convert.ToHexString(File.AsSpan(Math.Max(0, i - 8), Math.Min(32, File.Length - Math.Max(0, i - 8))))}");
    }

    [Fact]
    public void NewCell_GetsItsHeadersForm()
    {
        var s = Make();
        var mod = StarfieldMod.CreateFromBinary(s.Path, StarfieldRelease.Starfield, ReadParams(s));
        var cell = new Cell(new FormKey(Plugin, 0x803), StarfieldRelease.Starfield) { EditorID = "NewCell" };
        cell.Temporary.Add(new PlacedObject(new FormKey(Plugin, 0x804), StarfieldRelease.Starfield));
        mod.Cells.Records[0].SubBlocks[0].Cells.Add(cell);
        // The stored cell keeps 01000801 on its two groups; the new one gets FD000803, its header's form
        ChildLabels(Write(s, mod)).ShouldBe(new uint[] { 0x01000801, 0x01000801, 0xFD000803, 0xFD000803 });
    }
}
