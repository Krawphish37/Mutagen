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

// C64 (decision 6.1 revised): a condition's FormIDs whose master index points past the master list are written back as
// stored, with both readers, like any other link of a record read from a plugin (C189). MissionsGalore.esm is the real
// case: 116 QUST conditions whose parameter 1 is one of its own GLOBs stored as 03xxxxxx with 2 masters (census of the
// 370 plugins in Data, C64). Here a Full plugin with one master (own index 01) stores 02000801 in each of a CTDA's FormID
// places, and parameter 3 also holds values that aren't links (the engine's 0 and -1, and any value when RunOnType isn't
// LinkedReference): those stay numbers and are written back as stored too.
public class ConditionPastListFormIdTests
{
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");
    private static readonly ModKey Plugin = ModKey.FromFileName("PastListConditions.esm");
    private const uint PastList = 0x02000801;

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

    // GetIsID (72): parameter 1 is a link; then RunOnType (offset 20), Reference (24), parameter 3 (28)
    private static byte[] Ctda(uint parameter1, int runOn, uint reference, uint parameter3)
    {
        var c = new byte[32];
        BitConverter.GetBytes(1f).CopyTo(c, 4);
        BitConverter.GetBytes((ushort)72).CopyTo(c, 8);
        BitConverter.GetBytes(parameter1).CopyTo(c, 12);
        BitConverter.GetBytes(runOn).CopyTo(c, 20);
        BitConverter.GetBytes(reference).CopyTo(c, 24);
        BitConverter.GetBytes(parameter3).CopyTo(c, 28);
        return Sub("CTDA", c);
    }

    private const int Subject = 0, Reference = 2, LinkedReference = 4;

    private static readonly byte[][] Conditions =
    [
        Ctda(PastList, Subject, 0, 0xFFFFFFFF),                        // parameter 1 past the list (MissionsGalore's case)
        Ctda(0x01000801, Reference, PastList, 0xFFFFFFFF),             // the reference past the list
        Ctda(0x01000801, LinkedReference, 0x01000801, PastList),       // the linked reference's keyword past the list
        Ctda(0x01000801, LinkedReference, 0x01000801, 0xFFFFFFFE),     // a keyword link with the master index FF
        Ctda(0x01000801, LinkedReference, 0x01000801, 0xFFFFFFFF),     // -1: no keyword (not a link)
        Ctda(0x01000801, LinkedReference, 0x01000801, 0),              // 0: no keyword (not a link)
        Ctda(0x01000801, Subject, 0, PastList),                        // not LinkedReference: a number, not a link
    ];

    private static readonly byte[] File = Record("TES4", 0,
            Sub("HEDR", Convert.FromHexString("8FC2753F0300000003080000"))
                .Concat(Sub("MAST", Encoding.ASCII.GetBytes("Starfield.esm\0"))).ToArray())
        .Concat(Group("GLOB",
            Record("GLOB", 0x01000801, Sub("EDID", Encoding.ASCII.GetBytes("OwnGlobal\0"))
                .Concat(Sub("FNAM", [(byte)'f'])).Concat(Sub("FLTV", BitConverter.GetBytes(1f))).ToArray())))
        .Concat(Group("CNDF",
            Record("CNDF", 0x01000802, Sub("EDID", Encoding.ASCII.GetBytes("PastListConditions\0"))
                .Concat(Conditions.SelectMany(c => c)).ToArray())))
        .ToArray();

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
            new KeyedMasterStyle(Plugin, MasterStyle.Full),
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

    private static void ReadsAsTheGameDoes(IConditionRecordGetter record)
    {
        var c = record.Conditions;
        c.Count.ShouldBe(Conditions.Length);
        var own = new FormKey(Plugin, 0x801);
        ((IGetIsIDConditionDataGetter)c[0].Data).FirstParameter.Link.FormKey.ShouldBe(own);
        c[1].Data.Reference.FormKey.ShouldBe(own);
        c[2].Data.LinkedReferenceKeyword.FormKey.ShouldBe(own);
        c[3].Data.LinkedReferenceKeyword.FormKey.ShouldBe(new FormKey(Plugin, 0xFFFFFE));
        c[4].Data.LinkedReferenceKeyword.IsNull.ShouldBeTrue();
        c[4].Data.ParameterThree.ShouldBe(-1);
        c[5].Data.LinkedReferenceKeyword.IsNull.ShouldBeTrue();
        c[5].Data.ParameterThree.ShouldBe(0);
        c[6].Data.LinkedReferenceKeyword.IsNull.ShouldBeTrue();
        c[6].Data.ParameterThree.ShouldBe(unchecked((int)PastList));
    }

    [Fact]
    public void ConditionFormIdsPastTheList_RoundTripByteForByte_BothReaders()
    {
        var s = Make();
        var full = StarfieldMod.CreateFromBinary(s.Path, StarfieldRelease.Starfield, ReadParams(s));
        ReadsAsTheGameDoes(full.ConditionRecords.Single());
        Write(s, full).ShouldBe(File);
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(s.Path, StarfieldRelease.Starfield, ReadParams(s));
        ReadsAsTheGameDoes(overlay.ConditionRecords.Single());
        Write(s, overlay).ShouldBe(File);
    }

    // A changed link gets the plugin's own index (01); the other stored values stay
    [Fact]
    public void ChangedConditionLink_GetsTheOwnIndex()
    {
        var s = Make();
        var mod = StarfieldMod.CreateFromBinary(s.Path, StarfieldRelease.Starfield, ReadParams(s));
        var conditions = mod.ConditionRecords.Single().Conditions;
        ((GetIsIDConditionData)conditions[0].Data).FirstParameter.Link.SetTo(new FormKey(Plugin, 0x802));
        var written = Write(s, mod);
        var at = written.AsSpan().IndexOf("CTDA"u8);
        BitConverter.ToUInt32(written, at + 6 + 12).ShouldBe(0x01000802u);
        // the rest of the file is as stored
        var expected = (byte[])File.Clone();
        BitConverter.GetBytes(0x01000802u).CopyTo(expected, at + 6 + 12);
        written.ShouldBe(expected);
    }
}
