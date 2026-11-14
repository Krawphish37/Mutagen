using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;

namespace Mutagen.Bethesda.Plugins.Records;

/// <summary>
/// Two records of one group that read as the same FormKey. A plugin can hold them: MissionsGalore.esm has GLOB 02000867
/// and GLOB 03000867, and with two masters both mean 000867:MissionsGalore.esm (an index past the master list is the
/// plugin itself). The game loads records in file order, so the later one is the form the game ends up with.<br/>
/// Mutagen reads such a plugin instead of throwing: the group holds the later record (lookups give what the game gives),
/// and the earlier one is kept aside (<see cref="GetShadowedDuplicates"/>) and written back where it was, so the plugin
/// writes back unchanged. <see cref="Found"/> is told about each one.
/// </summary>
public static class DuplicateRecords
{
    /// <summary>
    /// Called for each record that another record of the same group replaced while reading (default: nothing)
    /// </summary>
    public static Action<string> Found { get; set; } = _ => { };

    /// <summary>
    /// The records of a mod read from a file that a later record with the same FormKey replaced in their group, in the
    /// order they were found. They are written back in their place when the mod is written.
    /// </summary>
    public static IReadOnlyList<IMajorRecordGetter> GetShadowedDuplicates(this IModGetter mod)
    {
        return Mods.TryGetValue(mod, out var set) ? set.Records : Array.Empty<IMajorRecordGetter>();
    }

    internal sealed class Set
    {
        private readonly object _lock = new();
        private readonly List<IMajorRecordGetter> _records = new();
        private readonly Dictionary<FormKey, List<IMajorRecordGetter>> _after = new();
        private readonly Dictionary<FormKey, List<IMajorRecordGetter>> _before = new();

        public IReadOnlyList<IMajorRecordGetter> Records
        {
            get { lock (_lock) return _records.ToArray(); }
        }

        private volatile int _count;

        public bool Any => _count > 0;

        /// <param name="shadowed">The earlier record</param>
        /// <param name="after">The record it followed in the file, if any</param>
        /// <param name="before">Otherwise the record that followed it</param>
        public void Add(IMajorRecordGetter shadowed, FormKey? after, FormKey? before, ModKey modKey, Type groupType)
        {
            lock (_lock)
            {
                // An overlay reads a group again each time it is opened: note each pair once
                if (_records.Any(r => r.FormKey == shadowed.FormKey && r.GetType() == shadowed.GetType()
                        && (after is { } fa ? _after.TryGetValue(fa, out var la) && la.Any(x => x.FormKey == shadowed.FormKey)
                            : before is { } fb && _before.TryGetValue(fb, out var lb) && lb.Any(x => x.FormKey == shadowed.FormKey))))
                {
                    return;
                }
                _records.Add(shadowed);
                _count = _records.Count;
                if (after is { } a)
                {
                    (_after.TryGetValue(a, out var l) ? l : _after[a] = new()).Add(shadowed);
                }
                else if (before is { } b)
                {
                    (_before.TryGetValue(b, out var l) ? l : _before[b] = new()).Add(shadowed);
                }
            }
            Found($"{modKey}: two {groupType.Name} records read as {shadowed.FormKey} ({shadowed.EditorID}); the later one is kept in the group, the earlier one aside, and both are written back");
        }

        public IReadOnlyList<IMajorRecordGetter>? After(FormKey formKey)
        {
            lock (_lock) return _after.TryGetValue(formKey, out var l) ? l.ToArray() : null;
        }

        public IReadOnlyList<IMajorRecordGetter>? Before(FormKey formKey)
        {
            lock (_lock) return _before.TryGetValue(formKey, out var l) ? l.ToArray() : null;
        }
    }

    private static readonly ConditionalWeakTable<object, Set> Mods = new();

    /// <summary>
    /// The set a mod being read collects its duplicates in
    /// </summary>
    internal static Set Attach(object mod, ParsingMeta meta)
    {
        var set = meta.Duplicates ??= new Set();
        Mods.AddOrUpdate(mod, set);
        return set;
    }

    internal static void Copy(object from, object to)
    {
        if (ReferenceEquals(from, to)) return;
        if (Mods.TryGetValue(from, out var set)) Mods.AddOrUpdate(to, set);
        else Mods.Remove(to);
    }

    // The mod being written (its duplicates), for the record writer
    private static readonly AsyncLocal<Set?> Writing = new();
    [ThreadStatic] private static bool _emitting;

    internal static void BeginWrite(IModGetter mod)
    {
        // An overlay finds its duplicates as its groups are first read, which can be during this write
        Writing.Value = Mods.TryGetValue(mod, out var set) ? set : null;
    }

