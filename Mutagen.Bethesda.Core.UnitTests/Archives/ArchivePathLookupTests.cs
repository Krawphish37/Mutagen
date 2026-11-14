using System.Diagnostics.CodeAnalysis;
using System.Text;
using Mutagen.Bethesda.Archives;
using Mutagen.Bethesda.Archives.Ba2;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Archives;

/// <summary>
/// C201: listing an archive's paths, and finding one file by path, without building every file. A BA2 lists its paths
/// from its name table alone and reads only the one file record it is asked for; a BSA looks only in the file's folder;
/// any other reader answers through Files. The answers are Files' answers.
/// </summary>
public class ArchivePathLookupTests
{
    private record Ba2Bytes(byte[] Bytes, long FileRecordsStart, long FileRecordsEnd, long NameTableStart);

    /// <summary>A version 1 GNRL archive with stored files and a name table of the given raw names</summary>
    private static Ba2Bytes BuildGeneralBa2(params (byte[] Name, byte[] Data)[] files)
    {
        const int headerLength = 24;
        const int recordLength = 36;
        var fileRecordsEnd = headerLength + recordLength * files.Length;
        var nameTableOffset = fileRecordsEnd + files.Sum(f => f.Data.Length);
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write("BTDX"u8);
        writer.Write(1u);
        writer.Write("GNRL"u8);
        writer.Write((uint)files.Length);
        writer.Write((ulong)nameTableOffset);
        long dataOffset = fileRecordsEnd;
        foreach (var file in files)
        {
            writer.Write(0u);
            writer.Write("bin\0"u8);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write((ulong)dataOffset);
            writer.Write(0u);
            writer.Write((uint)file.Data.Length);
            writer.Write(0xBAADF00Du);
            dataOffset += file.Data.Length;
        }
        foreach (var file in files) writer.Write(file.Data);
        foreach (var file in files)
        {
            writer.Write((short)file.Name.Length);
            writer.Write(file.Name);
        }
        writer.Flush();
        return new Ba2Bytes(stream.ToArray(), headerLength, fileRecordsEnd, nameTableOffset);
    }

    private static readonly (byte[] Name, byte[] Data)[] Files =
    {
        ("textures/a.dds"u8.ToArray(), new byte[] { 1, 2 }),
        ("meshes/b.nif"u8.ToArray(), new byte[] { 3 }),
        ("Strings/Test_en.STRINGS"u8.ToArray(), new byte[] { 4, 5, 6 }),
        // Replaced by FilesWithRawUmlaut with a name that isn't UTF-8
        ("textures/sky/placeholder.dds"u8.ToArray(), new byte[] { 7 }),
        ("meshes/c.nif"u8.ToArray(), new byte[] { 8, 9 }),
    };

    /// <summary>
    /// <see cref="Files"/> with a name that isn't UTF-8 (a Windows-1252 o-umlaut), as in ProjectRework-Akila -
    /// Textures.ba2: Files reads it as U+FFFD
    /// </summary>
    private static (byte[] Name, byte[] Data)[] FilesWithRawUmlaut()
    {
        var ret = Files.ToArray();
        var name = Encoding.ASCII.GetBytes("textures/sky/xi bo#tis.dds");
        name[Array.IndexOf(name, (byte)'#')] = 0xF6;
        ret[3] = (name, ret[3].Data);
        return ret;
    }

    [Fact]
    public void Ba2_PathsAndLookups_AreFilesAnswers_WithoutReadingEveryRecord()
    {
        var data = BuildGeneralBa2(FilesWithRawUmlaut());
        var streams = new List<ReadTrackingStream>();
        Ba2Reader Reader() => new(() =>
        {
            var s = new ReadTrackingStream(data.Bytes);
            streams.Add(s);
            return s;
        });
        var expected = Reader().Files.ToList();
        var expectedPaths = expected.Select(f => f.Path).ToList();
        expectedPaths[3].ShouldContain('�');

        // Listing and "contains": the name table only, no file record read
        streams.Clear();
        var reader = Reader();
        reader.EnumerateFilePaths().ShouldBe(expectedPaths);
        reader.ContainsFile("MESHES\\B.NIF").ShouldBeTrue();
        reader.ContainsFile("meshes/missing.nif").ShouldBeFalse();
        reader.ContainsFile(expectedPaths[3]).ShouldBeTrue();
        streams.SelectMany(s => s.Reads)
            .Where(r => r.Length > 0 && r.Start < data.FileRecordsEnd && r.Start + r.Length > data.FileRecordsStart)
            .ShouldBeEmpty();

        // One file: its record (and the first, for the record length), same path and bytes as Files' file
        for (int i = 0; i < expected.Count; i++)
        {
            foreach (var query in new[] { expectedPaths[i], expectedPaths[i].ToUpperInvariant(), expectedPaths[i].Replace('/', '\\') })
            {
                streams.Clear();
                var fresh = Reader();
                fresh.TryGetFile(query, out var file).ShouldBeTrue(query);
                file.Path.ShouldBe(expectedPaths[i]);
                file.GetBytes().ShouldBe(expected[i].GetBytes());
                var recordsRead = streams.SelectMany(s => s.Reads)
                    .Where(r => r.Length > 0 && r.Start < data.FileRecordsEnd && r.Start + r.Length > data.FileRecordsStart)
                    .Select(r => (int)((r.Start - data.FileRecordsStart) / 36))
                    .Distinct()
                    .ToList();
                recordsRead.ShouldBeSubsetOf(new[] { 0, i });
            }
        }
        Reader().TryGetFile("meshes/missing.nif", out _).ShouldBeFalse();
    }

