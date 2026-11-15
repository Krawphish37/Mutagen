using System.Text;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C38: Package's DataInputNames step (the input names and flags that follow XNAM when the procedure tree is empty)
// reads UNAM, BNAM and PNAM, and now lists all three (it listed only UNAM). Generated: BNAM and PNAM join UNAM in the
// step's case of both readers; the trigger list already held them.
// No PACK in the 370 plugins in Data has names without a tree (5,499 PACK: 5,262 with no tree have no names, 237 with
// a tree hold 4,719 entries, every one UNAM BNAM PNAM), so these records are made from PilotDockingPackageIgnoreCombat
// (07D422:Starfield.esm) with its two branches taken out.
public class PackageDataInputNamesTests
{
    private static List<(string Type, byte[] Bytes)> Subrecords(byte[] content)
    {
        var ret = new List<(string, byte[])>();
        for (var p = 0; p < content.Length;)
        {
            var len = 6 + BitConverter.ToUInt16(content, p + 4);
            ret.Add((Encoding.ASCII.GetString(content, p, 4), content[p..(p + len)]));
            p += len;
        }
        return ret;
    }

    // The Pilot package's subrecords, the procedure tree (from the ANAM after XNAM to the first UNAM) taken out
    private static List<(string Type, byte[] Bytes)> WithoutTree()
    {
        var subs = Subrecords(Hex(PackageCopyKeepsOrderTests.Pilot));
        var xnam = subs.FindIndex(s => s.Type == "XNAM");
        var names = subs.FindIndex(xnam, s => s.Type == "UNAM");
        subs.RemoveRange(xnam + 1, names - xnam - 1);
        return subs;
    }

    private static byte[] Content(List<(string Type, byte[] Bytes)> subs) => subs.SelectMany(s => s.Bytes).ToArray();

    private static IEnumerable<IPackageGetter> Read(byte[] content) => ReadBoth<IPackageGetter>(Record("PACK", content),
        f => Package.CreateFromBinary(f), (s, p) => PackageBinaryOverlay.PackageFactory(s, p));

    private static byte[] Written(IPackageGetter pack)
        => GameConstants.Starfield.MajorRecord(Write(w => pack.WriteToBinary(w))).Content.ToArray();

    [Fact]
    public void NamesAfterXnam_ReadAndWrittenBack()
    {
        var subs = WithoutTree();
        subs.SkipWhile(s => s.Type != "XNAM").Skip(1).Take(3).Select(s => s.Type).ShouldBe(new[] { "UNAM", "BNAM", "PNAM" });
        var content = Content(subs);
        foreach (var pack in Read(content))
        {
            pack.ProcedureTree.ShouldBeEmpty();
            // 27 entries, not by index (0x25 comes last): the read order is kept
            pack.GetProcedureTreeDataOrder()!.Count.ShouldBe(27);
            pack.GetProcedureTreeDataOrder()![^1].ShouldBe((sbyte)0x25);
            pack.Data[0x01].Name.ShouldBe("Patrol Target");
            pack.Data[0x01].Flags.ShouldBe((APackageData.Flag)0);
            pack.Data[0x03].Flags.ShouldBe((APackageData.Flag)1);
            pack.Data[0x25].ShouldBeOfType<PackageDataTarget>().Name.ShouldBe("Docking Target");
            Written(pack).ShouldBe(content);
            Written(pack.DeepCopy()).ShouldBe(content);
        }
    }

    // Every entry starts with its UNAM. A BNAM or PNAM right after XNAM (now one of the step's subrecords, before C38
    // skipped as unknown) is refused with a clear message, not a NullReferenceException.
    [Theory]
    [InlineData("BNAM", "Package input name (BNAM) came before its index (UNAM)")]
    [InlineData("PNAM", "Package input flags (PNAM) came before its index (UNAM)")]
    public void NameOrFlagsBeforeIndex_IsRefused(string type, string message)
    {
        var subs = WithoutTree();
        var xnam = subs.FindIndex(s => s.Type == "XNAM");
        subs.RemoveAt(xnam + 1);
        subs[xnam + 1].Type.ShouldBe("BNAM");
        if (type == "PNAM") subs.RemoveAt(xnam + 1);
        subs[xnam + 1].Type.ShouldBe(type);
        var bytes = Record("PACK", Content(subs));
        Should.Throw<Exception>(() => Package.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta()))))
            .ShouldSatisfyAllConditions(e => Messages(e).ShouldContain(message));
        var meta = Meta();
        Should.Throw<Exception>(() => PackageBinaryOverlay.PackageFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta)))
            .ShouldSatisfyAllConditions(e => Messages(e).ShouldContain(message));
    }

    private static List<string> Messages(Exception? e)
    {
        var ret = new List<string>();
        for (; e != null; e = e.InnerException) ret.Add(e.Message);
        return ret;
    }
}
