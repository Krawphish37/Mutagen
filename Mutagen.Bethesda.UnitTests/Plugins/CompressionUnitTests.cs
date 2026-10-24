using System.IO.Abstractions;
using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog.Testing.Extensions;

namespace Mutagen.Bethesda.UnitTests.Plugins;

public class CompressionUnitTests
{
    [Theory, MutagenModAutoData]
    public void CompressedExport(
        IFileSystem fileSystem,
        ModPath filePath,
        SkyrimMod mod,
        Npc npc)
    {
        npc.EditorID = "Test123";
        npc.IsCompressed = true;
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
        reimport.Npcs.Select(x => x.EditorID)
            .ShouldEqualEnumerable("Test123");
    }

    [Theory, MutagenModAutoData]
    public void CompressedExportToStreamSingleThread(
        SkyrimMod mod,
        Npc npc)
    {
        npc.EditorID = "Test123";
        npc.IsCompressed = true;
        using var stream = new MemoryStream();
        mod.WriteToBinary(stream, new BinaryWriteParameters()
        {
            ModKey = ModKeyOption.NoCheck,
            Parallel = new ParallelWriteParameters()
            {
                MaxDegreeOfParallelism = 1
            }
        });
        stream.CanWrite.ShouldBeTrue();
        var bytes = stream.ToArray();
        using var reimport = SkyrimMod.Create(mod.SkyrimRelease)
            .FromStreamFactory(() => new MemoryStream(bytes), mod.ModKey)
            .Construct();
        reimport.Npcs.Select(x => x.EditorID)
            .ShouldEqualEnumerable("Test123");
        reimport.Npcs.First().IsCompressed.ShouldBeTrue();
    }
}