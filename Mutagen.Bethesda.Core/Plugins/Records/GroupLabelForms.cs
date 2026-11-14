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
