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

// C128: the kept subrecord order (C82) must also hold for records whose bytes Mutagen rewrites without them being
// changed. ShatteredSpace.esm REFR 0980D7 holds XFLG before its two XPLK (Mutagen writes XPLK first), and its first
// XPLK points through master index 02 although the plugin has one master: that reads as the plugin itself and is
// written back with the plugin's own index 01, so the record's bytes never match the file's and the kept order was
// dropped as if the record had been changed. Now a subrecord whose read held such a FormID is matched by type and
// field only (full import), and an overlay, which can't be changed, is matched by type and field only.
// The REFR is read from a plugin made of ShatteredSpace.esm's header essentials (flags, HEDR, master Starfield.esm), a
// stand-in interior cell and the real record, with both readers. Written back, its subrecords keep the file's order;
// the only byte that differs is that master index (02 -> 01).
public class OfficialPluginSubrecordOrderTests
{
    private static readonly ModKey Plugin = ModKey.FromFileName("ShatteredSpace.esm");

    // REFR 0980D7:ShatteredSpace.esm: NAME, XFLG, XPLK (index 02), XPLK (index 01), XLYR, DATA
    private const string Refr = "524546525800000000040000D78009017434200046020B004E414D4504000A6A0E0058464C4704004000000058504C4B0800D98009020200000058504C4B0800D980090102000000584C595204005A2E0200444154411800D6146BC18C1528427B4E50C30000000000000080DB0F493F";

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    private static byte[] Sub(string type, byte[] content) =>
        Encoding.ASCII.GetBytes(type).Concat(BitConverter.GetBytes((ushort)content.Length)).Concat(content).ToArray();

    private static byte[] Rec(string type, uint flags, uint formId, byte[] content)
    {
        var s = new MemoryStream();
        s.Write(Encoding.ASCII.GetBytes(type));
        s.Write(BitConverter.GetBytes(content.Length));
        s.Write(BitConverter.GetBytes(flags));
        s.Write(BitConverter.GetBytes(formId));
        s.Write(Hex("7434200046020000")); // version control and form version 582, as in ShatteredSpace.esm
        s.Write(content);
        return s.ToArray();
    }

    private static byte[] Group(byte[] label, int groupType, byte[] content)
    {
        var s = new MemoryStream();
        s.Write("GRUP"u8);
        s.Write(BitConverter.GetBytes(24 + content.Length));
        s.Write(label);
        s.Write(BitConverter.GetBytes(groupType));
        s.Write(new byte[8]);
        s.Write(content);
        return s.ToArray();
    }

    private static byte[] Plugin_()
    {
        var tes4 = Rec("TES4", 0x81, 0,
            Sub("HEDR", Hex("8FC2753F99B50F00001000FF")).Concat(Sub("MAST", Encoding.ASCII.GetBytes("starfield.esm\0"))).ToArray());
        const uint cellId = 0x01000F99;
        var cell = Rec("CELL", 0, cellId, Sub("EDID", Encoding.ASCII.GetBytes("TestInteriorCell\0")));
        var children = Group(BitConverter.GetBytes(cellId), 6, Group(BitConverter.GetBytes(cellId), 9, Hex(Refr)));
        var subBlock = Group(BitConverter.GetBytes(0), 3, cell.Concat(children).ToArray());
        var block = Group(BitConverter.GetBytes(0), 2, subBlock);
        return tes4.Concat(Group("CELL"u8.ToArray(), 0, block)).ToArray();
    }

    private static IEnumerable<string> Signatures(byte[] record)
    {
        int p = 24;
        while (p + 6 <= record.Length)
        {
            yield return Encoding.ASCII.GetString(record, p, 4);
            p += 6 + BitConverter.ToUInt16(record, p + 4);
        }
    }

    private static byte[] WrittenRefr(byte[] file)
    {
        int p = 24 + BitConverter.ToInt32(file, 4);
        while (p + 24 <= file.Length)
        {
            var sig = Encoding.ASCII.GetString(file, p, 4);
            int size = BitConverter.ToInt32(file, p + 4);
            if (sig == "GRUP") { p += 24; continue; }
            if (sig == "REFR") return file.AsSpan(p, 24 + size).ToArray();
            p += 24 + size;
        }
        throw new InvalidOperationException("REFR not written");
    }

    [Fact]
    public void RefrWithOutOfRangeMasterIndex_KeepsFileOrder()
    {
        var fs = new MockFileSystem();
        var path = Path.Combine("C:", "Data", Plugin.FileName);
        fs.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        fs.File.WriteAllBytes(path, Plugin_());
        var lo = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(ModKey.FromFileName("Starfield.esm"), MasterStyle.Full),
            new KeyedMasterStyle(Plugin, MasterStyle.Full),
        });
        var param = new BinaryReadParameters { FileSystem = fs, MasterFlagsLookup = lo };
        var mods = new IStarfieldModGetter[]
        {
            StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, param),
            StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, param),
        };
        // The expected bytes: the file's, with the out-of-range master index written as the plugin's own (02 -> 01)
        var expected = Hex(Refr.Replace("58504C4B0800D980090202000000", "58504C4B0800D980090102000000"));
        foreach (var mod in mods)
        {
            var refr = mod.EnumerateMajorRecords<IPlacedObjectGetter>().ShouldHaveSingleItem();
            refr.FormKey.ShouldBe(new FormKey(Plugin, 0x0980D7));
            refr.PowerLinks!.Count.ShouldBe(2);

            var outPath = Path.Combine("C:", "Out", Plugin.FileName);
            fs.Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            mod.WriteToBinary(outPath, new BinaryWriteParameters
            {
                FileSystem = fs,
                MasterFlagsLookup = lo,
                MastersListContent = MastersListContentOption.NoCheck,
                MastersListOrdering = new MastersListOrderingByLoadOrder(mod.MasterReferences.Select(m => m.Master)),
            });
            var written = WrittenRefr(fs.File.ReadAllBytes(outPath));
            Signatures(written).ShouldBe(new[] { "NAME", "XFLG", "XPLK", "XPLK", "XLYR", "DATA" });
            written.ShouldBe(expected);
        }
    }
}
