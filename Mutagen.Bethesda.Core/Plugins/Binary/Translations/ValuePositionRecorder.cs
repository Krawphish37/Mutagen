using Mutagen.Bethesda.Plugins.Binary.Streams;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Binary.Translations;

/// <summary>
/// For ValuePositionMixIn (C60): while one record is read by the full-import reader, notes where each value it reads
/// sits in the record's bytes. The readers note their own reads (the generated readers around every field, the list,
/// gendered, dictionary and 2D-array translations around every item, the hand-written readers around what they set),
/// so the positions come from the code that reads the values, not from a second description of it.<br/>
/// Every note site is written <c>if (ValuePositionRecorder.On) ValuePositionRecorder.X(...)</c>: while nothing is being
/// recorded that is one load of a static field per field read, and nothing else.
/// </summary>
internal static class ValuePositionRecorder
{
    /// <summary>Whether a recording runs on any thread (the cheap check every note site makes first)</summary>
    internal static bool On;

    private static readonly object Gate = new();
    private static int _active;

    [ThreadStatic] private static Recording? _current;

    internal enum NodeKind : byte
    {
        Field,
        List,
        Item,
        HandRead,
        Count,
        At,
        /// <summary>A field a hand-written reader fills with no bytes of its own (a count derived from a length): no position</summary>
        Filled,
        /// <summary>The stored key of a dictionary entry (C83), named by the entry ("[3]")</summary>
        Key,
    }

    internal struct Node
    {
        public int Parent;
        public NodeKind Kind;
        public string? Name;
        public int Index;
        public long Start;
        public long End;
        public ulong BitMask;
        public bool Dropped;
        public bool HasChildren;
        public bool Converted;
    }

    /// <summary>One record's notes, in the order they were made</summary>
    internal sealed class Recording
    {
        public readonly List<Node> Nodes = new();
        public readonly List<int> Stack = new();

        /// <summary>The record's content was handed over already decompressed (an overlay's bytes)</summary>
        public bool ContentDecompressed;

        /// <summary>Where the record (its header) starts, with the stream's offset; null until its read starts</summary>
        public long? RecordStart;

        /// <summary>Where the record's content starts (after the header), with the stream's offset</summary>
        public long? ContentStart;

        /// <summary>The record whose content is being read (the first one whose read started)</summary>
        public object? Record;

        /// <summary>The first node noted in the record's content (after its header); -1 until the content starts</summary>
        public int ContentNodeStart = -1;

        /// <summary>
        /// Only which fields the record's readers fill is wanted (PartialFormMixIn.TryGetPresentFields, C75): the record is
        /// read exactly as without a recording (a compressed record is decompressed as usual)
        /// </summary>
        public bool PresenceOnly;

        /// <summary>A compressed record's content, as decompressed while recording</summary>
        public byte[]? Decompressed;

        public int Depth => Stack.Count;
    }

    public static Recording Start(bool contentDecompressed)
    {
        if (_current != null) throw new InvalidOperationException("A value-position recording already runs on this thread");
        var ret = new Recording { ContentDecompressed = contentDecompressed };
        _current = ret;
        lock (Gate)
        {
            _active++;
            On = true;
        }
        return ret;
    }

    /// <summary>
    /// Starts a recording of a record whose reader doesn't say where it starts (a plugin header, C82): it starts at
    /// <paramref name="recordStart"/> and its content at <paramref name="contentStart"/> (with the stream's offset)
    /// </summary>
    public static Recording Start(bool contentDecompressed, long recordStart, long contentStart)
    {
        var ret = Start(contentDecompressed);
        ret.RecordStart = recordStart;
        ret.ContentStart = contentStart;
        ret.ContentNodeStart = 0;
        return ret;
    }

    /// <summary>The recording running on this thread, if any</summary>
    internal static Recording? Current => _current;

