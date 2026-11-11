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

// The master list of a Starfield plugin on the default write (no MastersListOrdering given). The default order sorts by
// name, which puts Starfield.esm after "sfbgs..." and "ShatteredSpace.esm": darkstar.esm's Starfield.esm, sfbgs00d.esm,
// sfbgs003.esm came back as sfbgs003.esm, sfbgs00d.esm, Starfield.esm, and every master FormID was re-encoded (12,472
// GBFM, 9,790 REFR, 80 group labels). A plugin read from a file now keeps its masters in the file's order; masters it
// didn't have come after them; an explicit load order still wins; a new mod lists Starfield.esm first.
public class MasterOrderDefaultTests
{
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");
    private static readonly ModKey Sfbgs00d = ModKey.FromFileName("sfbgs00d.esm");
    private static readonly ModKey Sfbgs003 = ModKey.FromFileName("sfbgs003.esm");
    private static readonly ModKey ShatteredSpace = ModKey.FromFileName("ShatteredSpace.esm");
    private static readonly ModKey Plugin = ModKey.FromFileName("MasterOrderTest.esm");

    private static byte[] Sub(string type, byte[] content) =>
        Encoding.ASCII.GetBytes(type).Concat(BitConverter.GetBytes((ushort)content.Length)).Concat(content).ToArray();

    private static byte[] Record(string type, uint formId, params byte[][] subs)
    {
        var content = subs.SelectMany(x => x).ToArray();
        var s = new MemoryStream();
        s.Write(Encoding.ASCII.GetBytes(type));
        s.Write(BitConverter.GetBytes(content.Length));
        s.Write(BitConverter.GetBytes(0));
        s.Write(BitConverter.GetBytes(formId));
        s.Write(Convert.FromHexString("0000000046020000"));
        s.Write(content);
        return s.ToArray();
    }

    private static byte[] Keyword(uint formId, string edid) =>
        Record("KYWD", formId, Sub("EDID", Encoding.ASCII.GetBytes(edid + "\0")));

    // darkstar.esm's master list: Starfield.esm, sfbgs00d.esm, sfbgs003.esm; a keyword from each, and one of its own
    private static byte[] DarkstarLike()
    {
        var tes4 = Record("TES4", 0,
            Sub("HEDR", Convert.FromHexString("8FC2753F0500000000080000")),
            Sub("MAST", Encoding.ASCII.GetBytes("Starfield.esm\0")),
            Sub("MAST", Encoding.ASCII.GetBytes("sfbgs00d.esm\0")),
            Sub("MAST", Encoding.ASCII.GetBytes("sfbgs003.esm\0")));
        var keywords = new[]
        {
            Keyword(0x00000ABC, "FromStarfield"),
            Keyword(0x01000DEF, "FromSfbgs00d"),
            Keyword(0x02000123, "FromSfbgs003"),
            Keyword(0x03000800, "OwnKeyword"),
        }.SelectMany(x => x).ToArray();
        var group = new MemoryStream();
        group.Write("GRUP"u8);
        group.Write(BitConverter.GetBytes(24 + keywords.Length));
        group.Write("KYWD"u8);
        group.Write(BitConverter.GetBytes(0));
        group.Write(new byte[8]);
        group.Write(keywords);
        // The TES4 flags field is 0 above; mark the plugin as a master (0x1)
        tes4[8] = 0x1;
        return tes4.Concat(group.ToArray()).ToArray();
    }

    private sealed record Setup(MockFileSystem Fs, string Path, LoadOrder<IModMasterStyledGetter> Lo);

