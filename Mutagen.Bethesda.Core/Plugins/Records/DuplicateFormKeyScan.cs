using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.IO.Compression;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Internals;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records.Internals;

namespace Mutagen.Bethesda.Plugins.Records;

/// <summary>
/// Finds records of one plugin that read as the same FormKey, from the record headers only, when a plugin is opened
/// (C204; DuplicateFormKeyOption.Refuse). Two stored FormIDs read as one FormKey when they are equal, or when both read as
/// the plugin itself in different forms (a full index and its FE/FD form, or a master index past the list). Clashes
/// across groups are found too (MissionsGalore.esm: GLOB 03000869 and TMLM 02000869).<br/>
/// Cost: the record headers are read in large blocks (a record's content is only skipped), a big file in parallel by
/// groups; the plugin's own object IDs go in one bit set, its masters' FormIDs in a set, so nothing is kept per record.
/// A file checked once in a process isn't read again while its path, size and write time stay the same.
/// </summary>
internal static class DuplicateFormKeyScan
{
    private const int BlockSize = 1 << 20;
    private const int OwnWords = 1 << 18; // 2^24 bits: every own object ID
    private const long SplitAbove = 1 << 25; // a group bigger than this is scanned by its child groups, in parallel

    // Plugins already checked in this process, by file (full path, size, last write time) and the master counts the
    // check depends on: null when clean, else the pairs
    private static readonly ConcurrentDictionary<string, IReadOnlyList<DuplicateFormKeyPair>?> Checked = new();

    /// <summary>
    /// Throws DuplicateFormKeyException naming every clashing pair, if the plugin file has any. A file checked before in
    /// this process (same path, size and write time, same master counts) isn't read again.
    /// </summary>
    /// <param name="open">Opens the file; called once per thread scanning it</param>
    public static void ThrowIfAny(string path, IFileSystem fileSystem, Func<Stream> open, GameConstants constants,
        IReadOnlySeparatedMasterPackage package, ModKey modKey)
    {
        string? key = null;
        try
        {
            var info = fileSystem.FileInfo.New(path);
            key = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{constants.Release}|{Signature(package)}";
        }
        catch (Exception)
        {
            // Not cached
        }
        if (key == null || !Checked.TryGetValue(key, out var pairs))
        {
            pairs = Find(open, parallel: true, constants, package);
            if (key != null) Checked[key] = pairs;
        }
        if (pairs != null) throw new DuplicateFormKeyException(modKey, pairs);
    }

    /// <summary>
    /// Throws DuplicateFormKeyException naming every clashing pair, if the plugin has any
    /// </summary>
    /// <param name="stream">The plugin, positioned anywhere; its position is restored</param>
    public static void ThrowIfAny(Stream stream, GameConstants constants, IReadOnlySeparatedMasterPackage package, ModKey modKey)
    {
        var start = stream.Position;
        try
        {
            if (Find(() => stream, parallel: false, constants, package) is { } pairs)
            {
                throw new DuplicateFormKeyException(modKey, pairs);
            }
        }
        finally
        {
            stream.Position = start;
        }
    }

    private static string Signature(IReadOnlySeparatedMasterPackage package) =>
        package is SeparatedMasterPackage separated ? separated.DuplicateKeySignature : $"N{package.Raw.Masters.Count}";

    // The FormKey a stored FormID reads as, as a number: a master's FormID as stored, the plugin's own as 1 << 32 + its ID
    private static ulong Key(IReadOnlySeparatedMasterPackage package, uint raw)
    {
        if (package is SeparatedMasterPackage separated) return separated.DuplicateKey(raw);
        var masters = (uint)package.Raw.Masters.Count;
        return (raw >> 24) < masters ? raw : (1UL << 32) | (raw & FormID.FullIdMask);
    }

