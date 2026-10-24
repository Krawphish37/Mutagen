using System.IO.Abstractions;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Fallout4;

public class TwoByteEnumTests
{
    private const Furniture.EntryPointType EntryPoints = (Furniture.EntryPointType)0x8001;
    private const APerkEffect.Flag PerkFlags = (APerkEffect.Flag)0x8001;

    private static IEnumerable<IFallout4ModGetter> WriteAndReadBoth(
        IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingDir)
    {
        var mod = new Fallout4Mod(modKey, Fallout4Release.Fallout4);
        var furniture = mod.Furniture.AddNew("Furniture");
        furniture.EnabledEntryPoints = EntryPoints;
        var perk = mod.Perks.AddNew("Perk");
        perk.Effects.Add(new PerkEntryPointAddActivateChoice()
        {
            Flags = PerkFlags
        });
        var modPath = Path.Combine(existingDir, mod.ModKey.FileName);
        mod.WriteToBinary(modPath, new BinaryWriteParameters()
        {
            FileSystem = fileSystem
        });

        yield return Fallout4Mod.Create(Fallout4Release.Fallout4)
            .FromPath(modPath)
            .WithFileSystem(fileSystem)
            .Mutable()
            .Construct();
        using var overlay = Fallout4Mod.Create(Fallout4Release.Fallout4)
            .FromPath(modPath)
            .WithFileSystem(fileSystem)
            .Construct();
        yield return overlay;
    }

    [Theory, MutagenAutoData]
    public void FurnitureEntryPointsHighBitNotSignExtended(
        IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingDir)
    {
        foreach (var mod in WriteAndReadBoth(fileSystem, modKey, existingDir))
        {
            mod.Furniture.First().EnabledEntryPoints.ShouldBe(EntryPoints);
        }
    }

    [Theory, MutagenAutoData]
    public void PerkEffectFlagsHighBitNotSignExtended(
        IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingDir)
    {
        foreach (var mod in WriteAndReadBoth(fileSystem, modKey, existingDir))
        {
            var effect = mod.Perks.First().Effects.Single().ShouldBeAssignableTo<IPerkEntryPointAddActivateChoiceGetter>()!;
            effect.Flags.ShouldBe(PerkFlags);
        }
    }
}
