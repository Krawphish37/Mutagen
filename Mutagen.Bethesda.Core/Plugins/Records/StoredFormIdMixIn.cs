using System.Buffers.Binary;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records.Internals;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Records;

/// <summary>
/// The form a FormID is stored in (games with separated master lists: Starfield)
/// </summary>
public enum StoredFormIdForm
{
    /// <summary>A full index: a full master's place, or the plugin's own place after the full masters</summary>
    Full,

    /// <summary>FE + a small index: a Small master, or a Small plugin's own FE form</summary>
    Small,

    /// <summary>FD + a medium index: a Medium master, or a Medium plugin's own FD form</summary>
    Medium,

    /// <summary>
    /// An index past the plugin's masters of that size and past its own place in that form (or the own place of a form
    /// the plugin's size doesn't have). The game, and Mutagen, read it as the plugin itself.
    /// </summary>
    PastTheList,
}

/// <summary>
/// How a FormID position was found
/// </summary>
public enum StoredFormIdLocation
{
    /// <summary>The record header's FormID</summary>
    Header,

    /// <summary>Where the record's write puts the FormID (from the record definition)</summary>
    Written,

    /// <summary>
    /// The write put this FormID through a buffer whose place couldn't be followed: found by looking for a stored value
    /// that reads as the same FormKey in the record's bytes
    /// </summary>
    Searched,

    /// <summary>Written by the record, but not found in its bytes (Offset is -1, Raw is the value Mutagen would write)</summary>
    NotFound,
}

/// <summary>
/// One FormID of a record as it is stored
/// </summary>
/// <param name="Subrecord">The subrecord holding it; null for the record header's FormID</param>
/// <param name="SubrecordIndex">Its subrecord's turn among the record's subrecords of that type, from 0</param>
/// <param name="Offset">Its offset in the subrecord's content (after the subrecord header); -1 when not found</param>
/// <param name="Raw">The stored value</param>
/// <param name="Form">The form it is stored in</param>
/// <param name="IsOwn">Whether it reads as the plugin itself (its own place, or past the list)</param>
/// <param name="FormKey">The FormKey it reads as</param>
/// <param name="Location">How the position was found</param>
public sealed record StoredFormIdPosition(
    RecordType? Subrecord,
    int SubrecordIndex,
    int Offset,
    uint Raw,
    StoredFormIdForm Form,
    bool IsOwn,
    FormKey FormKey,
    StoredFormIdLocation Location);

