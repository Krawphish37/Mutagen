using System.IO.Compression;
using System.Text;
using ICSharpCode.SharpZipLib.Zip.Compression;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Archives.Ba2;
using Mutagen.Bethesda.Archives.Bsa;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Archives;

/// <summary>
/// C42: IArchiveFile.GetStorage() says where a file's bytes are in its archive, how many are on disk and how they are
/// compressed. For every format the reader reads (BA2 general and texture, versions 1, 2 and 3 with zlib and LZ4;
/// BSA versions 103, 104 and 105, compressed by default or not, with and without embedded names, with the compression
/// toggle bit): the chunks read raw from the archive's bytes and inflated, after the reader's own header, are what
/// GetBytes() returns.
/// </summary>
public class ArchiveFileStorageTests
{
    /// <summary>
    /// Reads each chunk raw from the archive's bytes, inflates it as its compression says, and checks the result is the
    /// file's content after the reader's own header; checks the sizes add up
    /// </summary>
    internal static void ShouldMatchContent(IArchiveFile file, byte[] archive)
    {
        var storage = file.GetStorage();
        storage.ShouldNotBeNull();
        var content = file.GetBytes();
        storage.Size.ShouldBe(file.Size);
        content.Length.ShouldBe((int)file.Size);
        storage.Chunks.ShouldNotBeEmpty();
        storage.Compressed.ShouldBe(storage.Chunks.Any(c => c.Compressed));
        foreach (var chunk in storage.Chunks)
        {
            if (chunk.Compressed) chunk.Compression.ShouldBe(storage.Compression);
            else chunk.StoredSize.ShouldBe(chunk.Size);
            chunk.Offset.ShouldBeGreaterThanOrEqualTo(storage.Offset);
            (chunk.Offset + chunk.StoredSize).ShouldBeLessThanOrEqualTo(archive.Length);
        }

        var rebuilt = new MemoryStream();
        foreach (var chunk in storage.Chunks)
        {
            var raw = archive.AsSpan(checked((int)chunk.Offset), (int)chunk.StoredSize).ToArray();
            var inflated = Inflate(raw, chunk.Compression, chunk.Size);
            inflated.Length.ShouldBe((int)chunk.Size);
            rebuilt.Write(inflated);
        }
        content[(int)storage.SynthesizedHeaderLength..].ShouldBe(rebuilt.ToArray());
    }

    internal static byte[] Inflate(byte[] raw, ArchiveCompression compression, uint size)
    {
        switch (compression)
        {
            case ArchiveCompression.None:
                return raw;
            case ArchiveCompression.Zlib:
            {
                using var zlib = new ZLibStream(new MemoryStream(raw), CompressionMode.Decompress);
                var ret = new MemoryStream();
                zlib.CopyTo(ret);
                return ret.ToArray();
            }
            case ArchiveCompression.Lz4Block:
            {
                var ret = new byte[size];
                LZ4Codec.Decode(raw, ret).ShouldBe((int)size);
                return ret;
            }
            case ArchiveCompression.Lz4Frame:
            {
                using var lz4 = LZ4Stream.Decode(new MemoryStream(raw));
                var ret = new MemoryStream();
                lz4.CopyTo(ret);
                return ret.ToArray();
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(compression));
        }
    }

    private static byte[] Zlib(byte[] data)
    {
        var deflater = new Deflater();
        deflater.SetInput(data);
        deflater.Finish();
        var buffer = new byte[data.Length + 256];
        var length = deflater.Deflate(buffer);
        return buffer[..length];
    }

    private static byte[] Lz4Block(byte[] data)
    {
        var buffer = new byte[LZ4Codec.MaximumOutputSize(data.Length)];
        var length = LZ4Codec.Encode(data, buffer);
        return buffer[..length];
    }

    private static byte[] Lz4Frame(byte[] data)
    {
        var ms = new MemoryStream();
        using (var lz4 = LZ4Stream.Encode(ms, leaveOpen: true))
        {
            lz4.Write(data);
        }
        return ms.ToArray();
    }

