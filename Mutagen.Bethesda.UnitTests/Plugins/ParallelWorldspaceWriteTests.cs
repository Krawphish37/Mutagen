using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Shouldly;
using Mutagen.Bethesda.Fallout3;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog.Testing.Extensions;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins;

public class ParallelWorldspaceWriteTests
{
    public enum WorldspaceState
    {
        Normal,
        Compressed,
        Deleted,
    }

    private static readonly ParallelWriteParameters SerialWrite = new() { MaxDegreeOfParallelism = 1 };
    private static readonly ParallelWriteParameters ParallelWrite = new() { MaxDegreeOfParallelism = 4 };

    private static IMod CreateModWithWorldspace(GameRelease release, WorldspaceState state)
    {
        var modKey = ModKey.FromFileName("ParallelWorldspace.esp");
        IMod mod;
        IMajorRecord worldspace;
        switch (release)
        {
            case GameRelease.Oblivion:
            {
                var m = new OblivionMod(modKey, OblivionRelease.Oblivion);
                worldspace = m.Worldspaces.AddNew();
                mod = m;
                break;
            }
            case GameRelease.Fallout3:
            {
                var m = new Fallout3Mod(modKey, Fallout3Release.Fallout3);
                worldspace = m.Worldspaces.AddNew();
                mod = m;
                break;
            }
            case GameRelease.SkyrimSE:
            {
                var m = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE);
                worldspace = m.Worldspaces.AddNew();
                mod = m;
                break;
            }
            case GameRelease.Fallout4:
            {
                var m = new Fallout4Mod(modKey, Fallout4Release.Fallout4);
                worldspace = m.Worldspaces.AddNew();
                mod = m;
                break;
            }
            case GameRelease.Starfield:
            {
                var m = new StarfieldMod(modKey, StarfieldRelease.Starfield);
                worldspace = m.Worldspaces.AddNew();
                mod = m;
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(release));
        }
        worldspace.EditorID = "TestWorldspace";
        worldspace.IsCompressed = state == WorldspaceState.Compressed;
        worldspace.IsDeleted = state == WorldspaceState.Deleted;
        return mod;
    }

    private static byte[] Write(IModGetter mod, ParallelWriteParameters parallel)
    {
        var fileSystem = new MockFileSystem();
        var path = Path.Combine("C:\\", mod.ModKey.FileName);
        mod.WriteToBinary(path, new BinaryWriteParameters()
        {
            FileSystem = fileSystem,
            Parallel = parallel,
        });
        return fileSystem.File.ReadAllBytes(path);
    }

    [Theory]
    [InlineData(GameRelease.Oblivion, WorldspaceState.Normal)]
    [InlineData(GameRelease.Oblivion, WorldspaceState.Compressed)]
    [InlineData(GameRelease.Oblivion, WorldspaceState.Deleted)]
    [InlineData(GameRelease.Fallout3, WorldspaceState.Normal)]
    [InlineData(GameRelease.Fallout3, WorldspaceState.Compressed)]
    [InlineData(GameRelease.Fallout3, WorldspaceState.Deleted)]
    [InlineData(GameRelease.SkyrimSE, WorldspaceState.Normal)]
    [InlineData(GameRelease.SkyrimSE, WorldspaceState.Compressed)]
    [InlineData(GameRelease.SkyrimSE, WorldspaceState.Deleted)]
    [InlineData(GameRelease.Fallout4, WorldspaceState.Normal)]
    [InlineData(GameRelease.Fallout4, WorldspaceState.Compressed)]
    [InlineData(GameRelease.Fallout4, WorldspaceState.Deleted)]
    [InlineData(GameRelease.Starfield, WorldspaceState.Normal)]
    [InlineData(GameRelease.Starfield, WorldspaceState.Compressed)]
    [InlineData(GameRelease.Starfield, WorldspaceState.Deleted)]
    public void ParallelWorldspaceWriteMatchesSerial(GameRelease release, WorldspaceState state)
    {
        var mod = CreateModWithWorldspace(release, state);
        var serial = Write(mod, SerialWrite);
        var parallel = Write(mod, ParallelWrite);
        parallel.ShouldBe(serial);
    }

    [Theory, MutagenModAutoData(GameRelease.SkyrimSE)]
    public void SkyrimCompressedWorldspaceExport(
        IFileSystem fileSystem,
        ModPath filePath,
        SkyrimMod mod)
    {
        var worldspace = mod.Worldspaces.AddNew();
        worldspace.EditorID = "Test123";
        worldspace.IsCompressed = true;
        mod.BeginWrite
            .ToPath(filePath)
            .WithNoLoadOrder()
            .NoModKeySync()
            .WithFileSystem(fileSystem)
            .Write();
        using var reimport = SkyrimMod.Create(mod.SkyrimRelease)
            .FromPath(filePath)
            .WithFileSystem(fileSystem)
            .Construct();
        reimport.Worldspaces.Select(x => x.EditorID)
            .ShouldEqualEnumerable("Test123");
        reimport.Worldspaces.First().IsCompressed.ShouldBeTrue();
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void StarfieldCompressedWorldspaceExport(
        IFileSystem fileSystem,
        ModPath filePath,
        StarfieldMod mod)
    {
        var worldspace = mod.Worldspaces.AddNew();
        worldspace.EditorID = "Test123";
        worldspace.IsCompressed = true;
        mod.BeginWrite
            .ToPath(filePath)
            .WithNoLoadOrder()
            .NoModKeySync()
            .WithFileSystem(fileSystem)
            .Write();
        using var reimport = StarfieldMod.Create(mod.StarfieldRelease)
            .FromPath(filePath)
            .WithNoLoadOrder()
            .WithFileSystem(fileSystem)
            .Construct();
        reimport.Worldspaces.Select(x => x.EditorID)
            .ShouldEqualEnumerable("Test123");
        reimport.Worldspaces.First().IsCompressed.ShouldBeTrue();
    }
}