    private static Setup Make(byte[] file)
    {
        var fs = new MockFileSystem();
        var path = Path.Combine("C:", "Data", Plugin.FileName);
        fs.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        fs.File.WriteAllBytes(path, file);
        var lo = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
            new KeyedMasterStyle(ShatteredSpace, MasterStyle.Full),
            new KeyedMasterStyle(Sfbgs00d, MasterStyle.Full),
            new KeyedMasterStyle(Sfbgs003, MasterStyle.Full),
            new KeyedMasterStyle(Plugin, MasterStyle.Full),
        });
        return new Setup(fs, path, lo);
    }

    private static byte[] Write(Setup s, IStarfieldModGetter mod, AMastersListOrderingOption? ordering = null)
    {
        var outPath = Path.Combine("C:", "Out", Plugin.FileName);
        s.Fs.Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        mod.WriteToBinary(outPath, new BinaryWriteParameters
        {
            FileSystem = s.Fs,
            MasterFlagsLookup = s.Lo,
            MastersListOrdering = ordering,
        });
        return s.Fs.File.ReadAllBytes(outPath);
    }

    private static string[] Masters(byte[] file)
    {
        var size = BitConverter.ToInt32(file, 4);
        var ret = new List<string>();
        for (int p = 24; p < 24 + size;)
        {
            int len = BitConverter.ToUInt16(file, p + 4);
            if (Encoding.ASCII.GetString(file, p, 4) == "MAST") ret.Add(Encoding.ASCII.GetString(file, p + 6, len - 1));
            p += 6 + len;
        }
        return ret.ToArray();
    }

    private static IEnumerable<IStarfieldModGetter> ReadAll(Setup s)
    {
        var param = new BinaryReadParameters { FileSystem = s.Fs, MasterFlagsLookup = s.Lo };
        var full = StarfieldMod.CreateFromBinary(s.Path, StarfieldRelease.Starfield, param);
        var overlay = StarfieldMod.CreateFromBinaryOverlay(s.Path, StarfieldRelease.Starfield, param);
        return new IStarfieldModGetter[] { full, overlay, (IStarfieldModGetter)full.DeepCopy(), (IStarfieldModGetter)overlay.DeepCopy() };
    }

    [Fact]
    public void DarkstarLikeMasters_RoundTripByteForByte()
    {
        var file = DarkstarLike();
        var s = Make(file);
        foreach (var mod in ReadAll(s))
        {
            var written = Write(s, mod);
            Masters(written).ShouldBe(new[] { "Starfield.esm", "sfbgs00d.esm", "sfbgs003.esm" });
            written.ShouldBe(file);
        }
    }

    [Fact]
    public void AddedMaster_ComesAfterTheKeptOnes()
    {
        var s = Make(DarkstarLike());
        var mod = StarfieldMod.CreateFromBinary(s.Path, StarfieldRelease.Starfield,
            new BinaryReadParameters { FileSystem = s.Fs, MasterFlagsLookup = s.Lo });
        mod.Keywords.Add(new Keyword(new FormKey(ShatteredSpace, 0x456), StarfieldRelease.Starfield) { EditorID = "FromShatteredSpace" });
        Masters(Write(s, mod)).ShouldBe(new[] { "Starfield.esm", "sfbgs00d.esm", "sfbgs003.esm", "ShatteredSpace.esm" });
    }

    [Fact]
    public void ExplicitLoadOrder_StillWins()
    {
        var s = Make(DarkstarLike());
        var mod = StarfieldMod.CreateFromBinary(s.Path, StarfieldRelease.Starfield,
            new BinaryReadParameters { FileSystem = s.Fs, MasterFlagsLookup = s.Lo });
        Masters(Write(s, mod, new MastersListOrderingByLoadOrder(new[] { Sfbgs003, StarfieldEsm, Sfbgs00d, Plugin })))
            .ShouldBe(new[] { "sfbgs003.esm", "Starfield.esm", "sfbgs00d.esm" });
    }

    [Fact]
    public void NewMod_ListsStarfieldEsmFirst()
    {
        var s = Make(DarkstarLike());
        var mod = new StarfieldMod(Plugin, StarfieldRelease.Starfield);
        mod.Keywords.Add(new Keyword(new FormKey(ShatteredSpace, 0x456), StarfieldRelease.Starfield) { EditorID = "A" });
        mod.Keywords.Add(new Keyword(new FormKey(Sfbgs00d, 0x457), StarfieldRelease.Starfield) { EditorID = "B" });
        mod.Keywords.Add(new Keyword(new FormKey(StarfieldEsm, 0x458), StarfieldRelease.Starfield) { EditorID = "C" });
        Masters(Write(s, mod)).ShouldBe(new[] { "Starfield.esm", "sfbgs00d.esm", "ShatteredSpace.esm" });
    }
}