/// <summary>
/// Where a record's FormIDs sit in its bytes and how each is stored (C203)
/// </summary>
public static class StoredFormIdMixIn
{
    /// <summary>
    /// Lists a record's FormIDs as they are stored: the header's, then every link and FormKey its subrecords hold, with
    /// the subrecord, the turn among that type, the offset, the stored value and form, and the FormKey it reads as.<br/>
    /// The positions come from the record definitions: the record is written (as it would write back, stored forms kept)
    /// into a scratch buffer, and each FormID the writer puts in it is noted where it goes. Nothing is computed until
    /// this is called, and nothing is kept.<br/>
    /// Only the record is looked at, not the records in its child groups (a cell's references, a quest's dialogue).
    /// Bytes no field reads (unknown subrecords, kept as read) aren't looked at.
    /// </summary>
    /// <param name="record">A record of <paramref name="mod"/>, from either reader</param>
    /// <param name="mod">The plugin the record was read from (its masters and size give each FormID's form)</param>
    /// <param name="masterFlagsLookup">The size of each master; needed for a record from a full import of a plugin with
    /// separated master lists. A record from an overlay uses the package it was read with.</param>
    public static IReadOnlyList<StoredFormIdPosition> GetStoredFormIds(
        this IMajorRecordGetter record,
        IModGetter mod,
        IReadOnlyCache<IModMasterStyledGetter, ModKey>? masterFlagsLookup = null)
    {
        var constants = GameConstants.Get(mod.GameRelease);
        IReadOnlySeparatedMasterPackage package;
        if (record is PluginBinaryOverlay overlay && masterFlagsLookup == null)
        {
            package = overlay._package.MetaData.MasterReferences;
        }
        else
        {
            package = SeparatedMasterPackage.Factory(
                mod.GameRelease,
                mod.ModKey,
                mod.GetMasterStyle(),
                new MasterReferenceCollection(mod.ModKey, mod.MasterReferences),
                masterFlagsLookup,
                OwnFormIdStyle.Get(mod),
                OwnFormIdStyle.GetExplicit(mod));
        }

        var ret = new List<StoredFormIdPosition>();
        var header = RawHeaderFormIds.Get(record) is { } kept && package.GetFormKey(new FormID(kept), reference: false) == record.FormKey
            ? kept
            : package.GetFormID(record.FormKey).Raw;
        ret.Add(Make(package, null, 0, 0, header, StoredFormIdLocation.Header));

        var recorder = FormIdPositionRecorder.Begin(record);
        try
        {
            using var stream = new MemoryStream();
            using var writer = new MutagenWriter(stream, new WritingBundle(constants)
            {
                MasterReferences = package.Raw,
                SeparatedMasterPackage = package,
                CleanNulls = false,
            }, dispose: false);
            try
            {
                ((IBinaryItem)record).WriteToBinary(writer);
            }
            catch (Exception ex) when (Caught(ex))
            {
                // The record's subrecords were captured; its child groups aren't needed
            }
        }
        finally
        {
            FormIdPositionRecorder.End();
        }
        if (recorder.Content is not { } content) return ret;

        // Each subrecord's type, turn among its type and content span
        var subrecords = Subrecords(content, constants);
        var claimed = new HashSet<int>();
        var unplaced = new List<FormKey>();
        foreach (var (position, formKey) in recorder.FormIds)
        {
            if (position is { } pos && Place(subrecords, pos) is { } place)
            {
                claimed.Add(pos);
                var raw = BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(pos));
                ret.Add(Make(package, place.Type, place.Turn, pos - place.ContentStart, raw, StoredFormIdLocation.Written));
            }
            else
            {
                unplaced.Add(formKey);
            }
        }
        foreach (var formKey in unplaced)
        {
            if (Search(content, subrecords, claimed, package, formKey) is { } found)
            {
                claimed.Add(found.Position);
                var raw = BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(found.Position));
                ret.Add(Make(package, found.Sub.Type, found.Sub.Turn, found.Position - found.Sub.ContentStart, raw,
                    StoredFormIdLocation.Searched));
            }
            else
            {
                var raw = package.GetFormID(formKey).Raw;
                var form = Classify(package, raw, out var own);
                ret.Add(new StoredFormIdPosition(null, 0, -1, raw, form, own, formKey, StoredFormIdLocation.NotFound));
            }
        }
        return ret;
    }

    private static bool Caught(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is FormIdPositionRecorder.Done) return true;
            if (e is AggregateException agg && agg.InnerExceptions.Any(Caught)) return true;
        }
        return false;
    }

    private static StoredFormIdPosition Make(IReadOnlySeparatedMasterPackage package, RecordType? type, int turn, int offset,
        uint raw, StoredFormIdLocation location)
    {
        var form = Classify(package, raw, out var own);
        var formKey = raw == 0 ? FormKey.Null : package.GetFormKey(new FormID(raw), reference: type != null);
        return new StoredFormIdPosition(type, turn, offset, raw, form, own, formKey, location);
    }

    private static StoredFormIdForm Classify(IReadOnlySeparatedMasterPackage package, uint raw, out bool own)
    {
        if (package is SeparatedMasterPackage separated) return separated.Classify(raw, out own);
        // Not separated: one list of masters, the plugin after them
        var index = raw >> 24;
        var masters = (uint)package.Raw.Masters.Count;
        own = index >= masters && raw != 0;
        return index > masters ? StoredFormIdForm.PastTheList : StoredFormIdForm.Full;
    }

    private readonly record struct Sub(RecordType Type, int Turn, int ContentStart, int End);

    private static List<Sub> Subrecords(byte[] content, GameConstants constants)
    {
        var ret = new List<Sub>();
        var turns = new Dictionary<int, int>();
        var headerLength = constants.SubConstants.HeaderLength;
        int pos = 0;
        while (pos + headerLength <= content.Length)
        {
            var type = BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(pos));
            int length = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(pos + 4));
            if (type == RecordTypes.XXXX.TypeInt && length == 4 && pos + headerLength + 4 + headerLength <= content.Length)
            {
                length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(pos + headerLength)));
                pos += headerLength + 4;
                type = BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(pos));
            }
            var contentStart = pos + headerLength;
            var end = Math.Min(content.Length, contentStart + length);
            var turn = turns.TryGetValue(type, out var t) ? t : 0;
            turns[type] = turn + 1;
            ret.Add(new Sub(new RecordType(type), turn, contentStart, end));
            pos = end;
        }
        return ret;
    }

    private static Sub? Place(List<Sub> subrecords, int position)
    {
        foreach (var s in subrecords)
        {
            if (position >= s.ContentStart && position + 4 <= s.End) return s;
        }
        return null;
    }

    private static (int Position, Sub Sub)? Search(byte[] content, List<Sub> subrecords, HashSet<int> claimed,
        IReadOnlySeparatedMasterPackage package, FormKey formKey)
    {
        foreach (var s in subrecords)
        {
            for (int i = s.ContentStart; i + 4 <= s.End; i++)
            {
                if (claimed.Contains(i)) continue;
                var raw = BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(i));
                if (raw == 0) continue;
                FormKey read;
                try
                {
                    read = package.GetFormKey(new FormID(raw), reference: true);
                }
                catch (Exception)
                {
                    continue;
                }
                if (read == formKey) return (i, s);
            }
        }
        return null;
    }
}
