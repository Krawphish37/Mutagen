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
/// field that comes earlier in Mutagen's order). For such a record it keeps, per subrecord in file order, its type,
/// a hash of its bytes and the field it was read into.<br/>
/// Writing a noted record writes it as usual into a buffer first. Mutagen writes fields in field order, so the file's
/// subrecords sorted by field (keeping the file's order within a field) predict what an unchanged record writes.
/// When the written subrecords are exactly that (same types and bytes, place by place), they are written in the
/// file's order. If anything differs (the record was changed), Mutagen's order is kept.<br/>
/// A copy (DeepCopy, DeepCopyIn) takes over the noted order of the record it copies.
/// </summary>
internal static class SubrecordOrder
{
    internal readonly record struct Entry(uint Type, uint Hash, int Field);

    private static readonly ConditionalWeakTable<object, Entry[]> Orders = new();

    // Field starts of the records being read on this thread (a reader appends, and removes its own when done)
    [ThreadStatic] private static List<(int Offset, int Field)>? _fieldStarts;
    internal static List<(int Offset, int Field)> FieldStarts => _fieldStarts ??= new();

    /// <summary>
    /// Notes a record whose subrecords were read out of Mutagen's order
    /// </summary>
    /// <param name="record">The record</param>
    /// <param name="content">The record's subrecords, as read</param>
    /// <param name="constants">Game constants</param>
    /// <param name="fieldStarts">Where in content each field's subrecords started, with the field's index, in read order,
    /// from index <paramref name="first"/> on</param>
    /// <param name="first">Where this record's entries start in fieldStarts</param>
    public static void Remember(
        object record,
        ReadOnlySpan<byte> content,
        GameConstants constants,
        List<(int Offset, int Field)> fieldStarts,
        int first)
    {
        var units = Units(content, constants);
        var entries = new Entry[units.Count];
        var call = first - 1;
        for (int i = 0; i < entries.Length; i++)
        {
            while (call + 1 < fieldStarts.Count && fieldStarts[call + 1].Offset <= units[i].SubrecordStart)
            {
                call++;
            }
            entries[i] = new Entry(units[i].Type, units[i].Hash, call < first ? -1 : fieldStarts[call].Field);
        }
        Orders.AddOrUpdate(record, entries);
    }

    /// <summary>
    /// A copy of a record takes over its noted order; one of a record that has none has none
    /// </summary>
    public static void Copy(object from, object to)
    {
        if (ReferenceEquals(from, to)) return;
        if (Orders.TryGetValue(from, out var entries))
        {
            Orders.AddOrUpdate(to, entries);
        }
        else
        {
            Orders.Remove(to);
        }
    }

    public static bool TryGet(object record, out Entry[] order) => Orders.TryGetValue(record, out order!);

    /// <summary>
    /// Writes <paramref name="written"/> (a record's subrecords in Mutagen's order) in the order of <paramref name="order"/>
    /// when they are the record's subrecords unchanged. Returns false, writing nothing, when they aren't.
    /// </summary>
    public static bool TryWriteInOrder(ReadOnlySpan<byte> written, Entry[] order, MutagenWriter writer)
    {
        var units = Units(written, writer.MetaData.Constants);
        if (units.Count != order.Length) return false;
        // predicted[k]: the file's subrecord that Mutagen writes k-th (by field, then as read)
        var predicted = Enumerable.Range(0, order.Length).OrderBy(i => order[i].Field).ToArray();
        var place = new int[order.Length];
        for (int k = 0; k < predicted.Length; k++)
        {
            var expected = order[predicted[k]];
            if (units[k].Type != expected.Type || units[k].Hash != expected.Hash) return false;
            place[predicted[k]] = k;
        }
        foreach (var k in place)
        {
            writer.Write(written.Slice(units[k].Start, units[k].Length));
        }
        return true;
    }

    private readonly record struct Unit(uint Type, uint Hash, int Start, int SubrecordStart, int Length);

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
            ret.Add(new Unit(type, Hash(content.Slice(contentStart, end - contentStart)), start, pos, end - start));
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
