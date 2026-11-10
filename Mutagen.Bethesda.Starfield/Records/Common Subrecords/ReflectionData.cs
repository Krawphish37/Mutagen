using System.Buffers.Binary;
using System.Text;

namespace Mutagen.Bethesda.Starfield;

/// <summary>
/// Type ids the reflection format uses for its built-in types. A type id below 0xFFFFFF00 is instead the offset of a
/// class name in the string table (STRT), i.e. one of <see cref="ReflectionData.Classes"/>.
/// </summary>
public enum ReflectionBuiltInType : uint
{
    None = 0xFFFFFF01,
    /// <summary>A string; also how enums are stored (by value name). u16 length (with the null), then the bytes.</summary>
    String = 0xFFFFFF02,
    /// <summary>A list; its items are in the next LIST chunk (u32 item type, u32 count, items).</summary>
    List = 0xFFFFFF03,
    /// <summary>A map; its pairs are in the next MAPC chunk (u32 key type, u32 value type, u32 count, pairs).</summary>
    Map = 0xFFFFFF04,
    /// <summary>A pointer: u32 type, then a value of that type. A pointer to a form is stored as a UInt32 FormID.</summary>
    Ref = 0xFFFFFF05,
    Int8 = 0xFFFFFF08,
    UInt8 = 0xFFFFFF09,
    Int16 = 0xFFFFFF0A,
    UInt16 = 0xFFFFFF0B,
    Int32 = 0xFFFFFF0C,
    UInt32 = 0xFFFFFF0D,
    Int64 = 0xFFFFFF0E,
    UInt64 = 0xFFFFFF0F,
    Bool = 0xFFFFFF10,
    Float = 0xFFFFFF11,
    Double = 0xFFFFFF12,
}

[Flags]
public enum ReflectionClassFlag : ushort
{
    /// <summary>The class serializes itself: its values are in their own USER (or, in a diff, USRD) chunk.</summary>
    User = 0x0004,
    Struct = 0x0008,
}

/// <summary>One chunk of a reflection blob: a 4-character signature, a u32 data length, then the data.</summary>
public sealed record ReflectionChunk(string Signature, int Position, int DataLength)
{
    /// <summary>Offset of the chunk's data in the blob (after the 8-byte chunk header).</summary>
    public int DataPosition => Position + 8;
}

/// <summary>A field of a reflection class definition (CLAS).</summary>
/// <param name="MemoryOffset">Offset of the field in the game's own (in-memory) object; not used by the file format.</param>
/// <param name="MemorySize">Size of the field in the game's own object; for a String it tells a string (8) from an enum (1-4).</param>
public sealed record ReflectionField(string Name, uint NameId, uint TypeId, string TypeName, ushort MemoryOffset, ushort MemorySize);

/// <summary>A class definition (CLAS chunk).</summary>
/// <param name="Form">The u32 after the class name (xEdit calls it "Form"); 0 or 1.</param>
public sealed record ReflectionClass(string Name, uint NameId, uint Form, ReflectionClassFlag Flags, IReadOnlyList<ReflectionField> Fields)
{
    public bool IsUser => (Flags & ReflectionClassFlag.User) != 0;
}

/// <summary>A decoded value. <see cref="Position"/> is the offset of its first byte in the blob.</summary>
public abstract record ReflectionValue(uint TypeId, int Position);

/// <summary>A number, bool or string (enums are strings). <see cref="Value"/> is the matching C# type.</summary>
public sealed record ReflectionScalar(uint TypeId, int Position, object Value) : ReflectionValue(TypeId, Position);

/// <summary>A class value. In a diff (DIFF/USRD) only the members that differ from the parent are present.</summary>
public sealed record ReflectionObject(uint TypeId, int Position, ReflectionClass Class, bool IsDiff, IReadOnlyList<ReflectionMember> Members)
    : ReflectionValue(TypeId, Position);

public sealed record ReflectionMember(int FieldIndex, ReflectionField Field, ReflectionValue Value);

public sealed record ReflectionList(uint TypeId, int Position, uint ItemTypeId, IReadOnlyList<ReflectionValue> Items)
    : ReflectionValue(TypeId, Position);

public sealed record ReflectionMap(uint TypeId, int Position, uint KeyTypeId, uint ValueTypeId, IReadOnlyList<KeyValuePair<ReflectionValue, ReflectionValue>> Pairs)
    : ReflectionValue(TypeId, Position);

