using System.IO.Abstractions;
using System.IO.Compression;
using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog.Testing.Extensions;
using StarfieldKeyword = Mutagen.Bethesda.Starfield.Keyword;
using StarfieldMod = Mutagen.Bethesda.Starfield.StarfieldMod;

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

    [Theory, MutagenModAutoData]
    public void DeletedCompressedExport(
        IFileSystem fileSystem,
        ModPath filePath,
        SkyrimMod mod,
        Npc npc)
    {
        npc.EditorID = "Test123";
        npc.IsCompressed = true;
        npc.IsDeleted = true;
        mod.BeginWrite
            .ToPath(filePath)
            .WithNoLoadOrder()
            .NoModKeySync()
            .WithFileSystem(fileSystem)
            .Write();
        npc.IsCompressed.ShouldBeTrue();

        using var overlay = SkyrimMod.Create(mod.SkyrimRelease)
            .FromPath(filePath)
            .WithFileSystem(fileSystem)
            .Construct();
        var overlayNpc = overlay.Npcs.ShouldHaveSingleItem();
        overlayNpc.IsDeleted.ShouldBeTrue();
        overlayNpc.IsCompressed.ShouldBeFalse();

        var reimport = SkyrimMod.Create(mod.SkyrimRelease)
            .FromPath(filePath)
            .WithFileSystem(fileSystem)
            .Mutable()
            .Construct();
        var reimportNpc = reimport.Npcs.ShouldHaveSingleItem();
        reimportNpc.IsDeleted.ShouldBeTrue();
        reimportNpc.IsCompressed.ShouldBeFalse();
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void DeletedCompressedExportStarfield(
        IFileSystem fileSystem,
        ModPath filePath,
        StarfieldMod mod,
        StarfieldKeyword keyword)
    {
        keyword.EditorID = "Test123";
        keyword.IsCompressed = true;
        keyword.IsDeleted = true;
        mod.BeginWrite
            .ToPath(filePath)
            .WithNoLoadOrder()
            .NoModKeySync()
            .WithFileSystem(fileSystem)
            .Write();

        using var overlay = StarfieldMod.Create(mod.StarfieldRelease)
            .FromPath(filePath)
            .WithLoadOrderFromHeaderMasters()
            .WithNoDataFolder()
            .WithFileSystem(fileSystem)
            .Construct();
        var overlayKeyword = overlay.Keywords.ShouldHaveSingleItem();
        overlayKeyword.IsDeleted.ShouldBeTrue();
        overlayKeyword.IsCompressed.ShouldBeTrue();
        overlayKeyword.EditorID.ShouldBe("Test123");

        var reimport = StarfieldMod.Create(mod.StarfieldRelease)
            .FromPath(filePath)
            .WithLoadOrderFromHeaderMasters()
            .WithNoDataFolder()
            .WithFileSystem(fileSystem)
            .Mutable()
            .Construct();
        var reimportKeyword = reimport.Keywords.ShouldHaveSingleItem();
        reimportKeyword.IsDeleted.ShouldBeTrue();
        reimportKeyword.IsCompressed.ShouldBeTrue();
        reimportKeyword.EditorID.ShouldBe("Test123");
    }

    [Fact]
    public void CompressedExportUsesBestCompression()
    {
        var payload = Enumerable.Range(0, 4000).Select(i => (byte)(i * 7 % 251)).ToArray();
        using var stream = new MemoryStream();
        using (var writer = new MutagenWriter(stream, GameConstants.SkyrimSE, dispose: false))
        using (CompressionExport.Compression(true, writer, out var writerToUse))
        {
            writerToUse.Write(payload);
        }

        var written = stream.ToArray();
        written[4].ShouldBe((byte)0x78);
        written[5].ShouldBe((byte)0xDA);
        using var expected = new MemoryStream();
        using (var zlib = new ZLibStream(expected, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(payload);
        }
        written.AsSpan(4).ToArray().ShouldBe(expected.ToArray());
    }
}
