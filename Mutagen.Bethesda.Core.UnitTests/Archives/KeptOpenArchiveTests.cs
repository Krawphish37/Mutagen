using System.IO.Abstractions.TestingHelpers;
using System.Text;
using ICSharpCode.SharpZipLib.Zip.Compression;
using Mutagen.Bethesda.Archives;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Archives;

/// <summary>
/// C199: Archive.CreateDisposableReader keeps the archive open until disposed and reads every entry through that one
/// file by position (several threads at once), where Archive.CreateReader opens the archive for each entry and holds
/// nothing open. Both readers must give the same bytes; only the disposable one holds the file.
/// </summary>
public class KeptOpenArchiveTests
{
    private static byte[] Zlib(byte[] data)
    {
        var deflater = new Deflater();
        deflater.SetInput(data);
        deflater.Finish();
        var buffer = new byte[data.Length + 256];
        var length = deflater.Deflate(buffer);
        return buffer[..length];
    }

    /// <summary>A Fallout 4 GNRL archive: 24 entries, every third stored, the others zlib, each with its own content</summary>
    private static (byte[] Archive, List<byte[]> Contents) BuildArchive()
    {
        var contents = Enumerable.Range(0, 24)
            .Select(i => Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat($"entry {i} BSTriShape NiNode ", 20 + i * 7))))
            .ToList();
        var stored = contents.Select((c, i) => i % 3 == 0 ? c : Zlib(c)).ToList();
        const int headerLength = 24;
        long dataStart = headerLength + 36L * contents.Count;
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write("BTDX"u8);
        writer.Write(1u);
        writer.Write("GNRL"u8);
        writer.Write((uint)contents.Count);
        writer.Write(0UL); // no name table
        long offset = dataStart;
        for (int i = 0; i < contents.Count; i++)
        {
            writer.Write(0x1000u + (uint)i); // name hash
            writer.Write("nif\0"u8);
            writer.Write(0x5678u); // directory hash
            writer.Write(0u); // flags
            writer.Write((ulong)offset);
            writer.Write(i % 3 == 0 ? 0u : (uint)stored[i].Length);
            writer.Write((uint)contents[i].Length);
            writer.Write(0xBAADF00Du);
            offset += stored[i].Length;
        }
        foreach (var s in stored) writer.Write(s);
        writer.Write(new byte[8192]); // more of the file after the last entry
        writer.Flush();
        return (stream.ToArray(), contents);
    }

    private static List<byte[]> ReadAll(IArchiveReader reader, int piece = 4096)
    {
        return reader.Files.Select(f =>
        {
            using var s = f.AsStream();
            var ms = new MemoryStream();
            var buffer = new byte[piece];
            int n;
            while ((n = s.Read(buffer, 0, buffer.Length)) > 0) ms.Write(buffer, 0, n);
            return ms.ToArray();
        }).ToList();
    }

    private static string TempArchive(byte[] bytes)
    {
        var dir = Path.Combine(Path.GetTempPath(), "MutagenKeptOpen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test.ba2");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void CleanUp(string path)
    {
        try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Disposable_ReadsTheSameBytes_AndHoldsTheFileUntilDisposed()
    {
        var (bytes, contents) = BuildArchive();
        var path = TempArchive(bytes);
        try
        {
            // Default reader: nothing stays open, the archive can be overwritten between and after reads
            var plain = Archive.CreateReader(GameRelease.Fallout4, path);
            ReadAll(plain).ShouldBe(contents);
            File.WriteAllBytes(path, bytes);
            ReadAll(plain, piece: 100).ShouldBe(contents);

            var kept = Archive.CreateDisposableReader(GameRelease.Fallout4, path);
            ReadAll(kept).ShouldBe(contents);
            ReadAll(kept, piece: 1).Select(c => c.Length).ShouldBe(contents.Select(c => c.Length));
            kept.Files.Select(f => f.GetBytes()).ShouldBe(contents);
            // held: it can't be overwritten while the reader is open
            Should.Throw<IOException>(() => File.WriteAllBytes(path, bytes));
            kept.Dispose();
            Should.Throw<ObjectDisposedException>(() => kept.Files.First().GetBytes());
            File.WriteAllBytes(path, bytes);
        }
        finally
        {
            CleanUp(path);
        }
    }

    [Fact]
    public void Disposable_FourThreadsReadTheSameBytes()
    {
        var (bytes, contents) = BuildArchive();
        var path = TempArchive(bytes);
        try
        {
            using var kept = Archive.CreateDisposableReader(GameRelease.Fallout4, path);
            var files = kept.Files.ToList();
            var results = new byte[200][];
            Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
            {
                using var s = files[i % files.Count].AsStream();
                var ms = new MemoryStream();
                var buffer = new byte[1 + i % 700];
                int n;
                while ((n = s.Read(buffer, 0, buffer.Length)) > 0) ms.Write(buffer, 0, n);
                results[i] = ms.ToArray();
            });
            for (int i = 0; i < results.Length; i++) results[i].ShouldBe(contents[i % contents.Count], $"read {i}");
        }
        finally
        {
            CleanUp(path);
        }
    }

    [Fact]
    public void Disposable_OnAnotherFileSystem_ReadsUnderALock()
    {
        var (bytes, contents) = BuildArchive();
        var fs = new MockFileSystem(new Dictionary<string, MockFileData> { [@"C:\Data\test.ba2"] = new(bytes) });
        using var kept = Archive.CreateDisposableReader(GameRelease.Fallout4, @"C:\Data\test.ba2", fs);
        var files = kept.Files.ToList();
        var results = new byte[100][];
        Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i => results[i] = files[i % files.Count].GetBytes());
        for (int i = 0; i < results.Length; i++) results[i].ShouldBe(contents[i % contents.Count]);
    }

    [Fact]
    public void Disposable_Bsa_ReadsAsTheDefaultReader()
    {
        var plain = Archive.CreateReader(GameRelease.SkyrimSE, BsaTests.TestBsa);
        using var kept = Archive.CreateDisposableReader(GameRelease.SkyrimSE, BsaTests.TestBsa);
        kept.Files.Select(f => f.Path).ShouldBe(plain.Files.Select(f => f.Path));
        ReadAll(kept).ShouldBe(ReadAll(plain));
        kept.TryGetFolder(plain.Files.First().Path.Contains('\\') ? Path.GetDirectoryName(plain.Files.First().Path)! : "", out _)
            .ShouldBe(plain.TryGetFolder(plain.Files.First().Path.Contains('\\') ? Path.GetDirectoryName(plain.Files.First().Path)! : "", out _));
    }
}
