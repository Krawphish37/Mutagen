using Mutagen.Bethesda.Archives.Ba2;
using System.Text;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Archives;

/// <summary>Verifies BA2 file and texture payload reads.</summary>
public class Ba2Tests
{
    /// <summary>A BA2 fixture containing one uncompressed file with bytes 1 through 8.</summary>
    public static readonly FilePath TestBa2 = new(Path.Combine("..", "..", "..", "Archives", "test.ba2"));

    /// <summary>A BC1 texture fixture with an uncompressed mip and a zlib-compressed mip.</summary>
    public static readonly FilePath TestTextureBa2 = new(Path.Combine("..", "..", "..", "Archives", "test-textures.ba2"));

    /// <summary>Reads a general file completely even when the source returns short reads.</summary>
    [Fact]
    public void AsBytes_ShortReads()
    {
        var data = File.ReadAllBytes(TestBa2.Path);
        var archive = new Ba2Reader(() => new ShortReadStream(data));
        byte[] expected = [1, 2, 3, 4, 5, 6, 7, 8];

        var result = archive.Files.Single().GetBytes();

        result.ShouldBe(expected);
    }

    /// <summary>Reads both compressed and uncompressed texture chunks completely.</summary>
    [Fact]
    public void AsBytes_TextureShortReads()
    {
        var data = File.ReadAllBytes(TestTextureBa2.Path);
        var archive = new Ba2Reader(() => new ShortReadStream(data));
        byte[] expected = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];

        var result = archive.Files.Single().GetBytes();

