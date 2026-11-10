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

// C130: a Small or Medium Starfield plugin stores the FormIDs of its own records either in its own style (FE / FD, all
// official Creations) or as a full index (many mods). Mutagen decided that from a hard-coded list of official plugins;
// it now reads the plugin's own record headers and writes it back the same way. A plugin with no own records (or
// mixing both) keeps the default (the list). Each test builds a plugin from real records (header flags, HEDR and master
// as in the file) and reads it with both readers; the records come back byte for byte.
public class OwnFormIdStylePerFileTests
{
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    private static byte[] Sub(string type, byte[] content) =>
        Encoding.ASCII.GetBytes(type).Concat(BitConverter.GetBytes((ushort)content.Length)).Concat(content).ToArray();

    private static byte[] Tes4(uint flags, string hedr) =>
        Record("TES4", flags, 0, Sub("HEDR", Hex(hedr)).Concat(Sub("MAST", Encoding.ASCII.GetBytes("Starfield.esm\0"))).ToArray());

    private static byte[] Record(string type, uint flags, uint formId, byte[] content)
    {
        var s = new MemoryStream();
        s.Write(Encoding.ASCII.GetBytes(type));
        s.Write(BitConverter.GetBytes(content.Length));
        s.Write(BitConverter.GetBytes(flags));
        s.Write(BitConverter.GetBytes(formId));
        s.Write(Hex("0000000046020000"));
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

    private sealed record Setup(MockFileSystem Fs, string Path, ModKey Plugin, LoadOrder<IModMasterStyledGetter> Lo);

    private static Setup Make(string fileName, MasterStyle style, byte[] file)
    {
        var fs = new MockFileSystem();
        var plugin = ModKey.FromFileName(fileName);
        var path = Path.Combine("C:", "Data", fileName);
        fs.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        fs.File.WriteAllBytes(path, file);
        var lo = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
            new KeyedMasterStyle(plugin, style),
        });
        return new Setup(fs, path, plugin, lo);
    }

    private static IEnumerable<IStarfieldModGetter> ReadBoth(Setup s)
    {
        var param = new BinaryReadParameters { FileSystem = s.Fs, MasterFlagsLookup = s.Lo };
        yield return StarfieldMod.CreateFromBinary(s.Path, StarfieldRelease.Starfield, param);
        yield return StarfieldMod.CreateFromBinaryOverlay(s.Path, StarfieldRelease.Starfield, param);
    }

    private static byte[] WriteBack(Setup s, IStarfieldModGetter mod)
    {
        var outPath = Path.Combine("C:", "Out", s.Plugin.FileName);
        s.Fs.Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        mod.WriteToBinary(outPath, new BinaryWriteParameters
        {
            FileSystem = s.Fs,
            MasterFlagsLookup = s.Lo,
            MastersListContent = MastersListContentOption.NoCheck,
            MastersListOrdering = new MastersListOrderingByLoadOrder(mod.MasterReferences.Select(m => m.Master)),
        });
        return s.Fs.File.ReadAllBytes(outPath);
    }

    // sfbgs00a_a.esm (official Creation, Small): its real header, OMOD FE000002 and COBJ FE00000A, whose CNAM points at
    // that OMOD
    private const string OfficialTes4 = "54455334630000008101000000000000F93020003F020000484544520C008FC2753F0C000000001000FF434E414D0700507974686F6E00534E414D0700536372697074004D4153540E00737461726669656C642E65736D00424E414D09004465765F4D61696E00494E43430400000000004348474C04003C2C3E00";
    private const string OfficialOmod = "4F4D4F44C100000000000000020000FEF93020003F0207004544494426006D6F645F41564D5F4472756D426561745F6353464247533030415F415F426C61636B6F75740046554C4C04000300000044455343040000000000464C4C44040003000000444154416700000000000200000000001B0000005445534F626A656374574541505F496E7374616E6365446174610000006C071F0000000000000000000400000002000000574B4559010000FE00000000000000000400000002000000574B4559FA4E370000000000000000004D4E414D0400FCBA0500";
    private const string OfficialCobj = "434F424A9B000000000000000A0000FEF93020003F020200454449443500636F5F6D6F645F41564D5F576561706F6E5F536B696E5F6353464247533030415F415F426C61636B6F75745F4472756D626561740044455343040000000000424E414D0400C0E12C00434E414D0400020000FE4E4E414D02000000534E414D040000000000544E414D0100004C524E4D01000344415441040000000000464E414D040002C507005245434608000200000000000000";

    [Fact]
    public void OfficialCreation_OwnStyle()
    {
        var s = Make("sfbgs00a_a.esm", MasterStyle.Small,
            Hex(OfficialTes4).Concat(Group("OMOD", Hex(OfficialOmod))).Concat(Group("COBJ", Hex(OfficialCobj))).ToArray());
        foreach (var mod in ReadBoth(s))
        {
            OwnFormIdStyle.Get(mod).ShouldBe(true);
            mod.ConstructibleObjects.ShouldHaveSingleItem().CreatedObject.FormKey.ShouldBe(new FormKey(s.Plugin, 0x002));
            var written = WriteBack(s, mod);
            RecordBytes(written, "COBJ").ShouldBe(Hex(OfficialCobj));
            RecordBytes(written, "OMOD").ShouldBe(Hex(OfficialOmod));
        }
    }

    // atom74.esm (a mod, Small, localized): its own records are FE (all 317). KYWD ma_ccAtom74Dusty (FE00099A:atom74.esm)
    private const string Atom74Kywd = "4B59574435000000000000009A0900FE88340000460200004544494411006D615F636341746F6D3734447573747900434E414D0400FFFFFF00544E414D040005000000464E414D040000000000";

    [Fact]
    public void ModInOwnStyle_OwnStyle_AlsoThroughDeepCopy()
    {
        var s = Make("atom74.esm", MasterStyle.Small,
            Tes4(0x181, "8FC2753F49010000000800FF").Concat(Group("KYWD", Hex(Atom74Kywd))).ToArray());
        foreach (var mod in ReadBoth(s))
        {
            OwnFormIdStyle.Get(mod).ShouldBe(true);
            mod.Keywords.ShouldHaveSingleItem().FormKey.ShouldBe(new FormKey(s.Plugin, 0x99A));
            RecordBytes(WriteBack(s, mod), "KYWD").ShouldBe(Hex(Atom74Kywd));
            var copy = (IStarfieldModGetter)mod.DeepCopy();
            OwnFormIdStyle.Get(copy).ShouldBe(true);
            RecordBytes(WriteBack(s, copy), "KYWD").ShouldBe(Hex(Atom74Kywd));
        }
    }

    // AccurateADS.esm (a mod, Small): its own records use a full index (01, all 7).
    // AVIF AccurateAimingAV_1st (01000811:AccurateADS.esm)
    private const string AccurateAdsAvif = "415649463900000000000000110800010000000046020000454449441500416363757261746541696D696E6741565F317374004E414D3004000000003F4156464C0400002000004E414D31040008000000";

    [Fact]
    public void ModWithFullIndex_FullIndex()
    {
        var s = Make("AccurateADS.esm", MasterStyle.Small,
            Tes4(0x101, "8FC2753F0C00000000080001").Concat(Group("AVIF", Hex(AccurateAdsAvif))).ToArray());
        foreach (var mod in ReadBoth(s))
        {
            OwnFormIdStyle.Get(mod).ShouldBe(false);
            mod.ActorValueInformation.ShouldHaveSingleItem().FormKey.ShouldBe(new FormKey(s.Plugin, 0x811));
            RecordBytes(WriteBack(s, mod), "AVIF").ShouldBe(Hex(AccurateAdsAvif));
        }
    }

    // A Small plugin that only overrides a Starfield.esm record has no own records: the default applies (not one of the
    // official plugins, so a full index for any record added to it)
    [Fact]
    public void NoOwnRecords_Default()
    {
        var kywd = Record("KYWD", 0, 0x0000ABCD, Sub("EDID", Encoding.ASCII.GetBytes("TestOverride\0")));
        var s = Make("TestOverridesOnly.esm", MasterStyle.Small,
            Tes4(0x101, "8FC2753F01000000000800FF").Concat(Group("KYWD", kywd)).ToArray());
        foreach (var mod in ReadBoth(s))
        {
            OwnFormIdStyle.Get(mod).ShouldBeNull();
            mod.Keywords.ShouldHaveSingleItem().FormKey.ShouldBe(new FormKey(StarfieldEsm, 0xABCD));
            RecordBytes(WriteBack(s, mod), "KYWD").ShouldBe(kywd);
        }
    }
}
