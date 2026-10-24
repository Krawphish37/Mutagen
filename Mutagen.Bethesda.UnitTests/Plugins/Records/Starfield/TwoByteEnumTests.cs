using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

public class TwoByteEnumTests
{
    private const Package.InterruptFlag InterruptFlags = (Package.InterruptFlag)0xFEFC;
    private const Mutagen.Bethesda.Starfield.Activator.Flag ActivatorFlags = (Mutagen.Bethesda.Starfield.Activator.Flag)0x8001;
    private const APerkEffect.Flag PerkFlags = (APerkEffect.Flag)0x8001;

    private static IEnumerable<IStarfieldModGetter> WriteAndReadBoth(
        IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingDir)
    {
        var mod = new StarfieldMod(modKey, StarfieldRelease.Starfield);
        var package = mod.Packages.AddNew("Package");
        package.InterruptFlags = InterruptFlags;
        var activator = mod.Activators.AddNew("Activator");
        activator.Flags = ActivatorFlags;
        var quest = mod.Quests.AddNew("Quest");
        quest.Aliases = new()
        {
            new QuestReferenceAlias()
            {
                ID = 1,
                CreateReferenceToObject = new CreateReferenceToObject()
                {
                    Create = CreateReferenceToObject.CreateEnum.In
                }
            }
        };
        var perk = mod.Perks.AddNew("Perk");
        perk.Ranks.Add(new PerkRank()
        {
            Effects =
            {
                new PerkEntryPointAddActivateChoice()
                {
                    Flags = PerkFlags
                }
            }
        });
        var modPath = Path.Combine(existingDir, mod.ModKey.FileName);
        mod.WriteToBinary(modPath, new BinaryWriteParameters()
        {
            FileSystem = fileSystem
        });

        yield return StarfieldMod.Create(StarfieldRelease.Starfield)
            .FromPath(modPath)
            .WithNoLoadOrder()
            .WithFileSystem(fileSystem)
            .Mutable()
            .Construct();
        using var overlay = StarfieldMod.Create(StarfieldRelease.Starfield)
            .FromPath(modPath)
            .WithNoLoadOrder()
            .WithFileSystem(fileSystem)
            .Construct();
        yield return overlay;
    }

    [Theory, MutagenAutoData]
    public void HighBitInStructEnumNotSignExtended(
        IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingDir)
    {
        foreach (var mod in WriteAndReadBoth(fileSystem, modKey, existingDir))
        {
            var flags = mod.Packages.First().InterruptFlags;
            ((int)flags).ShouldBe(0xFEFC);
            flags.ShouldBe(InterruptFlags);
        }
    }

    [Theory, MutagenAutoData]
    public void HighBitInSubrecordEnumNotSignExtended(
        IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingDir)
    {
        foreach (var mod in WriteAndReadBoth(fileSystem, modKey, existingDir))
        {
            mod.Activators.First().Flags.ShouldBe(ActivatorFlags);
        }
    }

    [Theory, MutagenAutoData]
    public void NamedValueAboveInt16MaxReadsAsNamedValue(
        IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingDir)
    {
        foreach (var mod in WriteAndReadBoth(fileSystem, modKey, existingDir))
        {
            var alias = mod.Quests.First().Aliases.Single().ShouldBeAssignableTo<IQuestReferenceAliasGetter>()!;
            alias.CreateReferenceToObject!.Create.ShouldBe(CreateReferenceToObject.CreateEnum.In);
        }
    }

    [Theory, MutagenAutoData]
    public void PerkRankEffectFlagsHighBitNotSignExtended(
        IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingDir)
    {
        foreach (var mod in WriteAndReadBoth(fileSystem, modKey, existingDir))
        {
            var effect = mod.Perks.First().Ranks.Single().Effects.Single().ShouldBeAssignableTo<IPerkEntryPointAddActivateChoiceGetter>()!;
            effect.Flags.ShouldBe(PerkFlags);
        }
    }
}
