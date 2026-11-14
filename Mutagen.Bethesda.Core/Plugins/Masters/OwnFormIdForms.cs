using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Internals;

namespace Mutagen.Bethesda.Plugins.Masters;

/// <summary>
/// The form each own FormID in a record's links was stored in, for Small and Medium Starfield plugins that store them
/// in more than one form (C163, decision 6.6). Such a plugin can refer to its own object 866 as 03000866 (its full
/// index, as the Creation Kit writes a small .esm) or FE001866 (the light form, as SF1Edit writes new records), in one
/// file and even in one record: SF1Edit keeps what it doesn't touch. The game and both tools read both forms the same
/// way. Mutagen writes one form; this keeps the other so an unchanged link is written back as stored.<br/>
/// What is kept: per record, each own FormID read in a form other than the one Mutagen writes, by subrecord type, its
/// turn among the record's subrecords of that type and its offset in the subrecord. Only these differences are kept,
/// so a plugin that stores its own FormIDs one way (every Full plugin, Starfield.esm included) keeps nothing.
/// A full import notes them while reading (only subrecords whose read met such a FormID); an overlay, which holds the
/// record's bytes anyway, finds them when the record is written.<br/>
/// Written back: when the record writes, at that place, Mutagen's form of the same own FormID (the link is unchanged)
/// and the stored form still means the plugin itself with the master list written. A changed link, a new one and every
/// link of a new mod get Mutagen's form. A master index past the master list (decision 6.1) is not an own form here and
/// is written as before. Record headers are kept by RawHeaderFormIds (C147).
/// </summary>
internal static class OwnFormIdForms
{
    internal readonly record struct Entry(uint SubrecordType, int Turn, int Offset, uint Raw);

    private static readonly ConditionalWeakTable<object, Entry[]> Records = new();

    /// <summary>
    /// A copy of a record (DeepCopy) keeps the forms of the record it copies; a copy of an overlay record takes them from
    /// its bytes
    /// </summary>
    public static void Copy(IMajorRecordGetter from, object to)
    {
        if (ReferenceEquals(from, to)) return;
        if (Get(from) is { } entries) Records.AddOrUpdate(to, entries);
        else Records.Remove(to);
    }

    /// <summary>
    /// Full import: notes the own FormIDs stored in another form in the given ranges of the record's content
    /// </summary>
    public static void Remember(
        object record,
        ReadOnlySpan<byte> content,
        GameConstants constants,
        IReadOnlySeparatedMasterPackage package,
        List<(int From, int To)> ranges,
        int firstRange)
    {
        if (package is not SeparatedMasterPackage separated) return;
        // Only subrecords whose read met a FormID Mutagen writes differently: own forms (Small/Medium) and master
        // indices past the list (any size, C189)
        var found = Scan(content, constants, separated, ranges, firstRange, includePastList: true);
        if (found == null) Records.Remove(record);
        else Records.AddOrUpdate(record, found);
    }

    /// <summary>
    /// The kept forms of a record about to be written: noted by a full import, or found in an overlay's bytes
    /// </summary>
    public static Entry[]? Get(IMajorRecordGetter record)
    {
        if (record is PluginBinaryOverlay overlay)
        {
            if (overlay._package.MetaData.MasterReferences is not SeparatedMasterPackage { HasOwnFormVariants: true } separated)
            {
                return null;
            }
            return Scan(overlay._recordData, overlay._package.MetaData.Constants, separated, ranges: null, firstRange: 0, includePastList: false);
        }
        return Records.TryGetValue(record, out var entries) ? entries : null;
    }

    /// <summary>
    /// An overlay record whose write read a FormID Mutagen writes differently: its own FormIDs in another form and its
    /// master indices past the list (C189), found in its bytes
    /// </summary>
    public static Entry[]? GetIncludingPastList(IMajorRecordGetter record)
    {
        if (record is not PluginBinaryOverlay overlay
            || overlay._package.MetaData.MasterReferences is not SeparatedMasterPackage separated)
        {
            return null;
        }
        return Scan(overlay._recordData, overlay._package.MetaData.Constants, separated, ranges: null, firstRange: 0, includePastList: true);
    }