    internal static void WriteBefore(IMajorRecordGetter record, MutagenWriter writer)
    {
        if (_emitting || Writing.Value is not { Any: true } set) return;
        Emit(set.Before(record.FormKey), writer);
    }

    internal static void WriteAfter(IMajorRecordGetter record, MutagenWriter writer)
    {
        if (_emitting || Writing.Value is not { Any: true } set) return;
        Emit(set.After(record.FormKey), writer);
    }

    private static void Emit(IReadOnlyList<IMajorRecordGetter>? records, MutagenWriter writer)
    {
        if (records == null) return;
        _emitting = true;
        try
        {
            foreach (var r in records)
            {
                ((IBinaryItem)r).WriteToBinary(writer);
            }
        }
        finally
        {
            _emitting = false;
        }
    }
}

/// <summary>
/// A record's header FormID as read, when it isn't the one Mutagen writes for its FormKey (an index past the master
/// list: 03000867 in a plugin with two masters is 000867 of the plugin itself, written 02000867). The header is
/// written back as read while that FormID still means the same record.
/// </summary>
internal static class RawHeaderFormIds
{
    private static readonly ConditionalWeakTable<object, StrongBox<uint>> Records = new();

    /// <summary>
    /// Notes a record read in full, when its header FormID isn't the one written for its FormKey
    /// </summary>
    public static void Remember(IMajorRecordGetter record, uint raw, ParsingMeta meta)
    {
        uint written;
        try
        {
            written = meta.MasterReferences.GetFormID(record.FormKey).Raw;
        }
        catch (Exception)
        {
            return;
        }
        if (written == raw) return;
        Records.AddOrUpdate(record, new StrongBox<uint>(raw));
    }

    /// <summary>
    /// Sets (null: removes) a record's header FormID as stored (RoundTripMixIn.SetHeaderFormIdAsStored, C206)
    /// </summary>
    public static void Set(IMajorRecordGetter record, uint? raw)
    {
        if (raw is { } value) Records.AddOrUpdate(record, new StrongBox<uint>(value));
        else Records.Remove(record);
    }

    public static void Copy(IMajorRecordGetter from, IMajorRecordGetter to)
    {
        if (ReferenceEquals(from, to)) return;
        if (Get(from) is { } raw) Records.AddOrUpdate(to, new StrongBox<uint>(raw));
        else Records.Remove(to);
    }

    public static uint? Get(IMajorRecordGetter record)
    {
        if (record is MajorRecordBinaryOverlay overlay) return overlay.FormIdAsRead;
        return Records.TryGetValue(record, out var box) ? box.Value : null;
    }

    /// <summary>
    /// Writes a child group's label for its parent record (cell, worldspace, quest, topic children) in the form the parent's
    /// header is written in: the FormID as read where it is kept (C163; e.g. a small plugin's topic 04000835 stored with
    /// its full index), else Mutagen's. A label stored in a form of its own is written as stored (GroupLabelForms, C188).
    /// </summary>
    public static void WriteGroupLabel(MutagenWriter writer, IMajorRecordGetter parent, int groupType)
    {
        if (writer.MetaData.SeparatedMasterPackage is { } labelPackage
            && GroupLabelForms.Get(parent, groupType) is { } label
            && labelPackage.GetFormKey(new FormID(label), reference: false) == parent.FormKey)
        {
            writer.Write(label);
            return;
        }
        if (writer.MetaData.SeparatedMasterPackage is { } package
            && Get(parent) is { } raw
            && package.GetFormID(parent.FormKey).Raw != raw
            && package.GetFormKey(new FormID(raw), reference: false) == parent.FormKey)
        {
            writer.Write(raw);
            return;
        }
        FormKeyBinaryTranslation.Instance.Write(writer, parent);
    }

    /// <summary>
    /// After a record's header is written: puts back the FormID as read if it differs and still means the same record
    /// </summary>
    public static void Restore(IMajorRecordGetter record, MutagenWriter writer, long headerPos)
    {
        if (Get(record) is not { } raw) return;
        var package = writer.MetaData.SeparatedMasterPackage;
        if (package == null) return;
        if (package.GetFormID(record.FormKey).Raw == raw) return;
        if (package.GetFormKey(new FormID(raw), reference: false) != record.FormKey) return;
        var end = writer.Position;
        writer.Position = headerPos + writer.MetaData.Constants.MajorConstants.FormIDLocationOffset;
        writer.Write(raw);
        writer.Position = end;
    }
}
