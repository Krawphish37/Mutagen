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

// C164: the Creation Kit's export of a small .esm stores its HEDR next object ID in full-index form, 03000800, next to
// its own keyword 03000809 (test_ck.esm). SF1Edit read the next ID as 0x800 and added FE002800 (test_xedit.esm, next
// 00000801). Mutagen's allocator counted up from the stored value, so the 10th new record of test_ck.esm (the 9th of
// test_xedit.esm) got 0x809 and, added under that FormKey, replaced the plugin's own keyword. New IDs now start above the
// highest own ID the mod has, whatever way its records got there (walked once at the first allocation, nested records
// included; own records added to a top-level group afterwards are noted), and stay within the mod's size: a Small
// master's new IDs end at 0xFFF and a Medium master's at 0xFFFF (a stored FF001000, the CK's value, no longer gives a
// Small plugin 0x1000), with a clear error when the range is used up. A mod with nothing added is written back as read.
// The files' TES4 and KYWD group below (their 4.5 MB NAVI override left out).
public class NextIdAfterCkExportTests
{
    private const string CkTes4 = "54455334C700000001010000000000000000000046020000484544520C008FC2753F0400000000080003434E414D080044454641554C54004D4153540E00537461726669656C642E65736D004D4153540D0053464247533030442E65736D004D4153540D006461726B737461722E65736D004D4153540C006275626C756D692E65736D004D4153541A006461726B7374617270617463685F6275626C756D692E65736D004D4153540D0053464247533030332E65736D004D4153540D0053464247533030362E65736D00424E414D05004D61696E00494E4343040000000000";
    private const string CkKywd = "47525550640000004B595744000000006D350000000000004B595744340000000000000009080003000000004602000045444944100078525F486F6C737465725F5465737400434E414D0400FFFFFF00544E414D040015000000464E414D040000000000";
    private const string XeditTes4 = "54455334C700000001010000000000000000000046020000484544520C008FC2753F0500000001080000434E414D080044454641554C54004D4153540E00537461726669656C642E65736D004D4153540D0053464247533030442E65736D004D4153540D006461726B737461722E65736D004D4153540C006275626C756D692E65736D004D4153541A006461726B7374617270617463685F6275626C756D692E65736D004D4153540D0053464247533030332E65736D004D4153540D0053464247533030362E65736D00424E414D05004D61696E00494E4343040000000000";
    private const string XeditKywd = "47525550B10000004B595744000000006D350000000000004B5957443500000000000000002800FE000000004602000045444944110078525F486F6C737465725F546573743200434E414D0400FFFFFF00544E414D040015000000464E414D0400000000004B595744340000000000000009080003000000004602000045444944100078525F486F6C737465725F5465737400434E414D0400FFFFFF00544E414D040015000000464E414D040000000000";

    private static readonly LoadOrder<IModMasterStyledGetter> Masters = new(new IModMasterStyledGetter[]
    {
        new KeyedMasterStyle(ModKey.FromFileName("Starfield.esm"), MasterStyle.Full),
        new KeyedMasterStyle(ModKey.FromFileName("SFBGS00D.esm"), MasterStyle.Full),
        new KeyedMasterStyle(ModKey.FromFileName("darkstar.esm"), MasterStyle.Full),
        new KeyedMasterStyle(ModKey.FromFileName("bublumi.esm"), MasterStyle.Small),
        new KeyedMasterStyle(ModKey.FromFileName("darkstarpatch_bublumi.esm"), MasterStyle.Small),
        new KeyedMasterStyle(ModKey.FromFileName("SFBGS003.esm"), MasterStyle.Medium),
        new KeyedMasterStyle(ModKey.FromFileName("SFBGS006.esm"), MasterStyle.Medium),
    });

    private static (MockFileSystem Fs, string Path, byte[] Bytes) Make(string fileName, string tes4, string kywd)
    {
        var fs = new MockFileSystem();
        var path = Path.Combine("C:", "Data", fileName);
        fs.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = Convert.FromHexString(tes4).Concat(Convert.FromHexString(kywd)).ToArray();
        fs.File.WriteAllBytes(path, bytes);
        return (fs, path, bytes);
    }