    private static Entry[]? Scan(
        ReadOnlySpan<byte> content,
        GameConstants constants,
        SeparatedMasterPackage package,
        List<(int From, int To)>? ranges,
        int firstRange,
        bool includePastList)
    {
        List<Entry>? ret = null;
        var turns = new Dictionary<uint, int>();
        var headerLength = constants.SubConstants.HeaderLength;
        int pos = 0;
        while (pos + headerLength <= content.Length)
        {
            var subrecordStart = pos;
            var type = BinaryPrimitives.ReadUInt32LittleEndian(content.Slice(pos));
            int length = BinaryPrimitives.ReadUInt16LittleEndian(content.Slice(pos + 4));
            if (type == RecordTypes.XXXX.TypeInt && length == 4 && pos + headerLength + 4 + headerLength <= content.Length)
            {
                length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(content.Slice(pos + headerLength)));
                pos += headerLength + 4;
                type = BinaryPrimitives.ReadUInt32LittleEndian(content.Slice(pos));
            }
            var contentStart = pos + headerLength;
            var end = Math.Min(content.Length, contentStart + length);
            var turn = turns.TryGetValue(type, out var t) ? t : 0;
            turns[type] = turn + 1;
            if (ranges == null || InRanges(ranges, firstRange, subrecordStart))
            {
                var sub = content.Slice(contentStart, end - contentStart);
                for (int i = 0; i + 4 <= sub.Length; i++)
                {
                    var raw = BinaryPrimitives.ReadUInt32LittleEndian(sub.Slice(i));
                    if (package.IsOwnFormVariant(raw, includePastList))
                    {
                        (ret ??= new()).Add(new Entry(type, turn, i, raw));
                    }
                }
            }
            pos = end;
        }
        return ret?.ToArray();
    }

    private static bool InRanges(List<(int From, int To)> ranges, int first, int position)
    {
        for (int r = first; r < ranges.Count; r++)
        {
            if (position >= ranges[r].From && position < ranges[r].To) return true;
        }
        return false;
    }

    /// <summary>
    /// Puts the kept forms back into a record's subrecords as written (<paramref name="written"/>, edited in place),
    /// where the record still writes Mutagen's form of the same own FormID
    /// </summary>
    public static void Restore(Span<byte> written, Entry[] entries, MutagenWriter writer)
    {
        if (writer.MetaData.SeparatedMasterPackage is not SeparatedMasterPackage package) return;
        var constants = writer.MetaData.Constants;
        var headerLength = constants.SubConstants.HeaderLength;
        var turns = new Dictionary<uint, int>();
        int pos = 0;
        while (pos + headerLength <= written.Length)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(written.Slice(pos));
            int length = BinaryPrimitives.ReadUInt16LittleEndian(written.Slice(pos + 4));
            if (type == RecordTypes.XXXX.TypeInt && length == 4 && pos + headerLength + 4 + headerLength <= written.Length)
            {
                length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(written.Slice(pos + headerLength)));
                pos += headerLength + 4;
                type = BinaryPrimitives.ReadUInt32LittleEndian(written.Slice(pos));
            }
            var contentStart = pos + headerLength;
            var end = Math.Min(written.Length, contentStart + length);
            var turn = turns.TryGetValue(type, out var t) ? t : 0;
            turns[type] = turn + 1;
            foreach (var entry in entries)
            {
                if (entry.SubrecordType != type || entry.Turn != turn) continue;
                var at = contentStart + entry.Offset;
                if (at + 4 > end) continue;
                // Still a form of the plugin's own FormID with the masters written, and the record still writes Mutagen's
                // form of that same FormID there
                if (!package.IsOwnFormVariant(entry.Raw, includePastList: true)) continue;
                var formKey = package.GetFormKey(new FormID(entry.Raw), reference: true);
                if (BinaryPrimitives.ReadUInt32LittleEndian(written.Slice(at)) != package.GetFormID(formKey).Raw) continue;
                BinaryPrimitives.WriteUInt32LittleEndian(written.Slice(at), entry.Raw);
            }
            pos = end;
        }
    }
}
