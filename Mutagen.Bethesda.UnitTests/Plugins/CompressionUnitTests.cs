using System.IO.Abstractions;
using System.IO.Compression;
using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Meta;
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
