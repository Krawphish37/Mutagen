using System.IO.Abstractions.TestingHelpers;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Masters;

// Starfield's official Small and Medium plugins store their own FormIDs in their own style: FE (Small) or FD (Medium),
// never as a full master index. That holds for every record header of sfbgs00a_a/d/e/f/g/i, sfbgs00b, sfbgs019/021/023
// and sfta01-06, not only for the plugins Mutagen already listed (OldMars, Constellation, SFBGS003/004/006/007/008/
// 00D/047/050). Mutagen read them fine but wrote their own FormIDs as full indices (01xxxxxx): M3 changed every
// self-referencing subrecord in those 16 plugins (e.g. sfta06: 48,706 of 56,399 records).
// Real bytes from sfbgs00a_a.esm (Small, one master Starfield.esm): its TES4 header, COBJ 00000A (CNAM points at the
// plugin's own OMOD FE000002) and that OMOD. Both readers, and the written records are byte for byte the originals.
public class OfficialPluginOwnStyleTests
{
    private static readonly ModKey Plugin = ModKey.FromFileName("sfbgs00a_a.esm");
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");

    private const string Tes4 = "54455334630000008101000000000000F93020003F020000484544520C008FC2753F0C000000001000FF434E414D0700507974686F6E00534E414D0700536372697074004D4153540E00737461726669656C642E65736D00424E414D09004465765F4D61696E00494E43430400000000004348474C04003C2C3E00";

    // co_mod_AVM_Weapon_Skin_cSFBGS00A_A_Blackout_Drumbeat (FE00000A:sfbgs00a_a.esm)
    private const string Cobj = "434F424A9B000000000000000A0000FEF93020003F020200454449443500636F5F6D6F645F41564D5F576561706F6E5F536B696E5F6353464247533030415F415F426C61636B6F75745F4472756D626561740044455343040000000000424E414D0400C0E12C00434E414D0400020000FE4E4E414D02000000534E414D040000000000544E414D0100004C524E4D01000344415441040000000000464E414D040002C507005245434608000200000000000000";

    // mod_AVM_DrumBeat_cSFBGS00A_A_Blackout (FE000002:sfbgs00a_a.esm)
    private const string Omod = "4F4D4F44C100000000000000020000FEF93020003F0207004544494426006D6F645F41564D5F4472756D426561745F6353464247533030415F415F426C61636B6F75740046554C4C04000300000044455343040000000000464C4C44040003000000444154416700000000000200000000001B0000005445534F626A656374574541505F496E7374616E6365446174610000006C071F0000000000000000000400000002000000574B4559010000FE00000000000000000400000002000000574B4559FA4E370000000000000000004D4E414D0400FCBA0500";

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    private static byte[] Group(string label, byte[] record)
    {
        var s = new MemoryStream();
        s.Write("GRUP"u8);
        s.Write(BitConverter.GetBytes(24 + record.Length));
        s.Write(System.Text.Encoding.ASCII.GetBytes(label));
        s.Write(BitConverter.GetBytes(0)); // top group
        s.Write(new byte[8]);
        s.Write(record);
        return s.ToArray();
    }

    private static (MockFileSystem Fs, string Path, LoadOrder<IModMasterStyledGetter> Lo) Setup()
    {
        var fs = new MockFileSystem();
        var dir = System.IO.Path.Combine("C:", "Data");
        fs.Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, Plugin.FileName);
        var file = Hex(Tes4).Concat(Group("OMOD", Hex(Omod))).Concat(Group("COBJ", Hex(Cobj))).ToArray();
        fs.File.WriteAllBytes(path, file);
        var lo = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
            new KeyedMasterStyle(Plugin, MasterStyle.Small),
        });
        return (fs, path, lo);
    }

    private static IEnumerable<IStarfieldModGetter> ReadBoth(MockFileSystem fs, string path, LoadOrder<IModMasterStyledGetter> lo)
    {
        var param = new BinaryReadParameters { FileSystem = fs, MasterFlagsLookup = lo };
        yield return StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, param);
        yield return StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, param);
    }

    // The bytes of the first record of a type in a written plugin
    private static byte[] RecordBytes(byte[] file, string type)
    {
        int p = 24 + BitConverter.ToInt32(file, 4);
        while (p < file.Length)
        {
            var sig = System.Text.Encoding.ASCII.GetString(file, p, 4);
            int size = BitConverter.ToInt32(file, p + 4);
            if (sig == "GRUP")
            {
                if (System.Text.Encoding.ASCII.GetString(file, p + 8, 4) == type)
                {
                    int rs = BitConverter.ToInt32(file, p + 24 + 4);
                    return file.AsSpan(p + 24, 24 + rs).ToArray();
                }
                p += size;
            }
            else p += 24 + size;
        }
        throw new InvalidOperationException(type + " not written");
    }

    [Fact]
    public void SmallOfficialPlugin_WritesItsOwnFormIdsAsSmall()
    {
        var (fs, path, lo) = Setup();
        foreach (var mod in ReadBoth(fs, path, lo))
        {
            var cobj = mod.ConstructibleObjects.ShouldHaveSingleItem();
            cobj.FormKey.ShouldBe(new FormKey(Plugin, 0x00A));
            cobj.CreatedObject.FormKey.ShouldBe(new FormKey(Plugin, 0x002));
            mod.ObjectModifications.ShouldHaveSingleItem().FormKey.ShouldBe(new FormKey(Plugin, 0x002));

            var outPath = System.IO.Path.Combine("C:", "Out", Plugin.FileName);
            fs.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath)!);
            mod.WriteToBinary(outPath, new BinaryWriteParameters
            {
                FileSystem = fs,
                MasterFlagsLookup = lo,
                MastersListContent = MastersListContentOption.NoCheck,
            });
            var written = fs.File.ReadAllBytes(outPath);
            RecordBytes(written, "COBJ").ShouldBe(Hex(Cobj));
            RecordBytes(written, "OMOD").ShouldBe(Hex(Omod));
        }
    }
}