        result.TakeLast(expected.Length).ShouldBe(expected);
    }

    /// <summary>Finds a folder by path prefix, ignoring case, and reads its file.</summary>
    [Fact]
    public void TryGetFolder_FindsFolder()
    {
        var data = BuildGeneralBa2(
            ("textures/a.dds", [1, 2]),
            ("strings/test_en.strings", [3, 4, 5]),
            ("meshes/b.nif", [6]));

        var archive = new Ba2Reader(() => new MemoryStream(data.Bytes));

        archive.TryGetFolder("STRINGS", out var folder).ShouldBeTrue();
        folder.Path.ShouldBe("STRINGS");
        var file = folder.Files.ShouldHaveSingleItem();
        file.Path.ShouldBe("strings/test_en.strings");
        file.GetBytes().ShouldBe(new byte[] { 3, 4, 5 });
    }

    /// <summary>Reports a missing folder, and still lists every file afterwards.</summary>
    [Fact]
    public void TryGetFolder_MissingFolder()
    {
        var data = BuildGeneralBa2(
            ("textures/a.dds", [1, 2]),
            ("meshes/b.nif", [6]));

        var archive = new Ba2Reader(() => new MemoryStream(data.Bytes));

        archive.TryGetFolder("strings", out _).ShouldBeFalse();
        archive.Files.Select(f => f.Path).ShouldBe(["textures/a.dds", "meshes/b.nif"]);
        archive.TryGetFolder("meshes", out var folder).ShouldBeTrue();
        folder.Files.ShouldHaveSingleItem().GetBytes().ShouldBe(new byte[] { 6 });
    }

    /// <summary>
    /// Looking for a folder the archive does not contain only scans the name table.  The file
    /// records are not parsed, so skipping the texture archives when looking for strings is cheap.
    /// </summary>
    [Fact]
    public void TryGetFolder_MissingFolder_DoesNotReadFileRecords()
    {
        var data = BuildGeneralBa2(
            ("textures/a.dds", [1, 2]),
            ("meshes/b.nif", [6]));
        var streams = new List<ReadTrackingStream>();

        var archive = new Ba2Reader(() =>
        {
            var stream = new ReadTrackingStream(data.Bytes);
            streams.Add(stream);
            return stream;
        });
        archive.TryGetFolder("strings", out _).ShouldBeFalse();

        streams.SelectMany(s => s.Reads)
            .Where(r => r.Start < data.FileRecordsEnd && r.Start + r.Length > data.FileRecordsStart)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// A reader that is kept and asked again and again for folders it does not contain scans the
    /// name table at most once.  Later lookups parse the records once and then answer from memory.
    /// </summary>
    [Fact]
    public void TryGetFolder_RepeatedMisses_ScanNameTableOnce()
    {
        var data = BuildGeneralBa2(
            ("textures/a.dds", [1, 2]),
            ("meshes/b.nif", [6]));
        var streams = new List<ReadTrackingStream>();

        var archive = new Ba2Reader(() =>
        {
            var stream = new ReadTrackingStream(data.Bytes);
            streams.Add(stream);
            return stream;
        });
        for (var i = 0; i < 5; i++)
        {
            archive.TryGetFolder($"strings{i}", out _).ShouldBeFalse();
        }
        archive.TryGetFolder("meshes", out var folder).ShouldBeTrue();
        folder.Files.ShouldHaveSingleItem().Path.ShouldBe("meshes/b.nif");

        bool ReadsNameTable(ReadTrackingStream s) =>
            s.Reads.Any(r => r.Length > 0 && r.Start + r.Length > data.NameTableStart);
        bool ReadsFileRecords(ReadTrackingStream s) =>
            s.Reads.Any(r => r.Start < data.FileRecordsEnd && r.Start + r.Length > data.FileRecordsStart);
        streams.Count(s => ReadsNameTable(s) && !ReadsFileRecords(s)).ShouldBeLessThanOrEqualTo(1);
        streams.Count(ReadsNameTable).ShouldBeLessThanOrEqualTo(2);
    }

    /// <summary>A hand-built version 1 GNRL archive, and where its file records and name table lie.</summary>
    private record Ba2Bytes(byte[] Bytes, long FileRecordsStart, long FileRecordsEnd, long NameTableStart);

    /// <summary>Builds a version 1 GNRL archive with uncompressed files and a name table.</summary>
    private static Ba2Bytes BuildGeneralBa2(params (string Path, byte[] Data)[] files)
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
            writer.Write(0u); // name hash
            writer.Write(Encoding.ASCII.GetBytes(Path.GetExtension(file.Path).TrimStart('.').PadRight(4, (char)0)[..4]));
            writer.Write(0u); // directory hash
            writer.Write(0u); // flags
            writer.Write((ulong)dataOffset);
            writer.Write(0u); // packed size: 0 means uncompressed
            writer.Write((uint)file.Data.Length);
            writer.Write(0xBAADF00Du); // align
            dataOffset += file.Data.Length;
        }
        foreach (var file in files)
        {
            writer.Write(file.Data);
        }
        foreach (var file in files)
        {
            var name = Encoding.UTF8.GetBytes(file.Path);
            writer.Write((short)name.Length);
            writer.Write(name);
        }
        writer.Flush();
        return new Ba2Bytes(stream.ToArray(), headerLength, fileRecordsEnd, nameTableOffset);
    }
}

/// <summary>An archive test stream that records the position and length of every read.</summary>
internal sealed class ReadTrackingStream(byte[] data) : MemoryStream(data)
{
    /// <summary>The position and number of bytes of each read.</summary>
    public List<(long Start, int Length)> Reads { get; } = new();

    /// <summary>Records a read through the array overload.</summary>
    public override int Read(byte[] buffer, int offset, int count)
    {
        var start = Position;
        var read = base.Read(buffer, offset, count);
        Reads.Add((start, read));
        return read;
    }

    /// <summary>Records a read through the span overload.</summary>
    public override int Read(Span<byte> buffer)
    {
        var start = Position;
        var read = base.Read(buffer);
        Reads.Add((start, read));
        return read;
    }

    /// <summary>Records a single byte read.</summary>
    public override int ReadByte()
    {
        var start = Position;
        var read = base.ReadByte();
        Reads.Add((start, read < 0 ? 0 : 1));
        return read;
    }
}

/// <summary>A seekable archive test stream that returns at most two bytes on each read.</summary>
internal sealed class ShortReadStream(byte[] data) : MemoryStream(data)
{
    /// <summary>Returns at most two bytes through the array overload.</summary>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return base.Read(buffer, offset, Math.Min(count, 2));
    }

    /// <summary>Returns at most two bytes through the span overload.</summary>
    public override int Read(Span<byte> buffer)
    {
        return base.Read(buffer[..Math.Min(buffer.Length, 2)]);
    }
}