    private static StarfieldMod Read(MockFileSystem fs, string path) =>
        StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield,
            new BinaryReadParameters { FileSystem = fs, MasterFlagsLookup = Masters });

    // Writes with the default parameters; returns the file and its HEDR next object ID
    private static (byte[] Bytes, uint Next) Write(MockFileSystem fs, StarfieldMod mod)
    {
        var outPath = Path.Combine("C:", "Out", mod.ModKey.FileName);
        fs.Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        mod.WriteToBinary(outPath, new BinaryWriteParameters { FileSystem = fs, MasterFlagsLookup = Masters });
        var bytes = fs.File.ReadAllBytes(outPath);
        for (int p = 24; p + 6 <= 24 + BitConverter.ToInt32(bytes, 4);)
        {
            int len = BitConverter.ToUInt16(bytes, p + 4);
            if (Encoding.ASCII.GetString(bytes, p, 4) == "HEDR") return (bytes, BitConverter.ToUInt32(bytes, p + 14));
            p += 6 + len;
        }
        throw new InvalidOperationException("No HEDR");
    }

    [Fact]
    public void CkExport_NothingAdded_WrittenAsRead()
    {
        var (fs, path, bytes) = Make("test_ck.esm", CkTes4, CkKywd);
        var mod = Read(fs, path);
        ((IModGetter)mod).NextFormID.ShouldBe(0x03000800u);
        var written = Write(fs, mod);
        written.Next.ShouldBe(0x03000800u);
        written.Bytes.ShouldBe(bytes);
    }

    [Fact]
    public void CkExport_NewIds_StartAboveTheOwnRecord()
    {
        var (fs, path, _) = Make("test_ck.esm", CkTes4, CkKywd);
        var mod = Read(fs, path);
        var own = new FormKey(mod.ModKey, 0x809);
        var ids = Enumerable.Range(0, 11).Select(i => mod.Keywords.AddNew($"C164_New{i}").FormKey.ID).ToList();
        ids.ShouldBe(Enumerable.Range(0x80A, 11).Select(i => (uint)i).ToList());
        mod.Keywords.Count.ShouldBe(12);
        mod.Keywords[own].EditorID.ShouldBe("xR_Holster_Test");
        var written = Write(fs, mod);
        written.Next.ShouldBe(0x03000815u);
        var back = Read(fs, Path.Combine("C:", "Out", mod.ModKey.FileName));
        back.Keywords.Count.ShouldBe(12);
        back.Keywords[own].EditorID.ShouldBe("xR_Holster_Test");
    }

    [Fact]
    public void XeditAddition_NothingAdded_WrittenAsRead()
    {
        var (fs, path, bytes) = Make("test_xedit.esm", XeditTes4, XeditKywd);
        var mod = Read(fs, path);
        ((IModGetter)mod).NextFormID.ShouldBe(0x801u);
        var written = Write(fs, mod);
        written.Next.ShouldBe(0x801u);
        written.Bytes.ShouldBe(bytes);
    }

    [Fact]
    public void XeditAddition_NewIds_StartAboveBothOwnRecords()
    {
        var (fs, path, _) = Make("test_xedit.esm", XeditTes4, XeditKywd);
        var mod = Read(fs, path);
        var ids = Enumerable.Range(0, 10).Select(i => mod.Keywords.AddNew($"C164_New{i}").FormKey.ID).ToList();
        ids.ShouldBe(Enumerable.Range(0x80A, 10).Select(i => (uint)i).ToList());
        mod.Keywords.Count.ShouldBe(12);
        mod.Keywords[new FormKey(mod.ModKey, 0x800)].EditorID.ShouldBe("xR_Holster_Test2");
        mod.Keywords[new FormKey(mod.ModKey, 0x809)].EditorID.ShouldBe("xR_Holster_Test");
        Write(fs, mod).Next.ShouldBe(0x814u);
    }

    [Fact]
    public void NewMod_CountsUpFromItsNextId()
    {
        var mod = new StarfieldMod(ModKey.FromFileName("New.esm"), StarfieldRelease.Starfield);
        var first = mod.Keywords.AddNew().FormKey.ID;
        mod.Keywords.AddNew().FormKey.ID.ShouldBe(first + 1);
    }

    private static StarfieldMod NewMod(string name = "New.esm") => new(ModKey.FromFileName(name), StarfieldRelease.Starfield);

    [Fact]
    public void ExplicitFormKey_BeforeTheFirstAllocation_IsSkipped()
    {
        var mod = NewMod();
        var next = mod.GetNextFormKey().ID; // the mod's first ID, before anything is added
        var mod2 = NewMod();
        mod2.Keywords.Add(new Keyword(new FormKey(mod2.ModKey, next + 5), StarfieldRelease.Starfield));
        mod2.Keywords.AddNew().FormKey.ID.ShouldBe(next + 6);
    }

    [Fact]
    public void ExplicitFormKey_AfterAnAllocation_IsSkipped()
    {
        var mod = NewMod();
        var first = mod.Keywords.AddNew().FormKey.ID;
        mod.Keywords.Add(new Keyword(new FormKey(mod.ModKey, first + 10), StarfieldRelease.Starfield));
        mod.Weapons.Set(new Weapon(new FormKey(mod.ModKey, first + 20), StarfieldRelease.Starfield));
        mod.Keywords.AddNew().FormKey.ID.ShouldBe(first + 21);
    }

    [Fact]
    public void NestedOwnRecord_IsSeenByTheFirstAllocation()
    {
        var mod = NewMod();
        var quest = new Quest(new FormKey(mod.ModKey, 0x800), StarfieldRelease.Starfield);
        quest.DialogTopics.Add(new DialogTopic(new FormKey(mod.ModKey, 0xA00), StarfieldRelease.Starfield));
        mod.Quests.Add(quest);
        mod.Keywords.AddNew().FormKey.ID.ShouldBe(0xA01u);
    }

    [Fact]
    public void Light_StoredFF001000_StaysInRange()
    {
        var mod = NewMod("Light.esm");
        mod.IsSmallMaster = true;
        mod.Keywords.Add(new Keyword(new FormKey(mod.ModKey, 0x805), StarfieldRelease.Starfield));
        ((IMod)mod).NextFormID = 0xFF001000;
        mod.Keywords.AddNew().FormKey.ID.ShouldBe(0x806u);
        mod.Keywords.AddNew().FormKey.ID.ShouldBe(0x807u);
    }

    [Fact]
    public void Light_NoOwnRecords_StartsAt800()
    {
        var mod = NewMod("Light.esm");
        mod.IsSmallMaster = true;
        ((IMod)mod).NextFormID = 0xFF001000;
        mod.Keywords.AddNew().FormKey.ID.ShouldBe(0x800u);
    }

    [Fact]
    public void Light_RangeUsedUp_Refuses()
    {
        var mod = NewMod("Light.esm");
        mod.IsSmallMaster = true;
        mod.Keywords.Add(new Keyword(new FormKey(mod.ModKey, 0xFFF), StarfieldRelease.Starfield));
        var ex = Should.Throw<InvalidOperationException>(() => mod.Keywords.AddNew());
        ex.Message.ShouldContain("0xFFF");
        mod.Keywords.Count.ShouldBe(1);
    }

    [Fact]
    public void Medium_StaysInRange_AndRefusesWhenFull()
    {
        var mod = NewMod("Medium.esm");
        mod.IsMediumMaster = true;
        mod.Keywords.Add(new Keyword(new FormKey(mod.ModKey, 0x1234), StarfieldRelease.Starfield));
        ((IMod)mod).NextFormID = 0xFF010000;
        mod.Keywords.AddNew().FormKey.ID.ShouldBe(0x1235u);
        mod.Keywords.Add(new Keyword(new FormKey(mod.ModKey, 0xFFFF), StarfieldRelease.Starfield));
        Should.Throw<InvalidOperationException>(() => mod.Keywords.AddNew());
    }
}
