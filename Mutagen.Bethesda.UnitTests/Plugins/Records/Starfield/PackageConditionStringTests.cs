using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C52: a package's target data input whose condition has a text parameter (CIS1, CIS2) followed by another data input.
// FillPackageData's first pass (the kinds of the inputs, from the ANAMs) skipped only the value subrecords it knew and
// stopped at a CIS1/CIS2, so the inputs after it got no kind and both readers threw "Index was out of range". Mutagen
// writes such packages itself (ConditionBinaryWriteTranslation.CustomStringExports). No PACK in Data has one, so the
// package is built here.
public class PackageConditionStringTests
{
    private static Package Build(string? s1, string? s2)
    {
        var package = new Package(FormKey.Factory("000800:Test.esp"), StarfieldRelease.Starfield) { EditorID = "CisPackage" };
        var target = new PackageDataTarget { Type = PackageDataTarget.Types.SingleRef, Name = "Target" };
        IConditionParameters parameters = new ActorExposedToSkyConditionData();
        parameters.StringParameter1 = s1;
        parameters.StringParameter2 = s2;
        target.Conditions.Add(new ConditionFloat { ComparisonValue = 1, Data = (ConditionData)parameters });
        package.Data[0] = target;
        package.Data[1] = new PackageDataBool { Data = true, Name = "Flag", Flags = APackageData.Flag.Public };
        return package;
    }

    // The full import, or the overlay
    private static IPackageGetter Read(byte[] bytes, bool overlay) => ReadBoth<IPackageGetter>(bytes,
        f => Package.CreateFromBinary(f), (s, p) => PackageBinaryOverlay.PackageFactory(s, p)).Skip(overlay ? 1 : 0).First();

    [Theory]
    [InlineData("abc", null, false)]
    [InlineData(null, "def", false)]
    [InlineData("abc", "def", false)]
    [InlineData("abc", null, true)]
    [InlineData(null, "def", true)]
    [InlineData("abc", "def", true)]
    public void TargetConditionWithText_ThenAnotherInput_ReadAndWrittenBack(string? s1, string? s2, bool overlay)
    {
        var bytes = Write(w => ((IPackageGetter)Build(s1, s2)).WriteToBinary(w));
        var pack = Read(bytes, overlay);
        if (overlay) pack.ShouldBeOfType<PackageBinaryOverlay>();
        else pack.ShouldBeOfType<Package>();
        pack.Data.Count.ShouldBe(2);
        var target = pack.Data[0].ShouldBeAssignableTo<IPackageDataTargetGetter>()!;
        var data = target.Conditions.ShouldHaveSingleItem().Data.ShouldBeAssignableTo<IConditionParametersGetter>()!;
        data.StringParameter1.ShouldBe(s1);
        data.StringParameter2.ShouldBe(s2);
        var flag = pack.Data[1].ShouldBeAssignableTo<IPackageDataBoolGetter>()!;
        flag.Data.ShouldBeTrue();
        flag.Name.ShouldBe("Flag");
        flag.Flags.ShouldBe(APackageData.Flag.Public);
        Write(w => pack.WriteToBinary(w)).ShouldBe(bytes);
        Write(w => ((IPackageGetter)pack.DeepCopy()).WriteToBinary(w)).ShouldBe(bytes);
    }
}
