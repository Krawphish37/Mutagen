using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Masters;

// C203: StoredFormIdMixIn.GetStoredFormIds lists where a record's FormIDs sit and how each is stored, from the record
// definitions (the writer notes where it puts each FormID), for both readers.
public class StoredFormIdTests
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

    // One master (Starfield.esm, Full): a plugin of the given size whose FLST (header given) links to its own 000801
    // stored as given, and to Starfield.esm's 000ABC
    private static (MockFileSystem Fs, string Path, ModKey Plugin, LoadOrder<IModMasterStyledGetter> Lo) Make(
        MasterStyle size, uint headerFlags, uint listHeader, uint ownLink)
    {
        var plugin = ModKey.FromFileName("Stored.esm");
        var file = Record("TES4", headerFlags, 0,
                Sub("HEDR", Convert.FromHexString("8FC2753F0300000004080000"))
                    .Concat(Sub("MAST", Encoding.ASCII.GetBytes("Starfield.esm\0"))).ToArray())
            .Concat(Group("FLST",
                Record("FLST", 0, listHeader, Sub("EDID", Encoding.ASCII.GetBytes("List\0"))
                    .Concat(Sub("LNAM", BitConverter.GetBytes(ownLink)))
                    .Concat(Sub("LNAM", BitConverter.GetBytes(0x00000ABCu))).ToArray())))
            .ToArray();
        var fs = new MockFileSystem();
        var path = System.IO.Path.Combine("C:", "Data", plugin.FileName);
        fs.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        fs.File.WriteAllBytes(path, file);
        var lo = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
            new KeyedMasterStyle(plugin, size),
        });
        return (fs, path, plugin, lo);
    }

    private static void Check(MasterStyle size, uint headerFlags, uint listHeader, StoredFormIdForm headerForm,
        uint ownLink, StoredFormIdForm ownLinkForm)
    {
        var (fs, path, plugin, lo) = Make(size, headerFlags, listHeader, ownLink);
        var param = new BinaryReadParameters { FileSystem = fs, MasterFlagsLookup = lo };
        var full = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, param);
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, param);
        foreach (var mod in new IStarfieldModGetter[] { full, overlay })
        {
            var ids = mod.FormLists.Single().GetStoredFormIds(mod, lo);
            ids.Count.ShouldBe(3, string.Join(" | ", ids));
            ids[0].ShouldBe(new StoredFormIdPosition(null, 0, 0, listHeader, headerForm, true, new FormKey(plugin, 0x803),
                StoredFormIdLocation.Header));
            ids[1].ShouldBe(new StoredFormIdPosition(new RecordType("LNAM"), 0, 0, ownLink, ownLinkForm, true,
                new FormKey(plugin, 0x801), StoredFormIdLocation.Written));
            ids[2].ShouldBe(new StoredFormIdPosition(new RecordType("LNAM"), 1, 0, 0x00000ABC, StoredFormIdForm.Full, false,
                new FormKey(StarfieldEsm, 0xABC), StoredFormIdLocation.Written));
        }
    }

    [Fact]
    public void SmallPlugin_FeHeader_FullIndexLink() =>
        Check(MasterStyle.Small, 0x101, 0xFE000803, StoredFormIdForm.Small, 0x01000801, StoredFormIdForm.Full);

    [Fact]
    public void MediumPlugin_FullIndexHeader_FdLink() =>
        Check(MasterStyle.Medium, 0x401, 0x01000803, StoredFormIdForm.Full, 0xFD000801, StoredFormIdForm.Medium);

    [Fact]
    public void FullPlugin_LinkPastTheList() =>
        Check(MasterStyle.Full, 0x1, 0x01000803, StoredFormIdForm.Full, 0x02000801, StoredFormIdForm.PastTheList);

    [Fact]
    public void SmallPlugin_FdFormIsPastTheList()
    {
        // A Small plugin has no FD form of its own: FD + its medium index (0) reads as itself, past the list
        Check(MasterStyle.Small, 0x101, 0xFE000803, StoredFormIdForm.Small, 0xFD000801, StoredFormIdForm.PastTheList);
    }
}
