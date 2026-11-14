using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Mutagen.Bethesda.Plugins.Masters;

namespace Mutagen.Bethesda.Plugins.Records;

/// <summary>
/// Child-group labels (cell, worldspace, quest, topic children) stored in another form than their parent record's
/// header (C188). A label names its parent by FormID; Mutagen writes it in the form of the parent's header
/// (RawHeaderFormIds.WriteGroupLabel). Some plugins store the two differently: RoleplayersAlternateStart.esm (Medium)
/// has its records in FD form and 336 child-group labels in full-index form (02001DC3 for FD001DC3). Such a label is
/// kept, per parent and group type, and written back while it still names the parent; a new group, or a parent whose
/// label was stored like its header, gets the header's form. Only labels that differ are kept.
/// </summary>
internal static class GroupLabelForms
{
    private static readonly ConditionalWeakTable<object, Dictionary<int, uint>> Parents = new();

    /// <summary>
    /// Notes a child group's label as read, when it isn't in the form the parent's header was read in
    /// </summary>
    public static void Remember(IMajorRecordGetter parent, int groupType, uint raw, IReadOnlySeparatedMasterPackage package)
    {
        uint header;
        try
        {
            header = RawHeaderFormIds.Get(parent) is { } h && package.GetFormKey(new FormID(h), reference: false) == parent.FormKey
                ? h
                : package.GetFormID(parent.FormKey).Raw;
            if (raw == header || package.GetFormKey(new FormID(raw), reference: false) != parent.FormKey) return;
        }
        catch (Exception)
        {
            return;
        }
        var labels = Parents.GetValue(parent, _ => new Dictionary<int, uint>());
        lock (labels)
        {
            labels[groupType] = raw;
        }
    }

    public static uint? Get(IMajorRecordGetter parent, int groupType)
    {
        if (!Parents.TryGetValue(parent, out var labels)) return null;
        lock (labels)
        {
            return labels.TryGetValue(groupType, out var raw) ? raw : null;
        }
    }

    private const string Prefix = "GL1:";

    /// <summary>
    /// The kept labels as an opaque value (RoundTripMixIn.GetGroupLabelFormsAsStored, C223): "GL1:" and base64 of
    /// (group type, stored label) pairs, 4 bytes each, by group type; null when there are none
    /// </summary>
    public static string? Export(IMajorRecordGetter parent)
    {
        if (!Parents.TryGetValue(parent, out var labels)) return null;
        KeyValuePair<int, uint>[] pairs;
        lock (labels)
        {
            pairs = labels.OrderBy(x => x.Key).ToArray();
        }
        if (pairs.Length == 0) return null;
        var bytes = new byte[pairs.Length * 8];
        for (int i = 0; i < pairs.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8 * i), pairs[i].Key);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8 * i + 4), pairs[i].Value);
        }
        return Prefix + Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Gives a record the labels of <see cref="Export"/> (null removes them)
    /// </summary>
    /// <exception cref="FormatException">The value isn't one Export gave</exception>
    public static void Import(IMajorRecordGetter parent, string? value)
    {
        if (value == null)
        {
            Parents.Remove(parent);
            return;
        }
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new FormatException($"Not stored group labels from GetGroupLabelFormsAsStored: {value}");
        }
        var bytes = Convert.FromBase64String(value.Substring(Prefix.Length));
        if (bytes.Length == 0 || bytes.Length % 8 != 0)
        {
            throw new FormatException($"Not stored group labels from GetGroupLabelFormsAsStored: {value}");
        }
        var labels = new Dictionary<int, uint>();
        for (int i = 0; i < bytes.Length / 8; i++)
        {
            labels[BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8 * i))] =
                BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8 * i + 4));
        }
        Parents.AddOrUpdate(parent, labels);
    }

    /// <summary>
    /// A copy of a record (DeepCopy) keeps the labels kept for the record it copies
    /// </summary>
    public static void Copy(object from, object to)
    {
        if (ReferenceEquals(from, to)) return;
        if (Parents.TryGetValue(from, out var labels))
        {
            lock (labels)
            {
                Parents.AddOrUpdate(to, new Dictionary<int, uint>(labels));
            }
        }
        else
        {
            Parents.Remove(to);
        }
    }
}
