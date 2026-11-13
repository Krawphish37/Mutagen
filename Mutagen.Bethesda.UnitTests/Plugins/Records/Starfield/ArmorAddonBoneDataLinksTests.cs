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

// ArmorAddon.BoneDataModifiers (BSMP, then BNAM bone modifiers, per sex) is a gendered list of FormLinks. The
// generator enumerated and remapped gendered loqui items and gendered single links, but skipped a gendered list, so
// these links were missing from EnumerateFormLinks and RemapLinks: not remapped by duplicate-as-new, compaction or
// master changes. kgcDoom_AA_HexapodA_BrainSquid_Demon (00B9A5:kgcdoom.esm) holds 3 of them, read here from a plugin
// made of kgcdoom.esm's real header and the record, with both readers.
public class ArmorAddonBoneDataLinksTests
{
    private const string KgcdoomTes4 = "54455334CD00000081040000000000004A34200046020000484544520C008FC2753FE13B0000001000FF434E414D0700507974686F6E00534E414D0700536372697074004D4153540E00737461726669656C642E65736D00544E414D64008800000080B900FD8EB900FD85B900FD83B900FD89B900FD7AB900FD90B900FD8FB900FD8DB900FD8CB900FD8AB900FD88B900FD86B900FD82B900FD81B900FD7FB900FD7CB900FD7BB900FD91B900FD7DB900FD84B900FD87B900FD8BB900FD7EB900FD424E414D09004465765F4D61696E00494E43430400070000004348474C0400494A4200";
    private const string BrainSquidArma = "41524D416F01000000000000A5B900FD4A342000460202004544494425006B6763446F6F6D5F41415F48657861706F64415F427261696E53717569645F44656D6F6E004246434219004247534B6579776F7264466F726D5F436F6D706F6E656E7400424643450000424F363408000800000000000000524E414D0400AACA2C00444E414D0D00000000000000000000000000004D4F443241004163746F72735C48657861706F64415C4D6573685C48657861706F64415F427261696E53717569645C48657861706F64415F427261696E53717569642E6E6966004D4C4D310600010070BA00FD464C4C44040001000000464C4C44040001000000464C4C44040001000000464C4C44040001000000464C4C44040001000000464C4C44040001000000534E44440400C27420004F4E414D040066F328004D4E414D040000000000544E414D010000534E414D010000564E414D01000042534D50040000000000424E414D040077573100424E414D040079573100424E414D04007857310042534D50040001000000";

    private static readonly ModKey Kgcdoom = ModKey.FromFileName("kgcdoom.esm");

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    private static readonly LoadOrder<IModMasterStyledGetter> Lo = new(new IModMasterStyledGetter[]
    {
        new KeyedMasterStyle(ModKey.FromFileName("Starfield.esm"), MasterStyle.Full),
        new KeyedMasterStyle(Kgcdoom, MasterStyle.Medium),
    });

    private static byte[] Group(string label, byte[] records)
    {
        var s = new MemoryStream();
        s.Write(Encoding.ASCII.GetBytes("GRUP"));
        s.Write(BitConverter.GetBytes(24 + records.Length));
        s.Write(Encoding.ASCII.GetBytes(label));
        s.Write(BitConverter.GetBytes(0));
        s.Write(new byte[8]);
        s.Write(records);
        return s.ToArray();
    }

    private static IEnumerable<IArmorAddonGetter> ReadBoth()
    {
        var fs = new MockFileSystem();
        var path = Path.Combine("C:", "Data", Kgcdoom.FileName);
        fs.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        fs.File.WriteAllBytes(path, Hex(KgcdoomTes4).Concat(Group("ARMA", Hex(BrainSquidArma))).ToArray());
        var param = new BinaryReadParameters { FileSystem = fs, MasterFlagsLookup = Lo };
        yield return StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, param).ArmorAddons.Single();
        var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, param);
        yield return overlay.ArmorAddons.Single();
    }

    [Fact]
    public void BoneDataModifiers_AreEnumeratedAndRemapped()
    {
        foreach (var arma in ReadBoth())
        {
            var bones = arma.BoneDataModifiers.SelectMany(l => l).Select(l => l.FormKey).ToArray();
            bones.Length.ShouldBe(3);
            var links = arma.EnumerateFormLinks().Select(l => l.FormKey).ToList();
            foreach (var bone in bones)
            {
                links.ShouldContain(bone);
            }

            var copy = arma.DeepCopy();
            var target = FormKey.Factory("ABCDEF:Remapped.esp");
            copy.RemapLinks(new Dictionary<FormKey, FormKey> { [bones[0]] = target });
            copy.BoneDataModifiers.SelectMany(l => l).Select(l => l.FormKey).ShouldContain(target);
            copy.BoneDataModifiers.SelectMany(l => l).Select(l => l.FormKey).ShouldNotContain(bones[0]);
        }
    }
}