    [Fact]
    public void Ba2_Textures_TryGetFile_ReadsAsFiles()
    {
        var reader = Archive.CreateReader(GameRelease.Fallout4, Ba2Tests.TestTextureBa2);
        var expected = Archive.CreateReader(GameRelease.Fallout4, Ba2Tests.TestTextureBa2).Files.ToList();
        reader.EnumerateFilePaths().ShouldBe(expected.Select(f => f.Path));
        foreach (var f in expected)
        {
            Archive.CreateReader(GameRelease.Fallout4, Ba2Tests.TestTextureBa2).TryGetFile(f.Path, out var got).ShouldBeTrue();
            got.GetBytes().ShouldBe(f.GetBytes());
        }
    }

    [Fact]
    public void Bsa_TryGetFile_LooksInTheFolder()
    {
        var expected = Archive.CreateReader(GameRelease.SkyrimSE, BsaTests.TestBsa).Files.ToList();
        expected.ShouldNotBeEmpty();
        var reader = Archive.CreateReader(GameRelease.SkyrimSE, BsaTests.TestBsa);
        reader.EnumerateFilePaths().ShouldBe(expected.Select(f => f.Path));
        foreach (var f in expected)
        {
            foreach (var query in new[] { f.Path, f.Path.ToUpperInvariant(), f.Path.Replace('\\', '/') })
            {
                reader.TryGetFile(query, out var got).ShouldBeTrue(query);
                got.Path.ShouldBe(f.Path);
                got.GetBytes().ShouldBe(f.GetBytes());
                reader.ContainsFile(query).ShouldBeTrue();
            }
        }
        reader.ContainsFile(Path.Combine("derp", "no_such_file.txt")).ShouldBeFalse();
        reader.ContainsFile(Path.Combine("no_such_folder", "someotherfile.txt")).ShouldBeFalse();
    }

    private sealed class FilesOnlyReader(IArchiveReader inner) : IArchiveReader
    {
        public bool TryGetFolder(string path, [MaybeNullWhen(false)] out IArchiveFolder folder) => inner.TryGetFolder(path, out folder);
        public IEnumerable<IArchiveFile> Files => inner.Files;
    }

    [Fact]
    public void OtherReaders_AnswerThroughFiles()
    {
        IArchiveReader reader = new FilesOnlyReader(Archive.CreateReader(GameRelease.SkyrimSE, BsaTests.TestBsa));
        var first = reader.Files.First();
        reader.EnumerateFilePaths().ShouldBe(reader.Files.Select(f => f.Path));
        reader.TryGetFile(first.Path.ToUpperInvariant(), out var got).ShouldBeTrue();
        got.Path.ShouldBe(first.Path);
        reader.ContainsFile("nothing/here.txt").ShouldBeFalse();
    }

    [Fact]
    public void DisposableReader_PassesThrough()
    {
        var path = Path.Combine(Path.GetTempPath(), "MutagenPathLookup-" + Guid.NewGuid().ToString("N") + ".ba2");
        File.WriteAllBytes(path, BuildGeneralBa2(FilesWithRawUmlaut()).Bytes);
        try
        {
            using var kept = Archive.CreateDisposableReader(GameRelease.Fallout4, path);
            kept.EnumerateFilePaths().Count().ShouldBe(5);
            kept.ContainsFile("meshes/c.nif").ShouldBeTrue();
            kept.TryGetFile("MESHES/C.NIF", out var file).ShouldBeTrue();
            file.GetBytes().ShouldBe(new byte[] { 8, 9 });
        }
        finally
        {
            File.Delete(path);
        }
    }
}
