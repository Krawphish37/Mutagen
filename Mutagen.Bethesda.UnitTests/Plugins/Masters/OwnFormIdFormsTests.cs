using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Masters;

// C163 (decision 6.6): a Small Starfield plugin can store its own FormIDs in two forms, its full index (01000801 with one
// master, as the Creation Kit writes) and the light form (FE000802, as SF1Edit writes new records), in one file and in
// one record. Mutagen keeps the form each own FormID was stored in, record headers (C147) and links alike, so the plugin
// writes back byte for byte; a changed link and a new link get Mutagen's form (here the full index: a plugin mixing both
// forms keeps the default).
public class OwnFormIdFormsTests
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

    // HEDR: version 0.96, 3 records + 1 (the CK's count), next object ID 0x804
    private static readonly byte[] File = Record("TES4", 0x101, 0,
            Sub("HEDR", Convert.FromHexString("8FC2753F0400000004080000"))
                .Concat(Sub("MAST", Encoding.ASCII.GetBytes("Starfield.esm\0"))).ToArray())
        .Concat(Group("KYWD",
            Record("KYWD", 0, 0x01000801, Sub("EDID", Encoding.ASCII.GetBytes("FullIndexKeyword\0"))),
            Record("KYWD", 0, 0xFE000802, Sub("EDID", Encoding.ASCII.GetBytes("LightKeyword\0")))))
        .Concat(Group("FLST",
            Record("FLST", 0, 0xFE000803, Sub("EDID", Encoding.ASCII.GetBytes("MixedList\0"))
                .Concat(Sub("LNAM", BitConverter.GetBytes(0xFE000802u)))
                .Concat(Sub("LNAM", BitConverter.GetBytes(0x01000801u))).ToArray())))
        .ToArray();

    private sealed record Setup(MockFileSystem Fs, string Path, ModKey Plugin, LoadOrder<IModMasterStyledGetter> Lo);

    private static Setup Make()
    {
        var plugin = ModKey.FromFileName("MixedForms.esm");
        var fs = new MockFileSystem();
        var path = System.IO.Path.Combine("C:", "Data", plugin.FileName);
        fs.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        fs.File.WriteAllBytes(path, File);
        var lo = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
            new KeyedMasterStyle(plugin, MasterStyle.Small),
        });
        return new Setup(fs, path, plugin, lo);
    }

    private static BinaryReadParameters ReadParams(Setup s) => new() { FileSystem = s.Fs, MasterFlagsLookup = s.Lo };

    private static byte[] Write(Setup s, IStarfieldModGetter mod)
    {
        var outPath = System.IO.Path.Combine("C:", "Out", s.Plugin.FileName);
        s.Fs.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath)!);
        mod.WriteToBinary(outPath, new BinaryWriteParameters { FileSystem = s.Fs, MasterFlagsLookup = s.Lo });
        return s.Fs.File.ReadAllBytes(outPath);
    }

    private static List<uint> Lnams(byte[] file)
    {
        var ret = new List<uint>();
        for (int i = 0; i + 10 <= file.Length; i++)
        {
            if (file[i] == 'L' && file[i + 1] == 'N' && file[i + 2] == 'A' && file[i + 3] == 'M'
                && BitConverter.ToUInt16(file, i + 4) == 4)
            {
                ret.Add(BitConverter.ToUInt32(file, i + 6));
            }
        }
        return ret;
    }

    [Fact]
    public void MixedSmallPlugin_RoundTripsByteForByte_BothReaders()
    {
        var s = Make();
        Write(s, StarfieldMod.CreateFromBinary(s.Path, StarfieldRelease.Starfield, ReadParams(s))).ShouldBe(File);
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(s.Path, StarfieldRelease.Starfield, ReadParams(s));
        Write(s, overlay).ShouldBe(File);
    }

    [Fact]
    public void DeepCopy_KeepsTheStoredForms()
    {
        var s = Make();
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(s.Path, StarfieldRelease.Starfield, ReadParams(s));
        Write(s, StarfieldModMixIn.DeepCopy(overlay)).ShouldBe(File);
    }

    [Fact]
    public void ChangedAndNewLinks_GetMutagensForm()
    {
        var s = Make();
        var mod = StarfieldMod.CreateFromBinary(s.Path, StarfieldRelease.Starfield, ReadParams(s));
        var list = mod.FormLists.Single();
        var own801 = new FormKey(s.Plugin, 0x801);
        var own802 = new FormKey(s.Plugin, 0x802);
        // The first link (stored FE000802) now points at 000801; a third link to 000802 is added
        list.Items[0] = own801.ToLink<IStarfieldMajorRecordGetter>();
        list.Items.Add(own802.ToLink<IStarfieldMajorRecordGetter>());

        // Changed: Mutagen's form, the size's (FE, decision A3). Unchanged second link: as stored. New: the size's form
        Lnams(Write(s, mod)).ShouldBe(new uint[] { 0xFE000801, 0x01000801, 0xFE000802 });
    }

    // Every record's header FormID, in file order
    private static List<uint> Headers(byte[] file)
    {
        var ret = new List<uint>();
        int p = 24 + BitConverter.ToInt32(file, 4);
        while (p + 24 <= file.Length)
        {
            var sig = Encoding.ASCII.GetString(file, p, 4);
            int size = BitConverter.ToInt32(file, p + 4);
            if (sig == "GRUP") { p += 24; continue; }
            ret.Add(BitConverter.ToUInt32(file, p + 12));
            p += 24 + size;
        }
        return ret;
    }

    [Theory]
    [InlineData(StarfieldModHeader.HeaderFlag.Light, 0xFE000000u)]
    [InlineData(StarfieldModHeader.HeaderFlag.Medium, 0xFD000000u)]
    public void NewSmallOrMediumMod_WritesTheSizesForm(StarfieldModHeader.HeaderFlag size, uint form)
    {
        var s = Make();
        var mod = new StarfieldMod(s.Plugin, StarfieldRelease.Starfield);
        mod.ModHeader.Flags |= size;
        var kw = mod.Keywords.AddNew("NewKeyword");
        var list = mod.FormLists.AddNew("NewList");
        list.Items.Add(kw.ToLink<IStarfieldMajorRecordGetter>());
        // A new mod not read from a file, no masters: FE000 + object ID (Small), FD00 + object ID (Medium)
        var bytes = Write(s, mod);
        Headers(bytes).ShouldBe(new[] { form | kw.FormKey.ID, form | list.FormKey.ID });
        Lnams(bytes).ShouldBe(new[] { form | kw.FormKey.ID });
    }

    [Fact]
    public void NewSmallMod_ObjectIdAboveFff_NeverMasked()
    {
        var s = Make();
        var mod = new StarfieldMod(s.Plugin, StarfieldRelease.Starfield);
        mod.ModHeader.Flags |= StarfieldModHeader.HeaderFlag.Light;
        var list = mod.FormLists.AddNew("NewList");
        // A link to own object 0x1802: no FE form holds it, and masking would make it 0x802. Written in the full index,
        // which holds it with the same meaning
        list.Items.Add(new FormKey(s.Plugin, 0x1802).ToLink<IStarfieldMajorRecordGetter>());
        Lnams(Write(s, mod)).ShouldBe(new uint[] { 0x00001802 });
    }

    // test_ck.esm (Creation Kit export of a small .esm, its TES4 and keyword group): the CK writes the plugin's own
    // keyword in the full form, 03000809 (three full masters). SF1Edit added a keyword to it as FE002800 (two small
    // masters) and left 03000809 as it was (test_xedit.esm); Mutagen does the same, except for the object ID: SF1Edit
    // took the stored next ID (0x800), while Mutagen's allocator (C164) starts above the highest own ID (0x809), so the
    // new keyword is FE00280A.
    private const string CkTes4 = "54455334c700000001010000000000000000000046020000484544520c008fc2753f0400000000080003434e414d080044454641554c54004d4153540e00537461726669656c642e65736d004d4153540d0053464247533030442e65736d004d4153540d006461726b737461722e65736d004d4153540c006275626c756d692e65736d004d4153541a006461726b7374617270617463685f6275626c756d692e65736d004d4153540d0053464247533030332e65736d004d4153540d0053464247533030362e65736d00424e414d05004d61696e00494e4343040000000000";
    private const string CkKeywords = "47525550640000004b595744000000006d350000000000004b595744340000000000000009080003000000004602000045444944100078525f486f6c737465725f5465737400434e414d0400ffffff00544e414d040015000000464e414d040000000000";

    [Fact]
    public void CreationKitFullFormFile_NewKeywordInFeForm_StoredOneKept()
    {
        var plugin = ModKey.FromFileName("test_ck.esm");
        var fs = new MockFileSystem();
        var path = System.IO.Path.Combine("C:", "Data", plugin.FileName);
        fs.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var file = Convert.FromHexString(CkTes4 + CkKeywords);
        fs.File.WriteAllBytes(path, file);
        var lo = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
            new KeyedMasterStyle(ModKey.FromFileName("SFBGS00D.esm"), MasterStyle.Small),
            new KeyedMasterStyle(ModKey.FromFileName("darkstar.esm"), MasterStyle.Full),
            new KeyedMasterStyle(ModKey.FromFileName("bublumi.esm"), MasterStyle.Full),
            new KeyedMasterStyle(ModKey.FromFileName("darkstarpatch_bublumi.esm"), MasterStyle.Small),
            new KeyedMasterStyle(ModKey.FromFileName("SFBGS003.esm"), MasterStyle.Medium),
            new KeyedMasterStyle(ModKey.FromFileName("SFBGS006.esm"), MasterStyle.Medium),
            new KeyedMasterStyle(plugin, MasterStyle.Small),
        });
        var s = new Setup(fs, path, plugin, lo);

        // Unchanged: byte for byte, both readers
        Write(s, StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, ReadParams(s))).ShouldBe(file);
        using (var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, ReadParams(s)))
        {
            Write(s, overlay).ShouldBe(file);
        }

        // A new keyword in the FE form, as SF1Edit wrote it (its ID from C164: above the highest own ID, 0x809); the
        // CK's 03000809 stays
        var mod = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, ReadParams(s));
        var added = mod.Keywords.AddNew("xR_Holster_Test2");
        added.FormKey.ID.ShouldBe(0x80Au);
        Headers(Write(s, mod)).ShouldBe(new uint[] { 0x03000809, 0xFE00280A });
    }
}
