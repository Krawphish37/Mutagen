using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records.Internals;

namespace Mutagen.Bethesda.Plugins.Binary.Translations;

/// <summary>
/// Keeps the order a major record's subrecords had in its file, for games whose records hold their subrecords in
/// more than one order (<see cref="GameConstants.KeepSubrecordOrder"/>; Starfield). Mutagen writes fields in one fixed
/// order, so without this an unchanged record can come back with the same subrecords in another order.<br/>
/// Reading notes a record only when its subrecords came in an order Mutagen would not write (a field read before a
/// field that comes earlier in Mutagen's order). A full import keeps, per such record, the subrecord types and a hash
/// of each one's bytes; an overlay keeps only a mark, since its bytes are still there to compare with.<br/>
/// Writing a noted record writes it as usual into a buffer first. When every subrecord written has the same type and
/// bytes as the one at the same place among the file's subrecords of that type, the subrecords are written in the
/// file's order. If anything differs (the record was changed), Mutagen's order is kept.
/// </summary>
internal static class SubrecordOrder
{
    // A full import's record -> one entry per subrecord in file order: (type << 32) | hash of its content.
    // An overlay's record -> OverlayMark (its own bytes are read again when it is written).
    private static readonly ConditionalWeakTable<object, ulong[]> Orders = new();
    private static readonly ulong[] OverlayMark = [];

    /// <summary>
    /// Notes a full import's record whose subrecords were read out of Mutagen's order
    /// </summary>
    public static void Remember(object record, ReadOnlySpan<byte> content, GameConstants constants)
    {
        Orders.AddOrUpdate(record, Capture(content, constants));
    }

    /// <summary>
    /// Notes an overlay's record whose subrecords were read out of Mutagen's order
    /// </summary>
    public static void RememberOverlay(object record)
    {
        Orders.AddOrUpdate(record, OverlayMark);
    }

    public static bool TryGet(object record, GameConstants constants, out ulong[] order)
    {
        if (!Orders.TryGetValue(record, out var stored))
        {
            order = null!;
            return false;
        }
        if (ReferenceEquals(stored, OverlayMark))
        {
            if (record is not PluginBinaryOverlay overlay)
            {
                order = null!;
                return false;
            }
            order = Capture(overlay._recordData, constants);
            return true;
        }
        order = stored;
        return true;
    }

    /// <summary>
    /// Writes <paramref name="written"/> (a record's subrecords in Mutagen's order) in the order of <paramref name="order"/>
    /// when the two hold the same subrecords. Returns false, writing nothing, when they don't.
    /// </summary>
    public static bool TryWriteInOrder(ReadOnlySpan<byte> written, ulong[] order, MutagenWriter writer)
    {
        var units = Units(written, writer.MetaData.Constants);
        if (units.Count != order.Length) return false;
        // The n-th written subrecord of a type takes the place of the n-th subrecord of that type in the file
        var byType = new Dictionary<uint, Queue<int>>();
        for (int i = 0; i < units.Count; i++)
        {
            var type = (uint)(units[i].Key >> 32);
            if (!byType.TryGetValue(type, out var queue))
            {
                byType[type] = queue = new Queue<int>();
            }
            queue.Enqueue(i);
        }
        var placed = new int[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            if (!byType.TryGetValue((uint)(order[i] >> 32), out var queue)
                || queue.Count == 0)
            {
                return false;
            }
            var unit = queue.Dequeue();
            if (units[unit].Key != order[i]) return false;
            placed[i] = unit;
        }
        foreach (var unit in placed)
        {
            writer.Write(written.Slice(units[unit].Start, units[unit].Length));
        }
        return true;
    }

    private static ulong[] Capture(ReadOnlySpan<byte> content, GameConstants constants)
    {
        var units = Units(content, constants);
        var ret = new ulong[units.Count];
        for (int i = 0; i < ret.Length; i++)
        {
            ret[i] = units[i].Key;
        }
        return ret;
    }

    private readonly record struct Unit(ulong Key, int Start, int Length);

    // The subrecords of a record's content. An XXXX subrecord (the length of the next one) is kept with the next one.
    private static List<Unit> Units(ReadOnlySpan<byte> content, GameConstants constants)
    {
        var headerLength = constants.SubConstants.HeaderLength;
        var ret = new List<Unit>();
        int pos = 0;
        while (pos + headerLength <= content.Length)
        {
            var start = pos;
            var type = BinaryPrimitives.ReadUInt32LittleEndian(content.Slice(pos));
            int length = BinaryPrimitives.ReadUInt16LittleEndian(content.Slice(pos + 4));
            if (type == RecordTypes.XXXX.TypeInt && length == 4 && pos + headerLength + 4 + headerLength <= content.Length)
            {
                var overflow = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(content.Slice(pos + headerLength)));
                pos += headerLength + 4;
                type = BinaryPrimitives.ReadUInt32LittleEndian(content.Slice(pos));
                length = overflow;
            }
            var contentStart = pos + headerLength;
            var end = Math.Min(content.Length, contentStart + length);
            var hash = Hash(content.Slice(contentStart, end - contentStart));
            ret.Add(new Unit(((ulong)type << 32) | hash, start, end - start));
            pos = end;
        }
        return ret;
    }

    // FNV-1a over the content, with the length mixed in
    private static uint Hash(ReadOnlySpan<byte> bytes)
    {
        uint hash = 2166136261;
        foreach (var b in bytes)
        {
            hash = (hash ^ b) * 16777619;
        }
        return (hash ^ (uint)bytes.Length) * 16777619;
    }
}