    private static byte[] Content(int i) =>
        Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat($"file {i} BSTriShape NiNode bhkNPCollisionObject ", 10 + i * 3)));

    // ---- BA2 general ----

    /// <summary>A general BA2: six files, every other one compressed, 64 bytes of other data between them</summary>
    private static (byte[] Archive, List<byte[]> Contents) BuildGeneralBa2(uint version, uint compression)
    {
        var contents = Enumerable.Range(0, 6).Select(Content).ToList();
        var lz4 = version == 3 && compression == 3;
        var stored = contents.Select((c, i) => i % 2 == 0 ? c : lz4 ? Lz4Block(c) : Zlib(c)).ToList();
        var headerLength = version switch { 1 => 24, 2 => 32, _ => 36 };
        long dataStart = headerLength + 36L * contents.Count;
        var gap = new byte[64];
        long nameTable = dataStart + stored.Sum(s => s.Length + gap.Length);

        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write("BTDX"u8);
        writer.Write(version);
        writer.Write("GNRL"u8);
        writer.Write((uint)contents.Count);
        writer.Write((ulong)nameTable);
        if (version != 1)
        {
            writer.Write(1u);
            writer.Write(0u);
        }
        if (version == 3) writer.Write(compression);
        long offset = dataStart;
        for (int i = 0; i < contents.Count; i++)
        {
            writer.Write(0x1000u + (uint)i); // name hash
            writer.Write("nif\0"u8);
            writer.Write(0x5678u); // directory hash
            writer.Write(0u); // flags
            writer.Write((ulong)offset);
            writer.Write(i % 2 == 0 ? 0u : (uint)stored[i].Length);
            writer.Write((uint)contents[i].Length);
            writer.Write(0xBAADF00Du);
            offset += stored[i].Length + gap.Length;
        }
        foreach (var s in stored)
        {
            writer.Write(s);
            writer.Write(gap);
        }
        for (int i = 0; i < contents.Count; i++)
        {
            var name = Encoding.UTF8.GetBytes($"meshes/test{i}.nif");
            writer.Write((short)name.Length);
            writer.Write(name);
        }
        writer.Flush();
        return (stream.ToArray(), contents);
    }

    [Theory]
    [InlineData(1u, 0u)] // Fallout 4
    [InlineData(2u, 0u)] // Starfield, zlib
    [InlineData(3u, 0u)]
    [InlineData(3u, 3u)] // Starfield, LZ4 blocks
    public void Ba2General_ChunksReadRawAndInflated_AreTheContent(uint version, uint compression)
    {
        var (bytes, contents) = BuildGeneralBa2(version, compression);
        var archive = new Ba2Reader(() => new MemoryStream(bytes));
        var files = archive.Files.ToList();
        files.Count.ShouldBe(contents.Count);
        var expectedKind = version == 3 && compression == 3 ? ArchiveCompression.Lz4Block : ArchiveCompression.Zlib;
        for (int i = 0; i < files.Count; i++)
        {
            files[i].GetBytes().ShouldBe(contents[i]);
            ShouldMatchContent(files[i], bytes);
            var storage = files[i].GetStorage()!;
            storage.Chunks.Count.ShouldBe(1);
            storage.SynthesizedHeaderLength.ShouldBe(0u);
            storage.Offset.ShouldBe(storage.Chunks[0].Offset);
            storage.StoredSize.ShouldBe(storage.Chunks[0].StoredSize);
            if (i % 2 == 0)
            {
                storage.Compression.ShouldBe(ArchiveCompression.None);
                storage.StoredSize.ShouldBe(contents[i].Length);
            }
            else
            {
                storage.Compression.ShouldBe(expectedKind);
                storage.StoredSize.ShouldBeLessThan(contents[i].Length);
            }
        }
    }

    [Fact]
    public void Ba2General_TryGetFile_GivesTheSameStorageAsFiles()
    {
        var (bytes, _) = BuildGeneralBa2(2, 0);
        var all = new Ba2Reader(() => new MemoryStream(bytes)).Files.ToList();
        // a new reader, so the file is built from its one record (C201)
        var reader = new Ba2Reader(() => new MemoryStream(bytes));
        reader.TryGetFile("MESHES\\test3.nif", out var one).ShouldBeTrue();
        var expected = all[3].GetStorage()!;
        var storage = one!.GetStorage()!;
        storage.Offset.ShouldBe(expected.Offset);
        storage.StoredSize.ShouldBe(expected.StoredSize);
        storage.Compression.ShouldBe(expected.Compression);
        storage.Chunks.ShouldBe(expected.Chunks);
        ShouldMatchContent(one, bytes);
    }

    [Fact]
    public void Ba2General_Fixture()
    {
        var bytes = File.ReadAllBytes(Ba2Tests.TestBa2.Path);
        var file = new Ba2Reader(() => new MemoryStream(bytes)).Files.Single();
        var storage = file.GetStorage()!;
        storage.Offset.ShouldBe(0x3C);
        storage.StoredSize.ShouldBe(8);
        storage.Compression.ShouldBe(ArchiveCompression.None);
        ShouldMatchContent(file, bytes);
    }

    // ---- BA2 textures ----

    [Theory]
    [InlineData(2u, 0u)]
    [InlineData(3u, 0u)]
    [InlineData(3u, 3u)]
    public void Ba2Texture_EachChunk_StoredOrCompressed(uint version, uint compression)
    {
        var bytes = Ba2StarfieldTests.BuildTextureBa2(version, compression);
        var file = new Ba2Reader(() => new MemoryStream(bytes)).Files.Single();
        ShouldMatchContent(file, bytes);
        var storage = file.GetStorage()!;
        var kind = version == 3 && compression == 3 ? ArchiveCompression.Lz4Block : ArchiveCompression.Zlib;
        storage.Compression.ShouldBe(kind);
        storage.SynthesizedHeaderLength.ShouldBe(4u + 124u); // DDS magic and header, BC1
        storage.Chunks.Count.ShouldBe(2);
        storage.Chunks[0].Compression.ShouldBe(ArchiveCompression.None);
        storage.Chunks[0].StoredSize.ShouldBe(8u);
        storage.Chunks[1].Compression.ShouldBe(kind);
        storage.Chunks[1].Size.ShouldBe(8u);
        storage.Offset.ShouldBe(storage.Chunks[0].Offset);
        storage.StoredSize.ShouldBe(storage.Chunks.Sum(c => (long)c.StoredSize));
    }

    [Fact]
    public void Ba2Texture_Fixture()
    {
        var bytes = File.ReadAllBytes(Ba2Tests.TestTextureBa2.Path);
        var file = new Ba2Reader(() => new MemoryStream(bytes)).Files.Single();
        ShouldMatchContent(file, bytes);
        var storage = file.GetStorage()!;
        storage.Chunks.ShouldBe([
            new ArchiveFileChunk(0x60, 8, 8, ArchiveCompression.None),
            new ArchiveFileChunk(0x68, 0x10, 8, ArchiveCompression.Zlib),
        ]);
        storage.Offset.ShouldBe(0x60);
        storage.StoredSize.ShouldBe(0x18);
        storage.Compression.ShouldBe(ArchiveCompression.Zlib);
    }

    // ---- BSA ----

    /// <summary>
    /// A BSA with one folder of four files; the file at index 1 has the compression toggle bit. When the archive embeds
    /// names, each file's data is preceded by its path as a byte-length string; when compressed, by its inflated size.
    /// </summary>
    private static (byte[] Archive, List<byte[]> Contents, List<bool> Compressed) BuildBsa(
        BsaVersionType version, bool compressedByDefault, bool embedNames)
    {
        var contents = Enumerable.Range(0, 4).Select(Content).ToList();
        const string folder = "meshes\\test";
        var names = contents.Select((_, i) => $"file{i}.txt").ToList();
        var compressed = contents.Select((_, i) => i == 1 ? !compressedByDefault : compressedByDefault).ToList();
        var flags = BsaArchiveFlags.HasFolderNames | BsaArchiveFlags.HasFileNames;
        if (compressedByDefault) flags |= BsaArchiveFlags.Compressed;
        if (embedNames) flags |= BsaArchiveFlags.HasFileNameBlobs;

        var stretches = new List<byte[]>();
        for (int i = 0; i < contents.Count; i++)
        {
            var s = new MemoryStream();
            var w = new BinaryWriter(s);
            if (embedNames)
            {
                var path = Encoding.ASCII.GetBytes($"{folder}\\{names[i]}");
                w.Write((byte)path.Length);
                w.Write(path);
            }
            if (compressed[i])
            {
                w.Write((uint)contents[i].Length);
                w.Write(version == BsaVersionType.SSE ? Lz4Frame(contents[i]) : Zlib(contents[i]));
            }
            else
            {
                w.Write(contents[i]);
            }
            w.Flush();
            stretches.Add(s.ToArray());
        }

        var folderName = Encoding.ASCII.GetBytes(folder + "\0");
        var fileNames = names.Select(n => Encoding.ASCII.GetBytes(n + "\0")).ToList();
        var folderRecordLength = version == BsaVersionType.SSE ? 24 : 16;
        long dataStart = BsaReader.HeaderLength + folderRecordLength
            + 1 + folderName.Length + BsaFileRecord.HeaderLength * contents.Count
            + fileNames.Sum(n => n.Length);

        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write("BSA\0"u8);
        writer.Write((uint)version);
        writer.Write((uint)BsaReader.HeaderLength);
        writer.Write((uint)flags);
        writer.Write(1u); // folders
        writer.Write((uint)contents.Count);
        writer.Write((uint)folderName.Length);
        writer.Write((uint)fileNames.Sum(n => n.Length));
        writer.Write(0u); // file flags
        // folder record
        writer.Write(0x1111UL); // hash
        writer.Write((uint)contents.Count);
        if (version == BsaVersionType.SSE)
        {
            writer.Write(0u);
            writer.Write(0UL);
        }
        else
        {
            writer.Write(0u);
        }
        // file record block
        writer.Write((byte)folderName.Length);
        writer.Write(folderName);
        long offset = dataStart;
        for (int i = 0; i < contents.Count; i++)
        {
            writer.Write(0x2000UL + (ulong)i); // hash
            var size = (uint)stretches[i].Length;
            if (i == 1) size |= 1u << 30;
            writer.Write(size);
            writer.Write((uint)offset);
            offset += stretches[i].Length;
        }
        foreach (var n in fileNames) writer.Write(n);
        stream.Position.ShouldBe(dataStart);
        foreach (var s in stretches) writer.Write(s);
        writer.Flush();
        return (stream.ToArray(), contents, compressed);
    }

    [Theory]
    [InlineData(0x67u, false, false)] // Oblivion
    [InlineData(0x67u, true, false)]
    [InlineData(0x68u, false, false)] // Fallout 3, New Vegas, Skyrim LE
    [InlineData(0x68u, true, false)]
    [InlineData(0x68u, true, true)]
    [InlineData(0x68u, false, true)]
    [InlineData(0x69u, false, false)] // Skyrim SE
    [InlineData(0x69u, true, false)]
    [InlineData(0x69u, true, true)]
    [InlineData(0x69u, false, true)]
    public void Bsa_ChunkReadRawAndInflated_IsTheContent(uint versionNumber, bool compressedByDefault, bool embedNames)
    {
        var version = (BsaVersionType)versionNumber;
        var (bytes, contents, compressed) = BuildBsa(version, compressedByDefault, embedNames);
        var archive = new BsaReader(() => new MemoryStream(bytes));
        var files = archive.Files.ToList();
        files.Count.ShouldBe(contents.Count);
        long expectedOffset = files.Min(f => f.GetStorage()!.Offset);
        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            file.GetBytes().ShouldBe(contents[i]);
            ShouldMatchContent(file, bytes);
            var storage = file.GetStorage()!;
            storage.Offset.ShouldBe(expectedOffset);
            var chunk = storage.Chunks.ShouldHaveSingleItem();
            storage.Compression.ShouldBe(
                compressed[i] ? version == BsaVersionType.SSE ? ArchiveCompression.Lz4Frame : ArchiveCompression.Zlib
                : ArchiveCompression.None);
            // the data ends where the stretch ends; ahead of it, the embedded path and the inflated size
            (chunk.Offset + chunk.StoredSize).ShouldBe(storage.Offset + storage.StoredSize);
            var prefix = (embedNames ? 1 + Encoding.ASCII.GetByteCount($"meshes\\test\\file{i}.txt") : 0) + (compressed[i] ? 4 : 0);
            (chunk.Offset - storage.Offset).ShouldBe(prefix);
            expectedOffset += storage.StoredSize;
        }
    }

    [Fact]
    public void Bsa_Fixture()
    {
        var bytes = File.ReadAllBytes(BsaTests.TestBsa.Path);
        var archive = new BsaReader(() => new MemoryStream(bytes));
        foreach (var file in archive.Files)
        {
            ShouldMatchContent(file, bytes);
        }
        var found = archive.Files.Single(f => f.Path.EndsWith("someotherfile.txt", StringComparison.Ordinal));
        var storage = found.GetStorage()!;
        storage.Offset.ShouldBe(0xB3);
        storage.StoredSize.ShouldBe(8);
        storage.Compression.ShouldBe(ArchiveCompression.None);
    }

    [Fact]
    public void Bsa_StorageBeforeOrAfterReading_IsTheSame()
    {
        var (bytes, _, _) = BuildBsa(BsaVersionType.SSE, true, true);
        var before = new BsaReader(() => new MemoryStream(bytes)).Files.Select(f => f.GetStorage()!).ToList();
        var after = new BsaReader(() => new MemoryStream(bytes)).Files.Select(f =>
        {
            using (f.AsStream()) { }
            return f.GetStorage()!;
        }).ToList();
        after.Select(s => (s.Offset, s.StoredSize, s.Compression, s.Chunks[0])).ShouldBe(
            before.Select(s => (s.Offset, s.StoredSize, s.Compression, s.Chunks[0])));
    }

    // ---- through Archive's readers ----

    [Fact]
    public void DisposableReader_GivesTheSameStorage()
    {
        var (bytes, _) = BuildGeneralBa2(3, 3);
        var dir = Path.Combine(Path.GetTempPath(), "MutagenStorage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test.ba2");
        File.WriteAllBytes(path, bytes);
        try
        {
            var plain = Archive.CreateReader(GameRelease.Starfield, path).Files.Select(f => f.GetStorage()!).ToList();
            using var kept = Archive.CreateDisposableReader(GameRelease.Starfield, path);
            var keptStorage = kept.Files.Select(f => f.GetStorage()!).ToList();
            keptStorage.Select(s => s.Chunks[0]).ShouldBe(plain.Select(s => s.Chunks[0]));
            foreach (var file in kept.Files)
            {
                ShouldMatchContent(file, bytes);
            }
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
