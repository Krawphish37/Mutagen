using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C63: a package's input names and flags (UNAM index, BNAM name, PNAM flags) follow the procedure tree (ANAM
// branches). With an empty tree they follow XNAM directly. Mutagen wrote them there but read them only after an
// ANAM, so both readers left them unclaimed and the names were lost (reported from Spriggit round trips of mods).
// No package in Starfield.esm has this shape: after XNAM, 3,369 have POBA and 179 an ANAM branch.
public class PackageInputNameTests
{
    private static Package Make()
    {
        var package = new Package(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            EditorID = "TestPackage",
            DataInputVersion = 1,
        };
        package.Data[0] = new PackageDataBool { Data = true, Name = "Wander", Flags = APackageData.Flag.Public };
        package.Data[1] = new PackageDataFloat { Data = 2.5f, Name = "Radius" };
        return package;
    }

    private static IEnumerable<IPackageGetter> Read(byte[] bytes)
        => ReadBoth<IPackageGetter>(bytes, f => Package.CreateFromBinary(f), (s, p) => PackageBinaryOverlay.PackageFactory(s, p));

    private static List<string> Signatures(byte[] bytes)
        => GameConstants.Starfield.MajorRecord(bytes).EnumerateSubrecords().Select(s => s.RecordType.Type).ToList();

    [Fact]
    public void NamesAfterEmptyTree_WrittenAfterXnam()
    {
        var bytes = Write(w => Make().WriteToBinary(w));
        var subs = Signatures(bytes);
        var x = subs.IndexOf("XNAM");
        subs.Skip(x + 1).ShouldBe(new[] { "UNAM", "BNAM", "PNAM", "UNAM", "BNAM" });
    }

    [Fact]
    public void NamesAfterEmptyTree_BothReadersKeepThem()
    {
        var bytes = Write(w => Make().WriteToBinary(w));
        foreach (var package in Read(bytes))
        {
            package.ProcedureTree.ShouldBeEmpty();
            package.Data.Count.ShouldBe(2);
            var wander = package.Data[0].ShouldBeAssignableTo<IPackageDataBoolGetter>()!;
            wander.Data.ShouldBeTrue();
            wander.Name.ShouldBe("Wander");
            wander.Flags.ShouldBe(APackageData.Flag.Public);
            var radius = package.Data[1].ShouldBeAssignableTo<IPackageDataFloatGetter>()!;
            radius.Data.ShouldBe(2.5f);
            radius.Name.ShouldBe("Radius");
            radius.Flags.ShouldBeNull();
            Write(w => package.WriteToBinary(w)).ShouldBe(bytes);
        }
    }
}
