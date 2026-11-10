using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog;
using Shouldly;
using Xunit;
using SK = Mutagen.Bethesda.Skyrim;
using FO4 = Mutagen.Bethesda.Fallout4;
using SF = Mutagen.Bethesda.Starfield;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

public class DictionaryEqualityTests
{
    private static SF.Package GetStarfieldPackage(params sbyte[] keyOrder)
    {
        var package = new SF.Package(TestConstants.Form1, SF.StarfieldRelease.Starfield)
        {
            EditorID = "Package"
        };
        foreach (var key in keyOrder)
        {
            package.Data[key] = key switch
            {
                0 => new SF.PackageDataInt() { Data = 5 },
                2 => new SF.PackageDataBool() { Data = true },
                _ => new SF.PackageDataFloat() { Data = key },
            };
        }
        return package;
    }

    [Fact]
    public void StarfieldPackageDataOrderDoesNotMatter()
    {
        var package = GetStarfieldPackage(2, 0);
        var other = GetStarfieldPackage(0, 2);
        package.Equals(other).ShouldBeTrue();
        other.Equals(package).ShouldBeTrue();
    }

    [Fact]
    public void StarfieldPackageDataDifferentValue()
    {
        var package = GetStarfieldPackage(2, 0);
        var other = GetStarfieldPackage(0, 2);
        ((SF.PackageDataInt)other.Data[0]).Data = 6;
        package.Equals(other).ShouldBeFalse();
    }

    [Fact]
    public void StarfieldPackageDataDifferentKeys()
    {
        var package = GetStarfieldPackage(2, 0);
        package.Equals(GetStarfieldPackage(0, 2, 7)).ShouldBeFalse();
        package.Equals(GetStarfieldPackage(0)).ShouldBeFalse();
        package.Equals(GetStarfieldPackage(0, 7)).ShouldBeFalse();
    }

    [Theory, MutagenAutoData]
    public void StarfieldPackageDataEqualAfterRoundTrip(
        IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingDir)
    {
        var mod = new SF.StarfieldMod(modKey, SF.StarfieldRelease.Starfield);
        var package = mod.Packages.AddNew("Package");
        package.Data[2] = new SF.PackageDataBool() { Data = true };
        package.Data[0] = new SF.PackageDataInt() { Data = 5 };
        var modPath = Path.Combine(existingDir, mod.ModKey.FileName);
        mod.WriteToBinary(modPath, new BinaryWriteParameters()
        {
            FileSystem = fileSystem
        });

        // The writer keeps the entries in their order (as Starfield.esm holds them, not sorted by key), so the reimport has the original's order
        var reimport = SF.StarfieldMod.Create(SF.StarfieldRelease.Starfield)
            .FromPath(modPath)
            .WithLoadOrderFromHeaderMasters()
            .WithNoDataFolder()
            .WithFileSystem(fileSystem)
            .Mutable()
            .Construct();
        var reimportPackage = reimport.Packages.First();
        reimportPackage.Data.Keys.ShouldBe(new sbyte[] { 2, 0 });
        package.Equals(reimportPackage).ShouldBeTrue();

        using var overlay = SF.StarfieldMod.Create(SF.StarfieldRelease.Starfield)
            .FromPath(modPath)
            .WithLoadOrderFromHeaderMasters()
            .WithNoDataFolder()
            .WithFileSystem(fileSystem)
            .Construct();
        ((SF.IPackageGetter)package).Equals(overlay.Packages.First()).ShouldBeTrue();
    }

    [Fact]
    public void SkyrimPackageDataOrderDoesNotMatter()
    {
        var package = new SK.Package(TestConstants.Form1, SK.SkyrimRelease.SkyrimSE);
        package.Data[2] = new SK.PackageDataBool() { Data = true };
        package.Data[0] = new SK.PackageDataInt() { Data = 5 };
        var other = new SK.Package(TestConstants.Form1, SK.SkyrimRelease.SkyrimSE);
        other.Data[0] = new SK.PackageDataInt() { Data = 5 };
        other.Data[2] = new SK.PackageDataBool() { Data = true };
        package.Equals(other).ShouldBeTrue();
        ((SK.PackageDataInt)other.Data[0]).Data = 6;
        package.Equals(other).ShouldBeFalse();
    }

    [Fact]
    public void Fallout4PackageDataOrderDoesNotMatter()
    {
        var package = new FO4.Package(TestConstants.Form1, FO4.Fallout4Release.Fallout4);
        package.Data[2] = new FO4.PackageDataBool() { Data = true };
        package.Data[0] = new FO4.PackageDataInt() { Data = 5 };
        var other = new FO4.Package(TestConstants.Form1, FO4.Fallout4Release.Fallout4);
        other.Data[0] = new FO4.PackageDataInt() { Data = 5 };
        other.Data[2] = new FO4.PackageDataBool() { Data = true };
        package.Equals(other).ShouldBeTrue();
        ((FO4.PackageDataInt)other.Data[0]).Data = 6;
        package.Equals(other).ShouldBeFalse();
    }

    [Fact]
    public void SkyrimRaceDictionaryOrderDoesNotMatter()
    {
        // Only the dictionaries, as gendered items are compared separately
        var mask = new SK.Race.TranslationMask(defaultOn: false)
        {
            BipedObjectNames = true,
            Starting = true,
        };
        var race = new SK.Race(TestConstants.Form1, SK.SkyrimRelease.SkyrimSE);
        race.BipedObjectNames[SK.BipedObject.Hands] = "Hands";
        race.BipedObjectNames[SK.BipedObject.Head] = "Head";
        race.Starting[SK.BasicStat.Stamina] = 2;
        race.Starting[SK.BasicStat.Health] = 1;
        var other = new SK.Race(TestConstants.Form1, SK.SkyrimRelease.SkyrimSE);
        other.BipedObjectNames[SK.BipedObject.Head] = "Head";
        other.BipedObjectNames[SK.BipedObject.Hands] = "Hands";
        other.Starting[SK.BasicStat.Health] = 1;
        other.Starting[SK.BasicStat.Stamina] = 2;
        SK.RaceMixIn.Equals(race, other, mask).ShouldBeTrue();
        other.BipedObjectNames[SK.BipedObject.Hands] = "Gloves";
        SK.RaceMixIn.Equals(race, other, mask).ShouldBeFalse();
    }
}
