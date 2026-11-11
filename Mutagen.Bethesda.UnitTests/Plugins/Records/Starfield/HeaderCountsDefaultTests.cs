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

// TES4 HEDR record count and next object ID, Starfield's default write (RecordCountOption.Auto, NextFormIDOption.Auto).
// The Creation Kit counts the records + 1 (TES4), no groups (Starfield.esm 3,829,247 = 3,829,246 + 1, and every
// CK-made official plugin), and stores FF001000 as next object ID. xEdit counts records + groups and stores the highest
// own ID + 1 (weap_starlight.esm: 93 = 85 + 8, 00000888), or keeps a stale one (darkstar.esm 02000800). Mutagen's old
// default recounted both (records + one per top-level group; highest own ID + 1), so every plugin's header changed on a
// plain rewrite. The plugins below use real TES4 headers with a few of their records.
public class HeaderCountsDefaultTests
{
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");

    // sfbgs00a_a.esm (official Creation, Small): HEDR count 12, next FF001000; its OMOD FE000002 and COBJ FE00000A
    private const string CkTes4 = "54455334630000008101000000000000F93020003F020000484544520C008FC2753F0C000000001000FF434E414D0700507974686F6E00534E414D0700536372697074004D4153540E00737461726669656C642E65736D00424E414D09004465765F4D61696E00494E43430400000000004348474C04003C2C3E00";
    private const string CkOmod = "4F4D4F44C100000000000000020000FEF93020003F0207004544494426006D6F645F41564D5F4472756D426561745F6353464247533030415F415F426C61636B6F75740046554C4C04000300000044455343040000000000464C4C44040003000000444154416700000000000200000000001B0000005445534F626A656374574541505F496E7374616E6365446174610000006C071F0000000000000000000400000002000000574B4559010000FE00000000000000000400000002000000574B4559FA4E370000000000000000004D4E414D0400FCBA0500";
    private const string CkCobj = "434F424A9B000000000000000A0000FEF93020003F020200454449443500636F5F6D6F645F41564D5F576561706F6E5F536B696E5F6353464247533030415F415F426C61636B6F75745F4472756D626561740044455343040000000000424E414D0400C0E12C00434E414D0400020000FE4E4E414D02000000534E414D040000000000544E414D0100004C524E4D01000344415441040000000000464E414D040002C507005245434608000200000000000000";

    // weap_starlight.esm (xEdit-made mod, Full): HEDR count 93, next 00000888; its KYWD ma_StarlightT (01 00080B)
    private const string XeditTes4 = "54455334490000000100000000000000000000003f020000484544520c008fc2753f5d00000088080000434e414d080044454641554c54004d4153540e00537461726669656c642e65736d00424e414d05004d61696e00494e4343040000000000";
    private const string XeditKywd = "4b59574431000000000000000b080001000000003f020000454449440d006d615f537461726c6967687400434e414d0400ffffff00544e414d040005000000464e414d040000000000";

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

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

