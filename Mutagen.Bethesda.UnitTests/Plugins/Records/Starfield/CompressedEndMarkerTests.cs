using System.IO.Abstractions;
using System.IO.Compression;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog.Testing.Extensions;
using Shouldly;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

/// <summary>
/// Furniture and Terminal are written with a STOP end marker after their subrecords.
/// When such a record is compressed, the end marker has to go inside the compressed payload.
/// </summary>
public class CompressedEndMarkerTests
{
    private static readonly byte[] StopMarker = { (byte)'S', (byte)'T', (byte)'O', (byte)'P', 0, 0 };

    private static void Write(StarfieldMod mod, ModPath filePath, IFileSystem fileSystem)
    {
        mod.BeginWrite
            .ToPath(filePath)
            .WithNoLoadOrder()
            .NoModKeySync()
            .WithFileSystem(fileSystem)
            .Write();
    }

    private static byte[] GetFirstRecordData(byte[] bytes)
    {
        // TES4 header record, then the first top level group header, then the first record
        const int RecordHeaderLength = 24;
        const int GroupHeaderLength = 24;
        var tes4Length = BitConverter.ToInt32(bytes, 4);
        var recordPos = RecordHeaderLength + tes4Length + GroupHeaderLength;
        var dataLength = BitConverter.ToInt32(bytes, recordPos + 4);
        return bytes.AsSpan(recordPos + RecordHeaderLength, dataLength).ToArray();
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void CompressedRecordsWithEndMarkerRoundTrip(
        IFileSystem fileSystem,
        ModPath filePath,
        StarfieldMod mod)
    {
        var furniture = mod.Furniture.AddNew("Furniture123");
        furniture.IsCompressed = true;
        var terminal = mod.Terminals.AddNew("Terminal123");
        terminal.IsCompressed = true;
        Write(mod, filePath, fileSystem);

        using var overlay = StarfieldMod.Create(StarfieldRelease.Starfield)
            .FromPath(filePath)
            .WithNoLoadOrder()
            .WithFileSystem(fileSystem)
            .Construct();
        overlay.Furniture.Select(x => x.EditorID)
            .ShouldEqualEnumerable("Furniture123");
        overlay.Terminals.Select(x => x.EditorID)
            .ShouldEqualEnumerable("Terminal123");
        overlay.Furniture.First().IsCompressed.ShouldBeTrue();
        overlay.Terminals.First().IsCompressed.ShouldBeTrue();

        var mutable = StarfieldMod.Create(StarfieldRelease.Starfield)
            .FromPath(filePath)
            .WithNoLoadOrder()
            .WithFileSystem(fileSystem)
            .Mutable()
            .Construct();
        mutable.Furniture.Select(x => x.EditorID)
            .ShouldEqualEnumerable("Furniture123");
        mutable.Terminals.Select(x => x.EditorID)
            .ShouldEqualEnumerable("Terminal123");
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void CompressedEndMarkerIsInsideCompressedPayload(
        IFileSystem fileSystem,
        ModPath filePath,
        StarfieldMod mod)
    {
        var furniture = mod.Furniture.AddNew("Furniture123");
        Write(mod, filePath, fileSystem);
        var uncompressedData = GetFirstRecordData(fileSystem.File.ReadAllBytes(filePath));

        furniture.IsCompressed = true;
        Write(mod, filePath, fileSystem);
        var compressedData = GetFirstRecordData(fileSystem.File.ReadAllBytes(filePath));

        uncompressedData.AsSpan(uncompressedData.Length - StopMarker.Length).ToArray()
            .ShouldBe(StopMarker);

        // uint32 decompressed length, then zlib data that inflates to the uncompressed layout, end marker included
        BitConverter.ToUInt32(compressedData, 0).ShouldBe((uint)uncompressedData.Length);
        using var zlib = new ZLibStream(
            new MemoryStream(compressedData, 4, compressedData.Length - 4),
            CompressionMode.Decompress);
        var inflated = new MemoryStream();
        zlib.CopyTo(inflated);
        inflated.ToArray().ShouldBe(uncompressedData);
    }
}