/// <summary>A pointer. <see cref="Target"/> is null when the pointer type is None.</summary>
public sealed record ReflectionRef(uint TypeId, int Position, uint TargetTypeId, ReflectionValue? Target)
    : ReflectionValue(TypeId, Position);

/// <summary>
/// A value of a class that serializes itself (USER/USRD chunk): u32 class, the class's own data, then a u32 count of
/// the chunks that follow and belong to it. In Starfield.esm the class's own data is a list of typed values
/// (u32 type, value), which is what <see cref="Values"/> holds.
/// </summary>
public sealed record ReflectionUser(uint TypeId, int Position, ReflectionClass Class, bool IsDiff, IReadOnlyList<ReflectionValue> Values, uint ChildChunkCount)
    : ReflectionValue(TypeId, Position);

/// <summary>A FormID stored inside a reflection blob (a pointer to a form).</summary>
/// <param name="Position">Offset of the 4 FormID bytes in the blob.</param>
/// <param name="FormID">The raw FormID, relative to the plugin's master list like any FormID in the file.</param>
/// <param name="Path">Where it is, e.g. <c>BGSFormFolderKeywordList.Keywords[0]</c>.</param>
public sealed record ReflectionFormIdLocation(int Position, uint FormID, string Path);

/// <summary>
/// A parsed view of a Starfield reflection blob (the subrecords REFL, PTCL, RDIF, PSDF, PCCC and XNSE all hold one).
/// The format is the one of Bethesda's reflection serializer (also used by the material database):
/// <c>BETH</c> (u32 version, u32 chunk count), <c>STRT</c> (the string table), <c>TYPE</c> (u32 class count),
/// one <c>CLAS</c> per class, then the object data: <c>OBJT</c> (a whole object) or <c>DIFF</c> (only what differs from
/// the reflection parent), with <c>LIST</c>, <c>MAPC</c>, <c>USER</c> and <c>USRD</c> chunks for the lists, maps and
/// self-serialized values met while reading it, in that order.
/// This is a read-only view: the blob's bytes stay the source of truth, so writing it back is always byte-identical.
/// </summary>
public sealed class ReflectionData
{
    private static readonly Encoding Latin1 = Encoding.Latin1;

    public uint Version { get; }
    /// <summary>The chunk count stored in the BETH header (counts every chunk, BETH included).</summary>
    public uint ChunkCount { get; }
    public IReadOnlyList<ReflectionChunk> Chunks { get; }
    /// <summary>The string table, by offset.</summary>
    public IReadOnlyDictionary<uint, string> Strings { get; }
    /// <summary>The class count stored in the TYPE chunk.</summary>
    public uint ClassCount { get; }
    public IReadOnlyList<ReflectionClass> Classes { get; }
    /// <summary>The top-level objects (one per OBJT or DIFF chunk).</summary>
    public IReadOnlyList<ReflectionValue> Objects { get; }
    /// <summary>True when every byte of the blob was decoded.</summary>
    public bool FullyDecoded => Error == null;
    /// <summary>Why the object data could not be fully decoded (the header, strings and classes still are).</summary>
    public string? Error { get; }
    /// <summary>Every FormID found in the object data (pointers whose type is UInt32).</summary>
    public IReadOnlyList<ReflectionFormIdLocation> FormIds { get; }

    private ReflectionData(uint version, uint chunkCount, IReadOnlyList<ReflectionChunk> chunks,
        IReadOnlyDictionary<uint, string> strings, uint classCount, IReadOnlyList<ReflectionClass> classes,
        IReadOnlyList<ReflectionValue> objects, IReadOnlyList<ReflectionFormIdLocation> formIds, string? error)
    {
        Version = version;
        ChunkCount = chunkCount;
        Chunks = chunks;
        Strings = strings;
        ClassCount = classCount;
        Classes = classes;
        Objects = objects;
        FormIds = formIds;
        Error = error;
    }

    /// <summary>True when the bytes start with the reflection header (BETH).</summary>
    public static bool IsReflection(ReadOnlySpan<byte> data) =>
        data.Length >= 16 && data[0] == (byte)'B' && data[1] == (byte)'E' && data[2] == (byte)'T' && data[3] == (byte)'H';