    // The TES4 with its HEDR next object ID replaced
    private static byte[] WithNext(string tes4, uint next)
    {
        var bytes = Hex(tes4);
        for (int p = 24; p + 6 <= bytes.Length;)
        {
            int len = BitConverter.ToUInt16(bytes, p + 4);
            if (Encoding.ASCII.GetString(bytes, p, 4) == "HEDR") BitConverter.GetBytes(next).CopyTo(bytes, p + 14);
            p += 6 + len;
        }
        return bytes;
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

    private static StarfieldMod ReadFull(Setup s) =>
        StarfieldMod.CreateFromBinary(s.Path, StarfieldRelease.Starfield,
            new BinaryReadParameters { FileSystem = s.Fs, MasterFlagsLookup = s.Lo });

    private static IStarfieldModDisposableGetter ReadOverlay(Setup s) =>
        StarfieldMod.CreateFromBinaryOverlay(s.Path, StarfieldRelease.Starfield,
            new BinaryReadParameters { FileSystem = s.Fs, MasterFlagsLookup = s.Lo });

    // Writes with the default parameters (masters kept as listed) and returns the written TES4 and its HEDR values
    private static (byte[] Tes4, uint Count, uint Next) Write(Setup s, IStarfieldModGetter mod,
        Func<BinaryWriteParameters, BinaryWriteParameters>? adjust = null)
    {
        var outPath = Path.Combine("C:", "Out", s.Plugin.FileName);
        s.Fs.Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        var param = new BinaryWriteParameters
        {
            FileSystem = s.Fs,
            MasterFlagsLookup = s.Lo,
            MastersListContent = MastersListContentOption.NoCheck,
            MastersListOrdering = new MastersListOrderingByLoadOrder(new[] { StarfieldEsm, s.Plugin }),
        };
        if (adjust != null) param = adjust(param);
        mod.WriteToBinary(outPath, param);
        var bytes = s.Fs.File.ReadAllBytes(outPath);
        var tes4 = bytes.AsSpan(0, 24 + BitConverter.ToInt32(bytes, 4)).ToArray();
        for (int p = 24; p + 6 <= tes4.Length;)
        {
            int len = BitConverter.ToUInt16(tes4, p + 4);
            if (Encoding.ASCII.GetString(tes4, p, 4) == "HEDR")
            {
                return (tes4, BitConverter.ToUInt32(tes4, p + 10), BitConverter.ToUInt32(tes4, p + 14));
            }
            p += 6 + len;
        }
        throw new InvalidOperationException("No HEDR");
    }

    private static Setup CkPlugin() => Make("sfbgs00a_a.esm", MasterStyle.Small,
        Hex(CkTes4).Concat(Group("OMOD", Hex(CkOmod))).Concat(Group("COBJ", Hex(CkCobj))).ToArray());

    private static Setup XeditPlugin(uint? next = null) => Make("weap_starlight.esm", MasterStyle.Full,
        (next is { } n ? WithNext(XeditTes4, n) : Hex(XeditTes4)).Concat(Group("KYWD", Hex(XeditKywd), StarfieldOverride())).ToArray());

    // An override of a Starfield.esm keyword, so the master stays referenced (an unreferenced master is dropped on the
    // default write, MastersListContentOption.Iterate)
    private static byte[] StarfieldOverride()
    {
        var edid = Encoding.ASCII.GetBytes("EDID").Concat(BitConverter.GetBytes((ushort)17)).Concat(Encoding.ASCII.GetBytes("StarfieldKeyword\0")).ToArray();
        return Encoding.ASCII.GetBytes("KYWD").Concat(BitConverter.GetBytes(edid.Length)).Concat(new byte[4])
            .Concat(BitConverter.GetBytes(0x00000ABCu)).Concat(Hex("000000003F020000")).Concat(edid).ToArray();
    }

    [Fact]
    public void CkHeader_Rewrite_KeepsCountAndNext_BothReaders()
    {
        var s = CkPlugin();
        var full = ReadFull(s);
        using var overlay = ReadOverlay(s);
        foreach (var mod in new IStarfieldModGetter[] { full, overlay, (IStarfieldModGetter)overlay.DeepCopy() })
        {
            var written = Write(s, mod);
            written.Tes4.ShouldBe(Hex(CkTes4));
            written.Count.ShouldBe(12u);
            written.Next.ShouldBe(0xFF001000u);
        }
    }

    [Fact]
    public void CkHeader_RecordAdded_CountMovesNextStaysTheCks()
    {
        var s = CkPlugin();
        var mod = ReadFull(s);
        mod.Keywords.Add(new Keyword(new FormKey(s.Plugin, 0xF00), StarfieldRelease.Starfield) { EditorID = "NewKeyword" });
        var written = Write(s, mod);
        written.Count.ShouldBe(13u);
        written.Next.ShouldBe(0xFF001000u);
    }

    [Fact]
    public void XeditHeader_Rewrite_KeepsCountAndNext_BothReaders()
    {
        var s = XeditPlugin();
        var full = ReadFull(s);
        using var overlay = ReadOverlay(s);
        foreach (var mod in new IStarfieldModGetter[] { full, overlay, (IStarfieldModGetter)full.DeepCopy() })
        {
            var written = Write(s, mod);
            written.Tes4.ShouldBe(Hex(XeditTes4));
            written.Count.ShouldBe(93u);
            written.Next.ShouldBe(0x888u);
        }
    }

    [Fact]
    public void XeditHeader_OwnRecordAboveTheNext_NextIsRecomputed()
    {
        var s = XeditPlugin();
        var mod = ReadFull(s);
        mod.Keywords.Add(new Keyword(new FormKey(s.Plugin, 0x900), StarfieldRelease.Starfield) { EditorID = "NewKeyword" });
        var written = Write(s, mod);
        written.Count.ShouldBe(94u);
        written.Next.ShouldBe(0x901u);
    }

    // A stale next object ID (below the own IDs, as darkstar.esm's 02000800) is kept on a plain rewrite, and replaced
    // once the mod gets an own record above those it was read with
    [Fact]
    public void StaleNext_KeptUntilANewOwnRecordGoesAbove()
    {
        var s = XeditPlugin(next: 0x800);
        Write(s, ReadFull(s)).Next.ShouldBe(0x800u);
        var mod = ReadFull(s);
        mod.Keywords.Add(new Keyword(new FormKey(s.Plugin, 0x900), StarfieldRelease.Starfield) { EditorID = "NewKeyword" });
        Write(s, mod).Next.ShouldBe(0x901u);
    }

    [Fact]
    public void NewMod_CountIsRecordsPlusOne()
    {
        var s = XeditPlugin();
        var mod = new StarfieldMod(s.Plugin, StarfieldRelease.Starfield);
        mod.Keywords.Add(new Keyword(new FormKey(s.Plugin, 0x801), StarfieldRelease.Starfield) { EditorID = "A" });
        mod.Keywords.Add(new Keyword(new FormKey(StarfieldEsm, 0x123), StarfieldRelease.Starfield) { EditorID = "B" });
        mod.Keywords.Add(new Keyword(new FormKey(s.Plugin, 0x802), StarfieldRelease.Starfield) { EditorID = "C" });
        var written = Write(s, mod, p => p with { MastersListContent = MastersListContentOption.Iterate });
        written.Count.ShouldBe(4u);
        written.Next.ShouldBe(0x803u);
    }

    // A mod rebuilt from fresh objects (as from JSON or a database) has no read state: with the default write its header
    // is counted as a new mod's (weap_starlight 93 -> 2, kgcdoom-style FF001000 -> the highest own ID + 1). Given back
    // the baseline from RoundTripMixIn, it writes the TES4 as read, and moves the count by the records added or removed.
    private static StarfieldMod Rebuild(StarfieldMod read)
    {
        var mod = new StarfieldMod(read.ModKey, StarfieldRelease.Starfield);
        mod.ModHeader.DeepCopyIn(read.ModHeader);
        foreach (var r in read.Keywords) mod.Keywords.Add(r.DeepCopy());
        foreach (var r in read.ObjectModifications) mod.ObjectModifications.Add(r.DeepCopy());
        foreach (var r in read.ConstructibleObjects) mod.ConstructibleObjects.Add(r.DeepCopy());
        mod.SetOwnFormIdStyle(read.GetOwnFormIdStyle());
        return mod;
    }

    private static (byte[] Tes4, uint Count, uint Next) WriteDefault(Setup s, IStarfieldModGetter mod) =>
        Write(s, mod, p => new BinaryWriteParameters { FileSystem = s.Fs, MasterFlagsLookup = s.Lo });

    [Theory]
    [InlineData("ck")]       // kgcdoom-style: records + 1, FF001000
    [InlineData("xedit")]    // weap_starlight: records + groups, highest own ID + 1
    [InlineData("stale")]    // darkstar-style: records + groups, a next object ID below the own IDs
    public void RebuiltMod_WithHeaderStatsBaseline_WritesTheTes4AsRead(string kind)
    {
        var s = kind == "ck" ? CkPlugin() : XeditPlugin(next: kind == "stale" ? 0x800 : null);
        var original = s.Fs.File.ReadAllBytes(s.Path);
        var tes4 = original.AsSpan(0, 24 + BitConverter.ToInt32(original, 4)).ToArray();
        var read = ReadFull(s);
        var baseline = read.GetHeaderStatsBaseline();
        baseline.ShouldNotBeNull();
        baseline.RecordsRead.ShouldBe(2u);
        baseline.HighestOwnObjectId.ShouldBe(kind == "ck" ? 0xAu : 0x80Bu);
        baseline.NextObjectId.ShouldBe(kind == "ck" ? 0xFF001000u : kind == "stale" ? 0x800u : 0x888u);
        using (var overlay = ReadOverlay(s))
        {
            overlay.GetHeaderStatsBaseline().ShouldBe(baseline);
        }

        // Without the baseline the rebuilt mod is counted as new
        WriteDefault(s, Rebuild(read)).Tes4.ShouldNotBe(tes4);

        var rebuilt = Rebuild(read);
        rebuilt.SetHeaderStatsBaseline(baseline);
        rebuilt.GetHeaderStatsBaseline().ShouldBe(baseline);
        WriteDefault(s, rebuilt).Tes4.ShouldBe(tes4);

        var stored = WriteDefault(s, read).Count;
        // One record added: the count moves by one, the next object ID stays (the new ID is below the highest read)
        var added = Rebuild(read);
        added.SetHeaderStatsBaseline(baseline);
        added.Keywords.Add(new Keyword(new FormKey(s.Plugin, kind == "ck" ? 0x005u : 0x801u), StarfieldRelease.Starfield) { EditorID = "Added" });
        var a = WriteDefault(s, added);
        a.Count.ShouldBe(stored + 1);
        a.Next.ShouldBe(baseline.NextObjectId);
        // One record removed: the count moves back by one
        var removed = Rebuild(read);
        removed.SetHeaderStatsBaseline(baseline);
        if (kind == "ck") removed.ConstructibleObjects.Clear(); else removed.Keywords.Remove(removed.Keywords.Single(k => k.FormKey.ModKey == s.Plugin));
        WriteDefault(s, removed).Count.ShouldBe(stored - 1);
    }

    // A master no record references any more stays in a read (or rebuilt, with a baseline) plugin's list: the CK and
    // xEdit leave it in, and dropping it moves every master index after it. weap_starlight's real TES4 lists
    // Starfield.esm; here its only record is its own keyword.
    [Fact]
    public void UnreferencedMaster_IsKept_ReadOrRebuilt()
    {
        var s = Make("weap_starlight.esm", MasterStyle.Full, Hex(XeditTes4).Concat(Group("KYWD", Hex(XeditKywd))).ToArray());
        var full = ReadFull(s);
        using var overlay = ReadOverlay(s);
        foreach (var mod in new IStarfieldModGetter[] { full, overlay, (IStarfieldModGetter)full.DeepCopy(), (IStarfieldModGetter)overlay.DeepCopy() })
        {
            WriteDefault(s, mod).Tes4.ShouldBe(Hex(XeditTes4));
        }
        var rebuilt = Rebuild(full);
        rebuilt.SetHeaderStatsBaseline(full.GetHeaderStatsBaseline());
        WriteDefault(s, rebuilt).Tes4.ShouldBe(Hex(XeditTes4));
        // A new mod, and an explicit Iterate, list only the masters the records reference (none here)
        var iterated = Write(s, ReadFull(s), p => new BinaryWriteParameters
        {
            FileSystem = s.Fs, MasterFlagsLookup = s.Lo, MastersListContent = MastersListContentOption.Iterate
        });
        Encoding.ASCII.GetString(iterated.Tes4).ShouldNotContain("MAST");
        Encoding.ASCII.GetString(WriteDefault(s, Rebuild(full)).Tes4).ShouldNotContain("MAST");
    }

    [Fact]
    public void ExplicitOptions_WorkAsBefore()
    {
        var s = CkPlugin();
        var mod = ReadFull(s);
        var iterate = Write(s, mod, p => p with { RecordCount = RecordCountOption.Iterate, NextFormID = NextFormIDOption.Iterate });
        iterate.Count.ShouldBe(4u); // 2 records + 2 groups (Mutagen's count)
        iterate.Next.ShouldBe(0xBu);
        var noCheck = Write(s, ReadFull(s), p => p with { RecordCount = RecordCountOption.NoCheck, NextFormID = NextFormIDOption.NoCheck });
        noCheck.Count.ShouldBe(12u);
        noCheck.Next.ShouldBe(0xFF001000u);
    }
}