    /// <summary>
    /// The clashing pairs, or null
    /// </summary>
    private static IReadOnlyList<DuplicateFormKeyPair>? Find(Func<Stream> open, bool parallel, GameConstants constants,
        IReadOnlySeparatedMasterPackage package)
    {
        List<(long Start, long End)> ranges;
        var first = open();
        try
        {
            ranges = Ranges(first, constants, parallel);
        }
        finally
        {
            if (parallel) first.Dispose();
        }

        var own = ArrayPool<long>.Shared.Rent(OwnWords);
        Array.Clear(own, 0, OwnWords);
        var mastersPerRange = new HashSet<uint>?[ranges.Count];
        var clashing = new ConcurrentDictionary<ulong, byte>();
        try
        {
            if (parallel && ranges.Count > 1)
            {
                // One stream and one block per worker thread
                Parallel.For(0, ranges.Count,
                    () => (Stream: open(), Block: ArrayPool<byte>.Shared.Rent(BlockSize)),
                    (i, _, local) =>
                    {
                        mastersPerRange[i] = ScanRange(local.Stream, local.Block, ranges[i], constants, package, own, clashing);
                        return local;
                    },
                    local =>
                    {
                        local.Stream.Dispose();
                        ArrayPool<byte>.Shared.Return(local.Block);
                    });
            }
            else
            {
                var stream = parallel ? open() : first;
                var block = ArrayPool<byte>.Shared.Rent(BlockSize);
                try
                {
                    for (int i = 0; i < ranges.Count; i++)
                    {
                        mastersPerRange[i] = ScanRange(stream, block, ranges[i], constants, package, own, clashing);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(block);
                    if (parallel) stream.Dispose();
                }
            }
        }
        finally
        {
            ArrayPool<long>.Shared.Return(own);
        }

        // Masters' FormIDs: one set; an override found in two ranges is a clash
        HashSet<uint>? masters = null;
        foreach (var set in mastersPerRange)
        {
            if (set == null) continue;
            if (masters == null)
            {
                masters = set;
                continue;
            }
            foreach (var raw in set)
            {
                if (!masters.Add(raw)) clashing.TryAdd(raw, 0);
            }
        }
        if (clashing.IsEmpty) return null;
        var detailStream = parallel ? open() : first;
        try
        {
            return Pairs(detailStream, constants, package, clashing);
        }
        finally
        {
            if (parallel) detailStream.Dispose();
        }
    }

    /// <summary>
    /// Splits the file (after its mod header) into runs of whole records and groups: each top group, and a big group by
    /// its child groups (and the records between them), so a big file can be scanned by several threads
    /// </summary>
    private static List<(long Start, long End)> Ranges(Stream stream, GameConstants constants, bool split)
    {
        var ret = new List<(long, long)>();
        var headerLength = constants.MajorConstants.HeaderLength;
        var header = new byte[headerLength];
        var length = stream.Length;
        stream.Position = 0;
        if (length < headerLength || stream.Read(header, 0, headerLength) != headerLength) return ret;
        var pos = headerLength + (long)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        if (!split)
        {
            if (pos < length) ret.Add((pos, length));
            return ret;
        }
        var bodyStart = pos;
        while (pos + headerLength <= length)
        {
            stream.Position = pos;
            if (stream.Read(header, 0, headerLength) != headerLength) break;
            var end = BinaryPrimitives.ReadInt32LittleEndian(header) == RecordTypes.GRUP.TypeInt
                ? pos + BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4))
                : pos + headerLength + BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
            if (end <= pos) break;
            Split(stream, constants, pos, Math.Min(end, length), ret, depth: 0);
            pos = end;
        }
        // Adjacent runs merged into about four per processor of similar size (what lies between two runs is a group
        // header, which a scan steps into)
        var target = Math.Max(SplitAbove, (length - bodyStart) / (Environment.ProcessorCount * 4));
        var chunks = new List<(long, long)>();
        var chunkStart = bodyStart;
        foreach (var (_, end) in ret)
        {
            if (end - chunkStart >= target)
            {
                chunks.Add((chunkStart, end));
                chunkStart = end;
            }
        }
        if (ret.Count > 0 && ret[^1].Item2 > chunkStart) chunks.Add((chunkStart, ret[^1].Item2));
        return chunks;
    }

    private static void Split(Stream stream, GameConstants constants, long start, long end, List<(long, long)> ranges, int depth)
    {
        var groupHeaderLength = constants.GroupConstants.HeaderLength;
        var headerLength = constants.MajorConstants.HeaderLength;
        if (end - start <= SplitAbove || depth >= 3)
        {
            ranges.Add((start, end));
            return;
        }
        var header = new byte[headerLength];
        // Past the group's own header: its records and child groups
        var pos = start + groupHeaderLength;
        var segmentStart = pos;
        while (pos + headerLength <= end)
        {
            stream.Position = pos;
            if (stream.Read(header, 0, headerLength) != headerLength) break;
            if (BinaryPrimitives.ReadInt32LittleEndian(header) == RecordTypes.GRUP.TypeInt)
            {
                var groupEnd = pos + BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
                if (groupEnd <= pos) break;
                if (pos > segmentStart) ranges.Add((segmentStart, pos));
                Split(stream, constants, pos, Math.Min(groupEnd, end), ranges, depth + 1);
                pos = groupEnd;
                segmentStart = pos;
            }
            else
            {
                pos += headerLength + BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
            }
        }
        if (end > segmentStart) ranges.Add((segmentStart, end));
    }

    /// <summary>
    /// Scans one run of whole records and groups: own object IDs into the shared bit set, masters' FormIDs into the run's
    /// own set (returned), clashes into <paramref name="clashing"/>
    /// </summary>
    private static HashSet<uint>? ScanRange(Stream stream, byte[] block, (long Start, long End) range, GameConstants constants,
        IReadOnlySeparatedMasterPackage package, long[] own, ConcurrentDictionary<ulong, byte> clashing)
    {
        var headerLength = constants.MajorConstants.HeaderLength;
        var groupHeaderLength = constants.GroupConstants.HeaderLength;
        var formIdOffset = constants.MajorConstants.FormIDLocationOffset;
        HashSet<uint>? masters = null;
        {
            long blockStart = 0;
            int blockLength = 0;
            var pos = range.Start;
            while (pos + headerLength <= range.End)
            {
                if (pos < blockStart || pos + headerLength > blockStart + blockLength)
                {
                    stream.Position = pos;
                    blockStart = pos;
                    var want = (int)Math.Min(BlockSize, range.End - pos);
                    blockLength = stream.ReadAtLeast(block.AsSpan(0, want), Math.Min(want, headerLength), throwOnEndOfStream: false);
                    if (blockLength < headerLength) break;
                }
                var header = block.AsSpan(checked((int)(pos - blockStart)), headerLength);
                if (BinaryPrimitives.ReadInt32LittleEndian(header) == RecordTypes.GRUP.TypeInt)
                {
                    pos += groupHeaderLength;
                    continue;
                }
                var next = pos + headerLength + BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(4));
                var key = Key(package, BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(formIdOffset)));
                if ((key >> 32) != 0)
                {
                    var id = (int)(key & FormID.FullIdMask);
                    var bit = 1L << (id & 63);
                    if ((Interlocked.Or(ref own[id >> 6], bit) & bit) != 0) clashing.TryAdd(key, 0);
                }
                else if (!(masters ??= new()).Add((uint)key))
                {
                    clashing.TryAdd(key, 0);
                }
                pos = next;
            }
        }
        return masters;
    }

    /// <summary>
    /// Only for a file with a clash: every clashing record's type, stored FormID and EditorID, paired in file order
    /// </summary>
    private static IReadOnlyList<DuplicateFormKeyPair> Pairs(Stream stream, GameConstants constants,
        IReadOnlySeparatedMasterPackage package, ConcurrentDictionary<ulong, byte> clashing)
    {
        var headerLength = constants.MajorConstants.HeaderLength;
        var groupHeaderLength = constants.GroupConstants.HeaderLength;
        var formIdOffset = constants.MajorConstants.FormIDLocationOffset;
        var found = new List<(ulong Key, long Position, RecordType Type, uint Raw)>();
        var header = new byte[headerLength];
        var length = stream.Length;
        stream.Position = 0;
        if (stream.Read(header, 0, headerLength) != headerLength) return Array.Empty<DuplicateFormKeyPair>();
        var pos = headerLength + (long)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        var block = ArrayPool<byte>.Shared.Rent(BlockSize);
        try
        {
            long blockStart = 0;
            int blockLength = 0;
            while (pos + headerLength <= length)
            {
                if (pos < blockStart || pos + headerLength > blockStart + blockLength)
                {
                    stream.Position = pos;
                    blockStart = pos;
                    blockLength = stream.ReadAtLeast(block.AsSpan(0, BlockSize), headerLength, throwOnEndOfStream: false);
                    if (blockLength < headerLength) break;
                }
                var h = block.AsSpan(checked((int)(pos - blockStart)), headerLength);
                var type = BinaryPrimitives.ReadInt32LittleEndian(h);
                if (type == RecordTypes.GRUP.TypeInt)
                {
                    pos += groupHeaderLength;
                    continue;
                }
                var raw = BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(formIdOffset));
                var key = Key(package, raw);
                if (clashing.ContainsKey(key)) found.Add((key, pos, new RecordType(type), raw));
                pos += headerLength + BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(4));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(block);
        }
        var pairs = new List<DuplicateFormKeyPair>();
        foreach (var group in found.GroupBy(f => f.Key))
        {
            var items = group.ToList();
            for (int i = 1; i < items.Count; i++)
            {
                var a = items[i - 1];
                var b = items[i];
                pairs.Add(new DuplicateFormKeyPair(
                    package.GetFormKey(new FormID(b.Raw), reference: false),
                    a.Type, EditorId(stream, constants, a.Position), a.Raw,
                    b.Type, EditorId(stream, constants, b.Position), b.Raw));
            }
        }
        return pairs;
    }

    private static string? EditorId(Stream stream, GameConstants constants, long recordStart)
    {
        try
        {
            var headerLength = constants.MajorConstants.HeaderLength;
            stream.Position = recordStart;
            var header = new byte[headerLength];
            stream.ReadExactly(header);
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)));
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
            var content = new byte[size];
            stream.ReadExactly(content);
            if ((flags & Constants.CompressedFlag) != 0 && content.Length >= 4)
            {
                using var z = new ZLibStream(new MemoryStream(content, 4, content.Length - 4), CompressionMode.Decompress);
                using var ms = new MemoryStream();
                z.CopyTo(ms);
                content = ms.ToArray();
            }
            var sub = constants.SubConstants.HeaderLength;
            if (content.Length >= sub && BinaryPrimitives.ReadInt32LittleEndian(content) == RecordTypes.EDID.TypeInt)
            {
                int len = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(4));
                return System.Text.Encoding.ASCII.GetString(content, sub, Math.Max(0, Math.Min(len, content.Length - sub))).TrimEnd('\0');
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
