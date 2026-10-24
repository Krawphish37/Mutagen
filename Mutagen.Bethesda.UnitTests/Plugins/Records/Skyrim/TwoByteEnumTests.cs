using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Skyrim;

public class TwoByteEnumTests
{
    private const PerkScriptFlag.Flag PerkFlags = (PerkScriptFlag.Flag)0x8001;

    [Theory, MutagenAutoData]
    public void PerkScriptFlagHighBitNotSignExtended(
        IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingDir)
    {
        var mod = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE);
        var perk = mod.Perks.AddNew("Perk");
        perk.Effects.Add(new PerkEntryPointAddActivateChoice()
        {
            Flags = new PerkScriptFlag()
            {
                Flags = PerkFlags,
                FragmentIndex = 3
            }
        });
        var modPath = Path.Combine(existingDir, mod.ModKey.FileName);
        mod.WriteToBinary(modPath, new BinaryWriteParameters()
        {
            FileSystem = fileSystem
        });

        var full = SkyrimMod.Create(SkyrimRelease.SkyrimSE)
            .FromPath(modPath)
            .WithFileSystem(fileSystem)
            .Mutable()
            .Construct();
        using var overlay = SkyrimMod.Create(SkyrimRelease.SkyrimSE)
            .FromPath(modPath)
            .WithFileSystem(fileSystem)
            .Construct();
        foreach (var readMod in new ISkyrimModGetter[] { full, overlay })
        {
            var flags = readMod.Perks.First().Effects.Single().Flags;
            flags.Flags.ShouldBe(PerkFlags);
            flags.FragmentIndex.ShouldBe((ushort)3);
        }
    }
}
