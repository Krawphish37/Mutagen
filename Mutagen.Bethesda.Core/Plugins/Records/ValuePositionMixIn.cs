using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Loqui;
using Mutagen.Bethesda.Plugins.Binary;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Internals;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records.Internals;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Records;

/// <summary>
/// What a value position holds
/// </summary>
public enum ValuePositionKind
{
    /// <summary>A value read from bytes: its own bytes (with <see cref="ValuePosition.BitMask"/>, some of their bits)</summary>
    Value,

    /// <summary>A value given by a subrecord being there (a marker): the subrecord, no bytes</summary>
    Presence,

    /// <summary>An object (a subrecord object, a gendered pair, a list item that is an object): what it read</summary>
    Object,

    /// <summary>A list, array or dictionary: what it read, its items after it</summary>
    List,

    /// <summary>The stored count of a list (a counter subrecord, or a count before the items)</summary>
    Count,

    /// <summary>
    /// What one hand-written reader step read (a CustomLogic step, a binary="Custom" field, a customBinaryEnd), named by
    /// the step. The fields it fills are listed on the step in the record XML (fills=); those the reader notes have their
    /// own positions too.
    /// </summary>
    HandRead,
}

/// <summary>
/// One subrecord of a record's content
/// </summary>
/// <param name="Type">Its type</param>
/// <param name="Index">Its place among the record's subrecords, from 0 (an XXXX overflow header counts with the subrecord
/// it is for, not as its own)</param>
/// <param name="Turn">Its turn among the record's subrecords of its type, from 0</param>
/// <param name="HeaderStart">Where it starts in <see cref="RecordValuePositions.Bytes"/> (an XXXX before it included)</param>
/// <param name="ContentStart">Where its content starts</param>
/// <param name="ContentLength">The length of its content</param>
public sealed record RecordSubrecord(RecordType Type, int Index, int Turn, int HeaderStart, int ContentStart, int ContentLength)
{
    /// <summary>The first position after it</summary>
    public int End => ContentStart + ContentLength;
}

/// <summary>
/// Where one value of a record was read (C60)
/// </summary>
/// <param name="Path">The value: the compiled property names from the record, joined by '.', with [i] for a list item,
/// [Key] for a dictionary entry, [x,y] for a 2D array cell; a gendered pair's halves are .Male and .Female.
/// A <see cref="ValuePositionKind.HandRead"/> entry is named by its step instead of a property.</param>
/// <param name="Kind">What it holds</param>
/// <param name="Start">Where its bytes start in <see cref="RecordValuePositions.Bytes"/>. A value: its own bytes; an
/// object, list or step: from the first to the last byte it read, the headers of the subrecords it read included.</param>
/// <param name="Length">How many bytes</param>
/// <param name="Subrecord">The subrecord the bytes are in (a container's: the one it starts in); null in the record header</param>
/// <param name="SubrecordIndex">That subrecord's <see cref="RecordSubrecord.Index"/>; -1 in the record header</param>
/// <param name="SubrecordTurn">That subrecord's <see cref="RecordSubrecord.Turn"/>; -1 in the record header</param>
/// <param name="Offset">Where the bytes start in that subrecord's content (in the record header: from its start)</param>
/// <param name="SubrecordCount">How many subrecords the bytes reach into (1 for a value)</param>
/// <param name="BitMask">0: all of the bytes. Otherwise the value is these bits of the little-endian value of the bytes
/// (a byte that holds two values)</param>
/// <param name="Converted">The reader computes the value from these bytes rather than taking them as they are (a scaled
/// or inverted value, a chance stored as a byte of 255, an index it looks up): the bytes are the value's, but reading them
/// as the property's type doesn't give the value</param>
public sealed record ValuePosition(
    string Path,
    ValuePositionKind Kind,
    int Start,
    int Length,
    RecordType? Subrecord,
    int SubrecordIndex,
    int SubrecordTurn,
    int Offset,
    int SubrecordCount,
    ulong BitMask,
    bool Converted = false);

/// <summary>
/// Where a record's values were read, as the reader read them (C60)
/// </summary>
public sealed class RecordValuePositions
{
    private Dictionary<string, List<ValuePosition>>? _byPath;

    /// <summary>The record as it was read while its positions were noted (a full-import object)</summary>
    public IMajorRecord Record { get; }

    /// <summary>
    /// The bytes the positions are in: the record header as stored, then the content (decompressed when the record is
    /// stored compressed)
    /// </summary>
    public ReadOnlyMemorySlice<byte> Bytes { get; }

    /// <summary>The length of the record header at the start of <see cref="Bytes"/></summary>
    public int HeaderLength { get; }

    /// <summary>The record is stored compressed: the positions of its content are in its decompressed form</summary>
    public bool Compressed { get; }