    /// <summary>Parses a reflection blob. Returns null if it isn't one (no BETH header or a broken chunk layout).</summary>
    public static ReflectionData? TryParse(ReadOnlySpan<byte> data)
    {
        if (!IsReflection(data)) return null;
        var span = data;
        var chunks = new List<ReflectionChunk>();
        int pos = 0;
        while (pos + 8 <= span.Length)
        {
            var len = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(pos + 4));
            if (len > (uint)(span.Length - pos - 8)) return null;
            chunks.Add(new ReflectionChunk(Latin1.GetString(span.Slice(pos, 4)), pos, (int)len));
            pos += 8 + (int)len;
        }
        if (pos != span.Length) return null;
        if (chunks.Count < 3 || chunks[0].DataLength < 8 || chunks[1].Signature != "STRT" || chunks[2].Signature != "TYPE"
            || chunks[2].DataLength < 4)
        {
            return null;
        }
        var version = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8));
        var chunkCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12));

        var strt = chunks[1];
        var strings = new Dictionary<uint, string>();
        var strtSpan = span.Slice(strt.DataPosition, strt.DataLength);
        var strtCopy = strtSpan.ToArray();
        for (int i = 0; i < strtSpan.Length;)
        {
            var end = strtSpan.Slice(i).IndexOf((byte)0);
            if (end < 0) end = strtSpan.Length - i;
            strings[(uint)i] = Latin1.GetString(strtSpan.Slice(i, end));
            i += end + 1;
        }

        var classCount = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(chunks[2].DataPosition));
        var classes = new List<ReflectionClass>();
        int chunkIndex = 3;
        string TypeName(uint id) => id >= 0xFFFFFF00
            ? (Enum.IsDefined(typeof(ReflectionBuiltInType), id) ? ((ReflectionBuiltInType)id).ToString() : $"BuiltIn{id & 0xFF:X2}")
            : NameOf(strtCopy, strings, id);
        for (; chunkIndex < chunks.Count && chunks[chunkIndex].Signature == "CLAS"; chunkIndex++)
        {
            var c = span.Slice(chunks[chunkIndex].DataPosition, chunks[chunkIndex].DataLength);
            if (c.Length < 12) return null;
            var nameId = BinaryPrimitives.ReadUInt32LittleEndian(c);
            var form = BinaryPrimitives.ReadUInt32LittleEndian(c.Slice(4));
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(c.Slice(8));
            var fieldCount = BinaryPrimitives.ReadUInt16LittleEndian(c.Slice(10));
            if (c.Length != 12 + fieldCount * 12) return null;
            var fields = new ReflectionField[fieldCount];
            for (int i = 0; i < fieldCount; i++)
            {
                var f = c.Slice(12 + i * 12);
                var fieldName = BinaryPrimitives.ReadUInt32LittleEndian(f);
                var typeId = BinaryPrimitives.ReadUInt32LittleEndian(f.Slice(4));
                fields[i] = new ReflectionField(NameOf(strtSpan, strings, fieldName), fieldName, typeId, TypeName(typeId),
                    BinaryPrimitives.ReadUInt16LittleEndian(f.Slice(8)), BinaryPrimitives.ReadUInt16LittleEndian(f.Slice(10)));
            }
            classes.Add(new ReflectionClass(NameOf(strtSpan, strings, nameId), nameId, form, (ReflectionClassFlag)flags, fields));
        }

        var parser = new Parser(data, chunks, chunkIndex, classes, TypeName);
        var objects = new List<ReflectionValue>();
        string? error = null;
        try
        {
            while (parser.NextChunk < chunks.Count)
            {
                objects.Add(parser.ReadTopLevel());
            }
        }
        catch (ReflectionFormatException ex)
        {
            error = ex.Message;
        }
        return new ReflectionData(version, chunkCount, chunks, strings, classCount, classes, objects,
            error == null ? parser.FormIds : Array.Empty<ReflectionFormIdLocation>(), error);
    }

    private static readonly byte[] RefTypeBytes = { 0x05, 0xFF, 0xFF, 0xFF };

    /// <summary>
    /// Fields that hold a FormID as a plain UInt32 rather than as a pointer, by class and field name.
    /// NavMeshSplineExtraData::ChunkDataRef.RefID (REFR XNSE): 21,587 in Starfield.esm, every one a REFR's FormID.
    /// </summary>
    public static readonly IReadOnlyList<(string Class, string Field)> PlainFormIdFields = new[]
    {
        ("NavMeshSplineExtraData::ChunkDataRef", "RefID"),
    };

    private static readonly byte[][] PlainFormIdClassNames = PlainFormIdFields
        .Select(x => Encoding.ASCII.GetBytes(x.Class))
        .Distinct()
        .ToArray();

    /// <summary>
    /// The FormIDs in a reflection blob, in the order they are stored. Empty when the bytes aren't a reflection blob,
    /// can't be fully decoded, or hold no FormIDs. Faster than <see cref="TryParse"/> for blobs without FormIDs.
    /// </summary>
    public static IReadOnlyList<ReflectionFormIdLocation> FindFormIds(ReadOnlySpan<byte> data)
    {
        // Every pointer, wherever it is, has its type written as the Ref type id (in a class field, a LIST or MAPC
        // item type, or a self-serialized value), so a blob without those 4 bytes holds no pointers; and a plain
        // FormID field needs its class name in the string table.
        if (!IsReflection(data)) return Array.Empty<ReflectionFormIdLocation>();
        if (data.IndexOf(RefTypeBytes) < 0)
        {
            var hasPlain = false;
            foreach (var name in PlainFormIdClassNames)
            {
                if (data.IndexOf(name) >= 0)
                {
                    hasPlain = true;
                    break;
                }
            }
            if (!hasPlain) return Array.Empty<ReflectionFormIdLocation>();
        }
        var parsed = TryParse(data);
        return parsed is { FullyDecoded: true } ? parsed.FormIds : Array.Empty<ReflectionFormIdLocation>();
    }

    /// <summary>Finds a class by its type id (the offset of its name in the string table).</summary>
    public ReflectionClass? GetClass(uint typeId)
    {
        foreach (var c in Classes)
        {
            if (c.NameId == typeId) return c;
        }
        return null;
    }

    private static string NameOf(ReadOnlySpan<byte> strt, Dictionary<uint, string> strings, uint offset)
    {
        if (strings.TryGetValue(offset, out var s)) return s;
        // An offset into the middle of a string (a suffix) is legal for a string table.
        if (offset < strt.Length)
        {
            var rest = strt.Slice((int)offset);
            var end = rest.IndexOf((byte)0);
            return Latin1.GetString(end < 0 ? rest : rest.Slice(0, end));
        }
        return $"<string {offset:X}>";
    }

    private sealed class ReflectionFormatException : Exception
    {
        public ReflectionFormatException(string message) : base(message) { }
    }

    /// <summary>A cursor over one chunk's data.</summary>
    private sealed class Cursor
    {
        public readonly ReflectionChunk Chunk;
        public int Pos;
        public int End => Chunk.DataPosition + Chunk.DataLength;
        public int Remaining => End - Pos;
        public Cursor(ReflectionChunk chunk) { Chunk = chunk; Pos = chunk.DataPosition; }
    }

    private ref struct Parser
    {
        private readonly ReadOnlySpan<byte> _data;
        private readonly List<ReflectionChunk> _chunks;
        private readonly Dictionary<uint, ReflectionClass> _classes = new();
        private readonly Func<uint, string> _typeName;
        public readonly List<ReflectionFormIdLocation> FormIds = new();
        public int NextChunk;

        public Parser(ReadOnlySpan<byte> data, List<ReflectionChunk> chunks, int firstObjectChunk,
            List<ReflectionClass> classes, Func<uint, string> typeName)
        {
            _data = data;
            _chunks = chunks;
            NextChunk = firstObjectChunk;
            foreach (var c in classes) _classes[c.NameId] = c;
            _typeName = typeName;
        }

        private ReadOnlySpan<byte> Span => _data;

        private static ReflectionFormatException Fail(string msg) => new(msg);

        private ReflectionClass Class(uint id) =>
            _classes.TryGetValue(id, out var c) ? c : throw Fail($"Unknown type id {id:X8}");

        private void Need(Cursor cur, int n)
        {
            if (cur.Remaining < n) throw Fail($"{cur.Chunk.Signature} at {cur.Chunk.Position}: read past the end");
        }

        private uint U32(Cursor cur) { Need(cur, 4); var v = BinaryPrimitives.ReadUInt32LittleEndian(Span.Slice(cur.Pos)); cur.Pos += 4; return v; }
        private ushort U16(Cursor cur) { Need(cur, 2); var v = BinaryPrimitives.ReadUInt16LittleEndian(Span.Slice(cur.Pos)); cur.Pos += 2; return v; }

        private Cursor Take(params string[] sigs)
        {
            if (NextChunk >= _chunks.Count) throw Fail($"Expected a {string.Join("/", sigs)} chunk, but the blob ended");
            var c = _chunks[NextChunk];
            if (Array.IndexOf(sigs, c.Signature) < 0) throw Fail($"Expected a {string.Join("/", sigs)} chunk at {c.Position}, found {c.Signature}");
            NextChunk++;
            return new Cursor(c);
        }

        private static void Done(Cursor cur)
        {
            if (cur.Remaining != 0) throw Fail($"{cur.Chunk.Signature} at {cur.Chunk.Position}: {cur.Remaining} bytes left over");
        }

        public ReflectionValue ReadTopLevel()
        {
            var cur = Take("OBJT", "DIFF");
            var diff = cur.Chunk.Signature == "DIFF";
            var pos = cur.Pos;
            var type = U32(cur);
            var cls = Class(type);
            var name = cls.Name;
            ReflectionValue v = cls.IsUser ? ReadUser(type, pos, diff, name) : ReadObject(cur, cls, type, pos, diff, name);
            Done(cur);
            return v;
        }

        private ReflectionValue ReadValue(Cursor cur, uint type, bool diff, string path)
        {
            var pos = cur.Pos;
            if (type >= 0xFFFFFF00)
            {
                switch ((ReflectionBuiltInType)type)
                {
                    case ReflectionBuiltInType.String:
                    {
                        var len = U16(cur);
                        Need(cur, len);
                        var s = Span.Slice(cur.Pos, len);
                        if (len > 0 && s[^1] == 0) s = s.Slice(0, len - 1);
                        cur.Pos += len;
                        return new ReflectionScalar(type, pos, Latin1.GetString(s));
                    }
                    case ReflectionBuiltInType.List: return ReadList(type, pos, diff, path);
                    case ReflectionBuiltInType.Map: return ReadMap(type, pos, diff, path);
                    case ReflectionBuiltInType.Ref:
                    {
                        var target = U32(cur);
                        if (target == (uint)ReflectionBuiltInType.None) return new ReflectionRef(type, pos, target, null);
                        if (target == (uint)ReflectionBuiltInType.UInt32)
                        {
                            FormIds.Add(new ReflectionFormIdLocation(cur.Pos, BinaryPrimitives.ReadUInt32LittleEndian(Span.Slice(cur.Pos, 4)), path));
                        }
                        return new ReflectionRef(type, pos, target, ReadValue(cur, target, diff, path));
                    }
                    case ReflectionBuiltInType.Int8: Need(cur, 1); cur.Pos += 1; return new ReflectionScalar(type, pos, (sbyte)Span[pos]);
                    case ReflectionBuiltInType.UInt8: Need(cur, 1); cur.Pos += 1; return new ReflectionScalar(type, pos, Span[pos]);
                    case ReflectionBuiltInType.Bool: Need(cur, 1); cur.Pos += 1; return new ReflectionScalar(type, pos, Span[pos] != 0);
                    case ReflectionBuiltInType.Int16: Need(cur, 2); cur.Pos += 2; return new ReflectionScalar(type, pos, BinaryPrimitives.ReadInt16LittleEndian(Span.Slice(pos)));
                    case ReflectionBuiltInType.UInt16: Need(cur, 2); cur.Pos += 2; return new ReflectionScalar(type, pos, BinaryPrimitives.ReadUInt16LittleEndian(Span.Slice(pos)));
                    case ReflectionBuiltInType.Int32: Need(cur, 4); cur.Pos += 4; return new ReflectionScalar(type, pos, BinaryPrimitives.ReadInt32LittleEndian(Span.Slice(pos)));
                    case ReflectionBuiltInType.UInt32: Need(cur, 4); cur.Pos += 4; return new ReflectionScalar(type, pos, BinaryPrimitives.ReadUInt32LittleEndian(Span.Slice(pos)));
                    case ReflectionBuiltInType.Int64: Need(cur, 8); cur.Pos += 8; return new ReflectionScalar(type, pos, BinaryPrimitives.ReadInt64LittleEndian(Span.Slice(pos)));
                    case ReflectionBuiltInType.UInt64: Need(cur, 8); cur.Pos += 8; return new ReflectionScalar(type, pos, BinaryPrimitives.ReadUInt64LittleEndian(Span.Slice(pos)));
                    case ReflectionBuiltInType.Float: Need(cur, 4); cur.Pos += 4; return new ReflectionScalar(type, pos, BinaryPrimitives.ReadSingleLittleEndian(Span.Slice(pos)));
                    case ReflectionBuiltInType.Double: Need(cur, 8); cur.Pos += 8; return new ReflectionScalar(type, pos, BinaryPrimitives.ReadDoubleLittleEndian(Span.Slice(pos)));
                    default: throw Fail($"Built-in type {type:X8} at {pos} isn't known");
                }
            }
            var cls = Class(type);
            if (cls.IsUser) return ReadUser(type, pos, diff, path);
            return ReadObject(cur, cls, type, pos, diff, path);
        }

        private ReflectionObject ReadObject(Cursor cur, ReflectionClass cls, uint type, int pos, bool diff, string path)
        {
            var members = new List<ReflectionMember>();
            if (diff)
            {
                while (true)
                {
                    var index = U16(cur);
                    if (index == 0xFFFF) break;
                    if (index >= cls.Fields.Count) throw Fail($"{cls.Name}: field index {index} out of range at {cur.Pos - 2}");
                    members.Add(ReadMember(cur, cls, index, diff, path));
                }
            }
            else
            {
                for (int i = 0; i < cls.Fields.Count; i++)
                {
                    members.Add(ReadMember(cur, cls, i, diff, path));
                }
            }
            return new ReflectionObject(type, pos, cls, diff, members);
        }

        private ReflectionMember ReadMember(Cursor cur, ReflectionClass cls, int index, bool diff, string path)
        {
            var field = cls.Fields[index];
            var fieldPath = $"{path}.{field.Name}";
            var value = ReadValue(cur, field.TypeId, diff, fieldPath);
            if (field.TypeId == (uint)ReflectionBuiltInType.UInt32 && value is ReflectionScalar { Value: uint formId })
            {
                foreach (var plain in PlainFormIdFields)
                {
                    if (plain.Field == field.Name && plain.Class == cls.Name)
                    {
                        FormIds.Add(new ReflectionFormIdLocation(value.Position, formId, fieldPath));
                        break;
                    }
                }
            }
            return new ReflectionMember(index, field, value);
        }

        private ReflectionList ReadList(uint type, int pos, bool diff, string path)
        {
            var cur = Take("LIST");
            var itemType = U32(cur);
            var count = U32(cur);
            var items = new List<ReflectionValue>();
            for (uint i = 0; i < count; i++)
            {
                items.Add(ReadValue(cur, itemType, diff, $"{path}[{i}]"));
            }
            Done(cur);
            return new ReflectionList(type, pos, itemType, items);
        }

        private ReflectionMap ReadMap(uint type, int pos, bool diff, string path)
        {
            var cur = Take("MAPC");
            var keyType = U32(cur);
            var valueType = U32(cur);
            var count = U32(cur);
            var pairs = new List<KeyValuePair<ReflectionValue, ReflectionValue>>();
            for (uint i = 0; i < count; i++)
            {
                var key = ReadValue(cur, keyType, diff, $"{path}[#{i} key]");
                var value = ReadValue(cur, valueType, diff, $"{path}[{(key is ReflectionScalar s ? s.Value : $"#{i}")}]");
                pairs.Add(new KeyValuePair<ReflectionValue, ReflectionValue>(key, value));
            }
            Done(cur);
            return new ReflectionMap(type, pos, keyType, valueType, pairs);
        }

        private ReflectionUser ReadUser(uint type, int pos, bool diff, string path)
        {
            var cur = Take(diff ? "USRD" : "USER");
            var cls = Class(type);
            var stored = U32(cur);
            if (stored != type) throw Fail($"{cur.Chunk.Signature} at {cur.Chunk.Position}: class {stored:X8}, expected {type:X8}");
            var first = NextChunk;
            var values = new List<ReflectionValue>();
            while (cur.Remaining > 4)
            {
                var t = U32(cur);
                var valuePos = cur.Pos;
                // The class itself is written inside its own chunk as a plain class (all fields, or a diff).
                values.Add(t == type
                    ? ReadObject(cur, cls, t, valuePos, diff, path)
                    : ReadValue(cur, t, diff, $"{path}<{_typeName(t)}>"));
            }
            var childCount = U32(cur);
            Done(cur);
            if (NextChunk - first != childCount)
            {
                throw Fail($"{cur.Chunk.Signature} at {cur.Chunk.Position}: says {childCount} child chunks, read {NextChunk - first}");
            }
            return new ReflectionUser(type, cur.Chunk.DataPosition, cls, diff, values, childCount);
        }
    }
}