    /// <summary>
    /// Starts a recording that only notes which fields the record's readers fill (C75): see <see cref="Recording.PresenceOnly"/>
    /// </summary>
    public static Recording StartPresence()
    {
        var ret = Start(contentDecompressed: false);
        ret.PresenceOnly = true;
        return ret;
    }

    /// <summary>
    /// The record's own fields its content's readers noted, from node <paramref name="from"/> on: the names of the notes
    /// made directly in the record, a hand-written step being transparent (its notes are named relative to the record),
    /// each cut to its first property ("Data.Function": Data). The record header's values aren't among them.
    /// </summary>
    public static IEnumerable<string> FieldsNoted(Recording recording, int from)
    {
        if (recording.ContentNodeStart < 0) yield break;
        var nodes = recording.Nodes;
        for (int i = Math.Max(from, recording.ContentNodeStart); i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n.Name == null || n.Kind is NodeKind.HandRead or NodeKind.Item or NodeKind.Count or NodeKind.Key || n.Dropped) continue;
            var parent = n.Parent;
            while (parent >= 0 && nodes[parent].Kind == NodeKind.HandRead && !nodes[parent].Dropped) parent = nodes[parent].Parent;
            if (parent >= 0) continue;
            var end = n.Name.IndexOfAny(['.', '[']);
            yield return end < 0 ? n.Name : n.Name[..end];
        }
    }

    /// <summary>
    /// For a hand-written reader: a field it fills whose value has no bytes of its own (a count derived from a length).
    /// No position; noted so the field counts as read from the record (PartialFormMixIn.TryGetPresentFields, C75).
    /// </summary>
    /// <param name="name">The field's name, relative to the object being read</param>
    public static void Filled(string name)
    {
        if (_current is not { RecordStart: not null } c) return;
        c.Nodes.Add(new Node
        {
            Parent = c.Stack.Count > 0 ? c.Stack[^1] : -1,
            Kind = NodeKind.Filled,
            Name = name,
            Start = -1,
            End = -1,
        });
    }

    public static void Stop(Recording recording)
    {
        if (ReferenceEquals(_current, recording)) _current = null;
        lock (Gate)
        {
            _active--;
            On = _active > 0;
        }
    }

    /// <summary>A reader's position, with its stream's offset (a reframed stream's positions continue its parent's)</summary>
    public static long Pos<TReader>(TReader reader)
        where TReader : IBinaryReadStream
    {
        return reader is IMutagenReadStream m ? m.Position + m.OffsetReference : reader.Position;
    }

    private static void Push<TReader>(TReader frame, NodeKind kind, string? name, int index)
        where TReader : IBinaryReadStream
    {
        if (_current is not { RecordStart: not null } c) return;
        var parent = c.Stack.Count > 0 ? c.Stack[^1] : -1;
        if (parent >= 0)
        {
            var p = c.Nodes[parent];
            p.HasChildren = true;
            c.Nodes[parent] = p;
        }
        c.Nodes.Add(new Node
        {
            Parent = parent,
            Kind = kind,
            Name = name,
            Index = index,
            Start = Pos(frame),
            End = -1,
        });
        c.Stack.Add(c.Nodes.Count - 1);
    }

    /// <summary>A field is read from here on (a property of the object being read)</summary>
    public static void Field<TReader>(TReader frame, string name) where TReader : IBinaryReadStream => Push(frame, NodeKind.Field, name, -1);

    /// <summary>
    /// A field is read from here on whose value the reader computes from the bytes (a float stored as an integer or
    /// scaled, a percent, swapped coordinates, a link whose max value reads as none): its bytes, not the value as read
    /// </summary>
    public static void Field<TReader>(TReader frame, string name, bool converted)
        where TReader : IBinaryReadStream
    {
        Push(frame, NodeKind.Field, name, -1);
        if (!converted || _current is not { Stack.Count: > 0 } c) return;
        var i = c.Stack[^1];
        var n = c.Nodes[i];
        n.Converted = true;
        c.Nodes[i] = n;
    }

    /// <summary>A list, array or dictionary field is read from here on</summary>
    public static void List<TReader>(TReader frame, string name) where TReader : IBinaryReadStream => Push(frame, NodeKind.List, name, -1);

    /// <summary>A list is read from here on whose items the reader computes from their bytes (see the Field overload)</summary>
    public static void List<TReader>(TReader frame, string name, bool converted)
        where TReader : IBinaryReadStream
    {
        Push(frame, NodeKind.List, name, -1);
        if (!converted || _current is not { Stack.Count: > 0 } c) return;
        var i = c.Stack[^1];
        var n = c.Nodes[i];
        n.Converted = true;
        c.Nodes[i] = n;
    }

    /// <summary>
    /// For a hand-written reader: a list field whose read started at <paramref name="start"/> (with the stream's offset),
    /// before the reader's place (a list found inside bytes read already); it ends at the next <see cref="End"/>
    /// </summary>
    public static void ListFrom(string name, long start)
    {
        if (_current is not { RecordStart: not null } c) return;
        var parent = c.Stack.Count > 0 ? c.Stack[^1] : -1;
        if (parent >= 0)
        {
            var p = c.Nodes[parent];
            p.HasChildren = true;
            c.Nodes[parent] = p;
        }
        c.Nodes.Add(new Node
        {
            Parent = parent,
            Kind = NodeKind.List,
            Name = name,
            Index = -1,
            Start = start,
            End = -1,
        });
        c.Stack.Add(c.Nodes.Count - 1);
    }

    /// <summary>
    /// For a hand-written reader: the stored key of an entry of the dictionary being read (C83: a package data input's
    /// UNAM index), <paramref name="length"/> bytes from <paramref name="start"/>. Its path is the entry's.
    /// </summary>
    /// <param name="key">The entry's key, as its path names it ("[3]")</param>
    /// <param name="start">Where the key's bytes start, with the stream's offset</param>
    /// <param name="length">How many bytes</param>
    public static void KeyAt(string key, long start, int length)
    {
        Leaf(NodeKind.Key, key, start, start + length, 0);
    }

    /// <summary>
    /// A hand-written reader step is read from here on (a CustomLogic step, a binary="Custom" field, a customBinaryEnd).
    /// What it notes inside is named relative to the object, as the object's own fields are.
    /// </summary>
    public static void HandRead<TReader>(TReader frame, string name) where TReader : IBinaryReadStream => Push(frame, NodeKind.HandRead, name, -1);

    /// <summary>An item of the list being read, by its index in the list</summary>
    public static void Item<TReader>(TReader frame, int index) where TReader : IBinaryReadStream => Push(frame, NodeKind.Item, null, index);

    /// <summary>An entry of the dictionary or 2D array being read, by its key ("Fire", "3,4")</summary>
    public static void Item<TReader>(TReader frame, string key) where TReader : IBinaryReadStream => Push(frame, NodeKind.Item, key, -1);

    /// <summary>An item of the list being read, by its index, whose read started at <paramref name="start"/> (with the stream's offset)</summary>
    public static void ItemFrom(long start, int index)
    {
        if (_current is not { RecordStart: not null } c) return;
        var parent = c.Stack.Count > 0 ? c.Stack[^1] : -1;
        if (parent >= 0)
        {
            var p = c.Nodes[parent];
            p.HasChildren = true;
            c.Nodes[parent] = p;
        }
        c.Nodes.Add(new Node
        {
            Parent = parent,
            Kind = NodeKind.Item,
            Index = index,
            Start = start,
            End = -1,
        });
        c.Stack.Add(c.Nodes.Count - 1);
    }

    /// <summary>For a hand-written reader: an item of the list being read that is a value, from <paramref name="start"/> (<see cref="Here"/>) to here</summary>
    public static void ItemValue<TReader>(TReader frame, int index, long start, bool converted = false)
        where TReader : IBinaryReadStream
    {
        if (start < 0 || !On) return;
        Leaf(NodeKind.Item, null, start, Pos(frame), 0, index, converted);
    }

    /// <summary>An item of the list being read that is a value, read already: <paramref name="length"/> bytes from <paramref name="start"/></summary>
    public static void ItemAt(int index, long start, int length, bool converted = false)
    {
        Leaf(NodeKind.Item, null, start, start + length, 0, index, converted);
    }

    /// <summary>
    /// Items of the list being read that were read in one go and end here: <paramref name="count"/> values of
    /// <paramref name="itemLength"/> bytes each, in order (C83: a fixed-size array of floats)
    /// </summary>
    public static void ItemsBefore<TReader>(TReader frame, int count, int itemLength)
        where TReader : IBinaryReadStream
    {
        if (_current is not { RecordStart: not null }) return;
        var start = Pos(frame) - (long)count * itemLength;
        for (int i = 0; i < count; i++)
        {
            ItemAt(i, start + (long)i * itemLength, itemLength);
        }
    }

    /// <summary>The value or item begun last is read: it ends here</summary>
    public static void End<TReader>(TReader frame)
        where TReader : IBinaryReadStream
    {
        if (_current is not { } c || c.Stack.Count == 0) return;
        var i = c.Stack[^1];
        c.Stack.RemoveAt(c.Stack.Count - 1);
        var n = c.Nodes[i];
        n.End = Pos(frame);
        c.Nodes[i] = n;
    }

    /// <summary>The item begun last didn't make an item (its reader gave nothing): it and what it holds are left out</summary>
    public static void Drop()
    {
        if (_current is not { } c || c.Stack.Count == 0) return;
        var i = c.Stack[^1];
        c.Stack.RemoveAt(c.Stack.Count - 1);
        var n = c.Nodes[i];
        n.Dropped = true;
        n.End = n.Start;
        c.Nodes[i] = n;
    }

    /// <summary>The stored count of the list being read: from <paramref name="start"/> (with the stream's offset) to here</summary>
    public static void Count<TReader>(TReader frame, long start)
        where TReader : IBinaryReadStream
    {
        Leaf(NodeKind.Count, null, start, Pos(frame), 0);
    }

    /// <summary>The stored count of the list being read, read already: <paramref name="length"/> bytes from <paramref name="start"/></summary>
    public static void CountAt(long start, int length)
    {
        Leaf(NodeKind.Count, null, start, start + length, 0);
    }

    /// <summary>
    /// C85: for a generated reader that reads a list's count inline (a prepended count, as the list's amount): notes the
    /// <paramref name="length"/> bytes just read as the count of the list being read, and gives the count back
    /// </summary>
    public static int Counted<TReader>(TReader frame, int count, int length)
        where TReader : IBinaryReadStream
    {
        if (On)
        {
            var end = Pos(frame);
            Leaf(NodeKind.Count, null, end - length, end, 0);
        }
        return count;
    }

    /// <summary>
    /// A value of the object being read, whose bytes were read already: <paramref name="length"/> bytes from
    /// <paramref name="start"/> (with the stream's offset, as <see cref="MutagenFrame.PositionWithOffset"/> gives it),
    /// optionally only the bits of <paramref name="bitMask"/> (of the little-endian value of those bytes)
    /// </summary>
    /// <param name="name">The value's name, relative to the object being read ("Data.Function", "[3]" for an item)</param>
    /// <param name="start">Where its bytes start, with the stream's offset</param>
    /// <param name="length">How many bytes</param>
    /// <param name="bitMask">0, or the bits of those bytes that hold the value</param>
    /// <param name="converted">The reader computes the value from the bytes (scaled, inverted, a byte of 255, an index
    /// it looks up, ...): the bytes are the value's, but not the value as it reads</param>
    public static void At(string name, long start, int length, ulong bitMask = 0, bool converted = false)
    {
        Leaf(NodeKind.At, name, start, start + length, bitMask, -1, converted);
    }

    /// <summary>
    /// For a hand-written reader: where the reader is (with its stream's offset), to note a value read from here on with
    /// <see cref="Value"/>; -1 while nothing is recorded (so nothing is computed)
    /// </summary>
    public static long Here<TReader>(TReader frame)
        where TReader : IBinaryReadStream
    {
        return On ? Pos(frame) : -1;
    }

    /// <summary>
    /// For a hand-written reader: a value of the object being read, from <paramref name="start"/> (<see cref="Here"/>)
    /// to where the reader is now; optionally only the bits of <paramref name="bitMask"/>
    /// </summary>
    public static void Value<TReader>(TReader frame, string name, long start, ulong bitMask = 0, bool converted = false)
        where TReader : IBinaryReadStream
    {
        if (start < 0 || !On) return;
        Leaf(NodeKind.At, name, start, Pos(frame), bitMask, -1, converted);
    }

    private static void Leaf(NodeKind kind, string? name, long start, long end, ulong bitMask, int index = -1, bool converted = false)
    {
        if (_current is not { RecordStart: not null } c) return;
        var parent = c.Stack.Count > 0 ? c.Stack[^1] : -1;
        if (parent >= 0)
        {
            var p = c.Nodes[parent];
            p.HasChildren = true;
            c.Nodes[parent] = p;
        }
        c.Nodes.Add(new Node
        {
            Parent = parent,
            Kind = kind,
            Name = name,
            Index = index,
            Start = start,
            End = end,
            BitMask = bitMask,
            Converted = converted,
        });
    }

    /// <summary>How many values are being read, nested (taken before a reader is called, for <see cref="CloseTo"/>)</summary>
    public static int Mark() => _current?.Depth ?? 0;

    /// <summary>
    /// A reader returned: what it began and didn't end (a step that returns from inside its field) ends at the position
    /// reached
    /// </summary>
    public static void CloseTo<TReader>(TReader frame, int mark)
        where TReader : IBinaryReadStream
    {
        if (_current is not { } c) return;
        while (c.Stack.Count > mark)
        {
            End(frame);
        }
    }

    /// <summary>A major record's read starts here (at its header). The first one is the record recorded.</summary>
    public static void RecordStart(object record, MutagenFrame frame)
    {
        if (_current is not { RecordStart: null } c) return;
        c.RecordStart = frame.PositionWithOffset;
        c.Record = record;
    }

    /// <summary>The recorded record's content starts here</summary>
    public static void ContentStart(object record, MutagenFrame frame)
    {
        if (_current is not { } c || !ReferenceEquals(c.Record, record) || c.ContentStart != null) return;
        c.ContentStart = frame.PositionWithOffset;
        c.ContentNodeStart = c.Nodes.Count;
    }

    /// <summary>
    /// The recorded record is compressed: its content to read. Handed over decompressed (an overlay's bytes), it is read
    /// as it is. Otherwise it is decompressed into a stream whose positions continue from the header, so the positions
    /// are in "the header, then the content as decompressed".
    /// </summary>
    /// <returns>False if this isn't the recorded record (it is read as usual)</returns>
    public static bool Decompress(object record, MutagenFrame frame, out MutagenFrame content)
    {
        if (_current is not { } c || !ReferenceEquals(c.Record, record) || c.PresenceOnly)
        {
            content = default;
            return false;
        }
        if (c.ContentDecompressed)
        {
            content = frame;
            return true;
        }
        var contentStart = frame.PositionWithOffset;
        var length = frame.ReadUInt32();
        var compressed = frame.ReadBytes(checked((int)frame.Remaining));
        c.Decompressed = Decompression.Decompress(compressed, length);
        content = new MutagenFrame(new MutagenMemoryReadStream(c.Decompressed, frame.MetaData, offsetReference: contentStart));
        return true;
    }
}
