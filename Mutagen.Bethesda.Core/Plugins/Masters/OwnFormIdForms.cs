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
/// is written as before. Record headers are kept by RawHeaderFormIds (C147).<br/>
/// The owner is always a major record read from a plugin (or a copy of one). A subrecord object read or written on
/// its own, outside a record (Condition.CreateFromBinary, a VMAD piece), has nowhere to keep them: its own FormIDs and
/// indices past the list are written in Mutagen's form (C64). No plugin is read or written that way by Mutagen itself.
/// </summary>
internal static class OwnFormIdForms
{
    internal readonly record struct Entry(uint SubrecordType, int Turn, int Offset, uint Raw);

    /// <summary>
    /// What a record keeps. Entries found by looking at its bytes (a full import's notes, a copy of an overlay record) are
    /// any 4 bytes of the subrecords that read as such a FormID, text and numbers included (C40: a quest's alias names
    /// and condition floats in Starfield.esm, read past its empty master list). Until they are checked against where the
    /// record writes FormIDs, <see cref="Package"/> is set; the check keeps only the FormIDs, once, when the record is
    /// first written or exported.
    /// </summary>
    private sealed class Kept
    {
        public Entry[] Entries = Array.Empty<Entry>();
        public IReadOnlySeparatedMasterPackage? Package;
        public GameConstants? Constants;
    }

    private static readonly ConditionalWeakTable<object, Kept> Records = new();

    // Overlay records whose construction read a FormID Mutagen writes differently (C69): an overlay parses some fields
    // when the record is made (a perk's effects), so its write doesn't read those links again and can't notice them
    private static readonly ConditionalWeakTable<object, object> MadeWithNonCanonical = new();

    /// <summary>
    /// Notes an overlay record whose construction read a FormID Mutagen writes differently (PluginBinaryOverlay)
    /// </summary>
    public static void MarkReadNonCanonicalWhenMade(object record) => MadeWithNonCanonical.AddOrUpdate(record, record);

    /// <summary>
    /// Whether an overlay record's construction read a FormID Mutagen writes differently (C69)
    /// </summary>
    public static bool ReadNonCanonicalWhenMade(object record) => MadeWithNonCanonical.TryGetValue(record, out _);

    // The kept entries being checked on this thread: the check's own write of the record uses them as they are
    [ThreadStatic] private static Kept? _checking;

    /// <summary>
    /// A copy of a record (DeepCopy) keeps the forms of the record it copies; a copy of an overlay record takes them from
    /// its bytes (checked against the copy's FormIDs before use)
    /// </summary>
    public static void Copy(IMajorRecordGetter from, object to)
    {
        if (ReferenceEquals(from, to)) return;
        if (from is PluginBinaryOverlay overlay)
        {
            if (Get(from) is { } found)
            {
                Records.AddOrUpdate(to, new Kept
                {
                    Entries = found,
                    Package = overlay._package.MetaData.MasterReferences,
                    Constants = overlay._package.MetaData.Constants,
                });
            }
            else Records.Remove(to);
            return;
        }
        if (Records.TryGetValue(from, out var kept))
        {
            Records.AddOrUpdate(to, new Kept { Entries = kept.Entries, Package = kept.Package, Constants = kept.Constants });
        }
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
        // Any 4 bytes of those subrecords that read so: checked against the record's FormIDs before use (C40)
        var found = Scan(content, constants, separated, ranges, firstRange, includePastList: true);
        if (found == null) Records.Remove(record);
        else Records.AddOrUpdate(record, new Kept { Entries = found, Package = package, Constants = constants });
    }

    /// <summary>
    /// Sets (null or empty: removes) a record's kept forms (RoundTripMixIn.SetOwnLinkFormsAsStored, C210)
    /// </summary>
    public static void Set(object record, Entry[]? entries)
    {
        if (entries == null || entries.Length == 0) Records.Remove(record);
        else Records.AddOrUpdate(record, new Kept { Entries = entries });
    }

    /// <summary>
    /// What a record keeps of its FormIDs stored in another form than Mutagen writes, for RoundTripMixIn (C210): a full
    /// import's notes, or an overlay's own forms and master indices past the list found in its bytes (what a full
    /// import of the same record notes)
    /// </summary>
    public static Entry[]? GetForRoundTrip(IMajorRecordGetter record)
    {
        if (record is not PluginBinaryOverlay overlay) return Records.TryGetValue(record, out var kept) ? Checked(record, kept) : null;
        // An overlay keeps nothing: its record is written as it writes back (stored forms put back) and the FormIDs the
        // write placed are compared with Mutagen's form of the same FormKey (C203's positions), so only FormID fields
        // count, not any 4 bytes that happen to read as one
        var package = overlay._package.MetaData.MasterReferences;
        if (package is not SeparatedMasterPackage) return null;
        return StoredFormIdMixIn.CollectNotAsWritten(record, overlay._package.MetaData.Constants, package)?.ToArray();
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
        return Records.TryGetValue(record, out var kept) ? Checked(record, kept) : null;
    }

    /// <summary>
    /// The kept entries at a place where the record writes a FormID (C40). Entries found in the record's bytes are checked
    /// once: the record is written as it would write back (entries put back), and an entry stays only where a FormID sits
    /// with the same stored value, as an overlay of the same record reports (GetForRoundTrip). Text, numbers and other
    /// bytes that only happen to read as such a FormID go, so they are neither exported nor written into a changed field.
    /// </summary>
    private static Entry[]? Checked(IMajorRecordGetter record, Kept kept)
    {
        var package = kept.Package;
        var constants = kept.Constants;
        // Already checked, given back by SetOwnLinkFormsAsStored, or this check's own write of the record; while a
        // record's FormIDs are being collected (StoredFormIdMixIn), they aren't checked inside that write
        if (package == null || constants == null
            || ReferenceEquals(_checking, kept)
            || Mutagen.Bethesda.Plugins.Binary.Translations.FormIdPositionRecorder.Active)
        {
            return kept.Entries.Length == 0 ? null : kept.Entries;
        }
        var previous = _checking;
        _checking = kept;
        List<StoredFormIdPosition> positions;
        try
        {
            positions = StoredFormIdMixIn.Collect(record, constants, package);
        }
        catch (Exception)
        {
            // The record can't be written: its entries are used as they are, and the write reports the problem
            return kept.Entries.Length == 0 ? null : kept.Entries;
        }
        finally
        {
            _checking = previous;
        }
        var places = new HashSet<(uint Type, int Turn, int Offset, uint Raw)>();
        foreach (var position in positions)
        {
            if (position.Subrecord is not { } type || position.Offset < 0) continue;
            if (position.Location is not (StoredFormIdLocation.Written or StoredFormIdLocation.Searched)) continue;
            places.Add(((uint)type.TypeInt, position.SubrecordIndex, position.Offset, position.Raw));
        }
        var entries = kept.Entries.Where(e => places.Contains((e.SubrecordType, e.Turn, e.Offset, e.Raw))).ToArray();
        kept.Entries = entries;
        kept.Package = null;
        kept.Constants = null;
        return entries.Length == 0 ? null : entries;
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