    /// <summary>The record's subrecords, in order</summary>
    public IReadOnlyList<RecordSubrecord> Subrecords { get; }

    /// <summary>Every position, in the order the reader read them: an object or list before what it holds</summary>
    public IReadOnlyList<ValuePosition> Positions { get; }

    /// <summary>
    /// Values the reader noted at a place outside the record's bytes (none are expected): their paths, so nothing noted is
    /// silently left out
    /// </summary>
    public IReadOnlyList<string> Unplaced { get; }

    internal RecordValuePositions(
        IMajorRecord record,
        ReadOnlyMemorySlice<byte> bytes,
        int headerLength,
        bool compressed,
        IReadOnlyList<RecordSubrecord> subrecords,
        IReadOnlyList<ValuePosition> positions,
        IReadOnlyList<string> unplaced)
    {
        Record = record;
        Bytes = bytes;
        HeaderLength = headerLength;
        Compressed = compressed;
        Subrecords = subrecords;
        Positions = positions;
        Unplaced = unplaced;
    }

    /// <summary>The positions of one path (a value read twice has two; a list has its List and its Count)</summary>
    public IReadOnlyList<ValuePosition> this[string path] =>
        ByPath.TryGetValue(path, out var list) ? list : Array.Empty<ValuePosition>();

    /// <summary>The positions by path</summary>
    public IReadOnlyDictionary<string, List<ValuePosition>> ByPath
    {
        get
        {
            if (_byPath != null) return _byPath;
            var ret = new Dictionary<string, List<ValuePosition>>();
            foreach (var p in Positions)
            {
                if (!ret.TryGetValue(p.Path, out var list))
                {
                    ret[p.Path] = list = new List<ValuePosition>(1);
                }
                list.Add(p);
            }
            return _byPath = ret;
        }
    }

    /// <summary>The bytes of a position</summary>
    public ReadOnlyMemorySlice<byte> Slice(ValuePosition position) => Bytes.Slice(position.Start, position.Length);
}

/// <summary>
/// Where a record's values sit in its bytes (C60). The record is read by Mutagen's full-import reader, which notes where
/// it reads each value (the generated readers, the list and gendered translations and the hand-written readers alike),
/// so the positions are the reader's own and can't fall behind it. Nothing is noted unless asked for.
/// </summary>
public static class ValuePositionMixIn
{
    /// <summary>
    /// Where a record's values sit in its bytes. The record must come from a binary overlay (it keeps its bytes); its bytes
    /// are read again by the full-import reader, noting positions. A record from a full import keeps no bytes: read it
    /// with <see cref="ReadValuePositionsAt"/> from a <see cref="MajorRecordReader"/> on its plugin.<br/>
    /// Only the record's own bytes are covered, not the records of its child groups.
    /// </summary>
    public static RecordValuePositions GetValuePositions(this IMajorRecordGetter record)
    {
        if (record is not PluginBinaryOverlay overlay)
        {
            throw new ArgumentException(
                $"{record} doesn't come from a binary overlay, so its bytes aren't kept. Read it with MajorRecordReader.ReadValuePositionsAt.",
                nameof(record));
        }
        var meta = overlay._package.MetaData;
        var constants = meta.Constants;
        var creator = GetCreator(record.Registration.ClassType);
        var structData = overlay._structData;
        var recordData = overlay._recordData;
        var typeAndLength = constants.MajorConstants.TypeAndLengthLength;
        var bytes = new byte[typeAndLength + structData.Length + recordData.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, creator.RecordType.TypeInt);
        // An overlay holds a compressed record's content decompressed: the header's length is the decompressed one
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)recordData.Length));
        structData.Span.CopyTo(bytes.AsSpan(typeAndLength));
        recordData.Span.CopyTo(bytes.AsSpan(typeAndLength + structData.Length));
        var parseMeta = meta.CloneForParallelParse();
        parseMeta.Parallel = false;
        parseMeta.FormVersion = null;
        return Read(
            bytes,
            contentDecompressed: true,
            read: () => creator.Create(new MutagenFrame(new MutagenMemoryReadStream(bytes, parseMeta))),
            position: 0);
    }

    /// <summary>
    /// Reads one record from its bytes (as <see cref="MajorRecordReader.Read(ReadOnlyMemorySlice{byte}, long)"/> does), noting where each value sits
    /// </summary>
    /// <param name="reader">A reader opened on the record's plugin</param>
    /// <param name="bytes">The record's bytes, starting with its header. A child group after it isn't covered.</param>
    /// <param name="position">Where the bytes start in the file (positions stay relative to the bytes)</param>
    public static RecordValuePositions ReadValuePositions(this MajorRecordReader reader, ReadOnlyMemorySlice<byte> bytes, long position = 0)
    {
        return Read(
            bytes,
            contentDecompressed: false,
            read: () => reader.Read(bytes, position),
            position: position);
    }

    /// <summary>
    /// Reads the record whose header starts at a position of the file, noting where each value sits
    /// </summary>
    public static RecordValuePositions ReadValuePositionsAt(this MajorRecordReader reader, long position)
    {
        return reader.ReadValuePositions(reader.ReadBytesAt(position), position);
    }

    private static RecordValuePositions Read(
        ReadOnlyMemorySlice<byte> bytes,
        bool contentDecompressed,
        Func<IMajorRecord> read,
        long position)
    {
        var recording = ValuePositionRecorder.Start(contentDecompressed);
        IMajorRecord record;
        try
        {
            record = read();
        }
        finally
        {
            ValuePositionRecorder.Stop(recording);
        }
        return Build(record, bytes, recording, position);
    }

    private static RecordValuePositions Build(
        IMajorRecord record,
        ReadOnlyMemorySlice<byte> given,
        ValuePositionRecorder.Recording recording,
        long position)
    {
        if (recording.RecordStart is not { } recordStart || recording.ContentStart is not { } contentStart)
        {
            throw new InvalidOperationException($"The read of {record} didn't go through a major record's reader");
        }
        var headerLength = checked((int)(contentStart - recordStart));
        var headerStart = checked((int)(recordStart - position));
        var storedContentLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(given.Span.Slice(headerStart + 4)));
        var compressed = recording.Decompressed != null || (recording.ContentDecompressed && IsCompressedHeader(given.Span.Slice(headerStart)));
        ReadOnlyMemorySlice<byte> bytes;
        if (recording.Decompressed is { } decompressed)
        {
            var buf = new byte[headerLength + decompressed.Length];
            given.Span.Slice(headerStart, headerLength).CopyTo(buf);
            decompressed.CopyTo(buf.AsSpan(headerLength));
            bytes = buf;
        }
        else
        {
            bytes = given.Slice(headerStart, headerLength + storedContentLength);
        }

        var subrecords = Subrecords(bytes, headerLength);
        var nodes = recording.Nodes;
        var prefixes = new string[nodes.Count];
        var positions = new List<ValuePosition>(nodes.Count);
        var unplaced = new List<string>();
        var dropped = new bool[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            var parentPrefix = n.Parent >= 0 ? prefixes[n.Parent] : string.Empty;
            dropped[i] = n.Dropped || (n.Parent >= 0 && dropped[n.Parent]);
            string path = n.Kind switch
            {
                ValuePositionRecorder.NodeKind.Item => $"{parentPrefix}[{(n.Name ?? n.Index.ToString())}]",
                ValuePositionRecorder.NodeKind.Count => parentPrefix,
                _ => Join(parentPrefix, n.Name!),
            };
            // A hand-written step is transparent: what it notes is named relative to the object, like the object's fields
            prefixes[i] = n.Kind == ValuePositionRecorder.NodeKind.HandRead ? parentPrefix : path;
            if (dropped[i]) continue;

            var start = n.Start - recordStart;
            var end = (n.End < 0 ? n.Start : n.End) - recordStart;
            if (start < 0 || end > bytes.Length || end < start)
            {
                unplaced.Add(path);
                continue;
            }
            var leaf = n.Kind switch
            {
                ValuePositionRecorder.NodeKind.Field or ValuePositionRecorder.NodeKind.Item => !n.HasChildren,
                ValuePositionRecorder.NodeKind.Count or ValuePositionRecorder.NodeKind.At => true,
                _ => false,
            };
            var kind = n.Kind switch
            {
                ValuePositionRecorder.NodeKind.List => ValuePositionKind.List,
                ValuePositionRecorder.NodeKind.HandRead => ValuePositionKind.HandRead,
                ValuePositionRecorder.NodeKind.Count => ValuePositionKind.Count,
                _ => leaf ? ValuePositionKind.Value : ValuePositionKind.Object,
            };
            if (kind == ValuePositionKind.HandRead && end == start && !n.HasChildren) continue;
            var place = Place(path, kind, leaf, (int)start, (int)end, n.BitMask, headerLength, subrecords);
            // An item of a list of converted values is converted too
            var converted = n.Converted
                || (leaf && n.Kind == ValuePositionRecorder.NodeKind.Item && n.Parent >= 0
                    && nodes[n.Parent] is { Kind: ValuePositionRecorder.NodeKind.List, Converted: true });
            positions.Add(converted ? place with { Converted = true } : place);
        }
        return new RecordValuePositions(record, bytes, headerLength, compressed, subrecords, positions, unplaced);
    }

    private static bool IsCompressedHeader(ReadOnlySpan<byte> header)
    {
        // The major record flags follow the type and length in every game's record header
        return (BinaryPrimitives.ReadInt32LittleEndian(header.Slice(8)) & Mutagen.Bethesda.Plugins.Internals.Constants.CompressedFlag) != 0;
    }

    private static string Join(string prefix, string name) =>
        prefix.Length == 0 ? name : name.StartsWith('[') ? prefix + name : $"{prefix}.{name}";

    /// <summary>
    /// A span of the record's bytes as a position: in the header, or in the subrecord(s) it reaches. A span that starts at
    /// a subrecord's header starts at its content (the header isn't the value); a value's subrecords that it only passed
    /// over (a marker before its own subrecord) are left out.
    /// </summary>
    private static ValuePosition Place(string path, ValuePositionKind kind, bool leaf, int start, int end, ulong bitMask,
        int headerLength, IReadOnlyList<RecordSubrecord> subrecords)
    {
        if (start < headerLength)
        {
            return new ValuePosition(path, kind, start, end - start, null, -1, -1, start, 1, bitMask);
        }
        var first = Containing(subrecords, start);
        if (first < 0)
        {
            // Past every subrecord (a value that read nothing at the content's end)
            return new ValuePosition(path, kind, start, end - start, null, -1, -1, start, 0, bitMask);
        }
        var sub = subrecords[first];
        var atHeader = start < sub.ContentStart;
        var s = Math.Max(start, sub.ContentStart);
        if (leaf && end > s)
        {
            var last = Containing(subrecords, end - 1);
            if (last > first)
            {
                first = last;
                sub = subrecords[last];
                s = Math.Max(s, sub.ContentStart);
            }
        }
        var e = Math.Max(end, s);
        var lastReached = e > s ? Math.Max(first, Containing(subrecords, e - 1)) : first;
        if (leaf && kind == ValuePositionKind.Value && atHeader && e == s)
        {
            kind = ValuePositionKind.Presence;
        }
        return new ValuePosition(path, kind, s, e - s, sub.Type, sub.Index, sub.Turn, s - sub.ContentStart,
            lastReached - first + 1, bitMask);
    }

    /// <summary>The index of the subrecord whose bytes (header included) hold a position; -1 if none</summary>
    private static int Containing(IReadOnlyList<RecordSubrecord> subrecords, int position)
    {
        int lo = 0, hi = subrecords.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (subrecords[mid].HeaderStart <= position)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        if (found < 0 || position >= subrecords[found].End) return -1;
        return found;
    }

    private static List<RecordSubrecord> Subrecords(ReadOnlyMemorySlice<byte> bytes, int headerLength)
    {
        // A subrecord header is its type (4 bytes) and its length (2 bytes) in every game
        const int subHeaderLength = 6;
        var ret = new List<RecordSubrecord>();
        var turns = new Dictionary<int, int>();
        var span = bytes.Span;
        int pos = headerLength;
        while (pos + subHeaderLength <= span.Length)
        {
            var headerStart = pos;
            var type = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(pos));
            int length = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(pos + 4));
            if (type == RecordTypes.XXXX.TypeInt && length == 4 && pos + subHeaderLength + 4 + subHeaderLength <= span.Length)
            {
                length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(pos + subHeaderLength)));
                pos += subHeaderLength + 4;
                type = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(pos));
            }
            var contentStart = pos + subHeaderLength;
            var contentLength = Math.Min(length, span.Length - contentStart);
            var turn = turns.TryGetValue(type, out var t) ? t : 0;
            turns[type] = turn + 1;
            ret.Add(new RecordSubrecord(new RecordType(type), ret.Count, turn, headerStart, contentStart, contentLength));
            pos = contentStart + contentLength;
        }
        return ret;
    }

    private sealed record Creator(RecordType RecordType, Func<MutagenFrame, IMajorRecord> Create);

    private static readonly ConcurrentDictionary<Type, Creator> Creators = new();

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The record classes are registered with Loqui and their readers are public")]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "The record classes are registered with Loqui and their readers are public")]
    private static Creator GetCreator(Type classType)
    {
        return Creators.GetOrAdd(classType, static cls =>
        {
            var registration = LoquiRegistration.GetRegister(cls);
            if (registration.GetType().GetField(Mutagen.Bethesda.Plugins.Internals.Constants.TriggeringRecordTypeMember, BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) is not RecordType recordType)
            {
                throw new ArgumentException($"{cls} has no record type of its own");
            }
            var method = cls.GetMethod(
                             "CreateFromBinary",
                             BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                             new[] { typeof(MutagenFrame), typeof(TypedParseParams) })
                         ?? throw new ArgumentException($"{cls} has no binary reader");
            var frame = Expression.Parameter(typeof(MutagenFrame), "frame");
            var create = Expression.Lambda<Func<MutagenFrame, IMajorRecord>>(
                Expression.Convert(
                    Expression.Call(method, frame, Expression.Default(typeof(TypedParseParams))),
                    typeof(IMajorRecord)),
                frame).Compile();
            return new Creator(recordType, create);
        });
    }
}
