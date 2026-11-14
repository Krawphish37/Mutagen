using System.Buffers.Binary;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;

namespace Mutagen.Bethesda.Plugins.Records;

/// <summary>
/// How a Small or Medium plugin stores the FormIDs of the records it defines itself (games with separated master
/// lists: Starfield). Nothing in the plugin's header tells which; Mutagen reads it from the record headers of a plugin
/// it reads, and writes the plugin back the same way.
/// </summary>
public enum ModOwnFormIdStyle
{
    /// <summary>
    /// Not known: a new mod, or a plugin with no records of its own, or one that mixes both. The writer's default applies.
    /// </summary>
    Default,

    /// <summary>
    /// The plugin's own style: FE + its small index (Small), FD + its medium index (Medium)
    /// </summary>
    OwnStyle,

    /// <summary>
    /// A full index: the plugin's position after its Full masters
    /// </summary>
    FullIndex,
}

/// <summary>
/// What a header's record count and next object ID were computed against when a plugin was read (Starfield;
/// <see cref="RoundTripMixIn.GetHeaderStatsBaseline"/>)
/// </summary>
/// <param name="RecordsRead">How many records the plugin had (the TES4 header and groups not counted)</param>
/// <param name="HighestOwnObjectId">The highest object ID among the plugin's own records (null: it had none)</param>
/// <param name="NextObjectId">The header's next object ID as read</param>
public sealed record ModHeaderStatsBaseline(uint RecordsRead, uint? HighestOwnObjectId, uint NextObjectId);

/// <summary>
/// What Mutagen keeps about a plugin and its records beyond their fields, so that a mod or record rebuilt from
/// another form (JSON, a database) can be written back exactly like the file it came from.
/// </summary>
public static class RoundTripMixIn
{
    /// <summary>
    /// How this mod's own records were numbered in the file it was read from
    /// (<see cref="ModOwnFormIdStyle.Default"/> when not known, e.g. a new mod)
    /// </summary>
    public static ModOwnFormIdStyle GetOwnFormIdStyle(this IModGetter mod)
    {
        return OwnFormIdStyle.Get(mod) switch
        {
            true => ModOwnFormIdStyle.OwnStyle,
            false => ModOwnFormIdStyle.FullIndex,
            null => ModOwnFormIdStyle.Default,
        };
    }

    /// <summary>
    /// Sets how this mod writes the FormIDs of its own records when it is a Small or Medium plugin, for every own FormID
    /// (record headers and links). Without it, a Small or Medium mod writes new own FormIDs in the form of its size (FE /
    /// FD) and keeps the form each FormID read from its file was stored in; a mod rebuilt from another form (JSON) has
    /// no stored forms, so set the style <see cref="GetOwnFormIdStyle"/> reported for the file to write it back the same.
    /// </summary>
    public static void SetOwnFormIdStyle(this IMod mod, ModOwnFormIdStyle style)
    {
        OwnFormIdStyle.Set(mod, style switch
        {
            ModOwnFormIdStyle.OwnStyle => true,
            ModOwnFormIdStyle.FullIndex => false,
            ModOwnFormIdStyle.Default => null,
            _ => throw new ArgumentOutOfRangeException(nameof(style)),
        }, explicitly: true);
    }

    /// <summary>
    /// Starfield: the records that met the overridden-forms rule (TES4 ONAM) when this mod was read from its file,
    /// sorted (null when not known, e.g. a new mod).<br/>
    /// With the default write (<see cref="Mutagen.Bethesda.Plugins.Binary.Parameters.OverriddenFormsOption.Auto"/>),
    /// a mod whose records meeting the rule are still these writes its overridden-forms list as it holds it (the
    /// Creation Kit's list, order and all); otherwise the list is updated by the difference. Store the value next to
    /// the mod's list and give it back with <see cref="SetOverriddenFormsBaseline"/> on a mod rebuilt from the same
    /// data.
    /// </summary>
    public static IReadOnlyList<FormKey>? GetOverriddenFormsBaseline(this IModGetter mod)
    {
        var set = OverriddenFormsBaseline.Get(mod);
        return set == null ? null : OverriddenFormsBaseline.Sorted(set).ToArray();
    }

    /// <summary>
    /// Gives a mod the baseline of <see cref="GetOverriddenFormsBaseline"/> (null removes it: the list is then
    /// computed on write)
    /// </summary>
    public static void SetOverriddenFormsBaseline(this IMod mod, IEnumerable<FormKey>? formKeys)
    {
        OverriddenFormsBaseline.Set(mod, formKeys);
    }

    /// <summary>
    /// Starfield: what the mod's header record count and next object ID (TES4 HEDR) were computed against when it was
    /// read from its file (null when not known, e.g. a new mod).<br/>
    /// With the default write (<see cref="Mutagen.Bethesda.Plugins.Binary.Parameters.RecordCountOption.Auto"/>,
    /// <see cref="Mutagen.Bethesda.Plugins.Binary.Parameters.NextFormIDOption.Auto"/>), a mod with this baseline keeps
    /// the count its header holds, moved by the records added or removed since, and keeps its next object ID while no
    /// own record goes above the highest one read (or when it is the Creation Kit's FF001000, or still above every own
    /// ID). Store the value next to the mod's header and give it back with <see cref="SetHeaderStatsBaseline"/> on a
    /// mod rebuilt from the same data; without it a rebuilt mod is counted as a new one.
    /// </summary>
    public static ModHeaderStatsBaseline? GetHeaderStatsBaseline(this IModGetter mod)
    {
        var snapshot = OverriddenFormsBaseline.GetSnapshot(mod);
        if (snapshot?.Records is not { } records) return null;
        return new ModHeaderStatsBaseline(records, snapshot.MaxOwnId, snapshot.NextObjectId ?? mod.NextFormID);
    }

    /// <summary>
    /// Gives a mod the baseline of <see cref="GetHeaderStatsBaseline"/> (null removes it: the header is then counted
    /// as for a new mod)
    /// </summary>
    public static void SetHeaderStatsBaseline(this IMod mod, ModHeaderStatsBaseline? baseline)
    {
        OverriddenFormsBaseline.SetHeaderStats(mod, baseline?.RecordsRead, baseline?.HighestOwnObjectId, baseline?.NextObjectId);
    }

    private const string SubrecordOrderPrefix = "SO1:";
    private const int SubrecordOrderEntryLength = 13;

    /// <summary>
    /// The order this record's subrecords had in its file, when it differs from the order Mutagen writes
    /// (null otherwise: the record is written in Mutagen's order anyway).<br/>
    /// The value is opaque: store it as it is, and give it back with <see cref="SetSubrecordOrder"/> on a record
    /// rebuilt from the same data. It is only used while the written record still has exactly the subrecords it was
    /// read with; a changed record is written in Mutagen's order.
    /// </summary>
    public static string? GetSubrecordOrder(this IMajorRecordGetter record)
    {
        if (!SubrecordOrder.TryGet(record, out var order)) return null;
        var bytes = new byte[order.Length * SubrecordOrderEntryLength];
        for (int i = 0; i < order.Length; i++)
        {
            var span = bytes.AsSpan(i * SubrecordOrderEntryLength, SubrecordOrderEntryLength);
            BinaryPrimitives.WriteUInt32LittleEndian(span, order[i].Type);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), order[i].Hash);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8), order[i].Field);
            span[12] = order[i].AnyBytes ? (byte)1 : (byte)0;
        }
        return SubrecordOrderPrefix + Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Gives a record the subrecord order of <see cref="GetSubrecordOrder"/> (null removes it)
    /// </summary>
    /// <exception cref="FormatException">The value isn't one GetSubrecordOrder gave</exception>
    public static void SetSubrecordOrder(this IMajorRecord record, string? order)
    {
        if (order == null)
        {
            SubrecordOrder.Remove(record);
            return;
        }
        if (!order.StartsWith(SubrecordOrderPrefix, StringComparison.Ordinal))
        {
            throw new FormatException("Not a subrecord order from GetSubrecordOrder.");
        }
        var bytes = Convert.FromBase64String(order.Substring(SubrecordOrderPrefix.Length));
        if (bytes.Length % SubrecordOrderEntryLength != 0)
        {
            throw new FormatException("Not a subrecord order from GetSubrecordOrder.");
        }
        var entries = new SubrecordOrder.Entry[bytes.Length / SubrecordOrderEntryLength];
        for (int i = 0; i < entries.Length; i++)
        {
            var span = bytes.AsSpan(i * SubrecordOrderEntryLength, SubrecordOrderEntryLength);
            entries[i] = new SubrecordOrder.Entry(
                BinaryPrimitives.ReadUInt32LittleEndian(span),
                BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4)),
                BinaryPrimitives.ReadInt32LittleEndian(span.Slice(8)),
                span[12] != 0);
        }
        SubrecordOrder.Set(record, entries);
    }
    /// <summary>
    /// The record's header FormID as stored in its file, when it isn't the FormID Mutagen writes for its FormKey (e.g.
    /// 03000867, a Small plugin's own record stored with its full index, where Mutagen writes FE000867); null otherwise
    /// (C206).<br/>
    /// Store it with the record's other data, and give it back with <see cref="SetHeaderFormIdAsStored"/> on a record
    /// rebuilt from that data (JSON, a database), so the header is written back as stored. It is only written while it
    /// still reads as the record's FormKey with the master list written.
    /// </summary>
    public static uint? GetHeaderFormIdAsStored(this IMajorRecordGetter record)
    {
        if (RawHeaderFormIds.Get(record) is not { } raw) return null;
        // An overlay record always knows its header as read: only a value Mutagen wouldn't write is reported
        if (record is PluginBinaryOverlay overlay)
        {
            try
            {
                if (overlay._package.MetaData.MasterReferences.GetFormID(record.FormKey).Raw == raw) return null;
            }
            catch (Exception)
            {
                // The FormKey has no FormID with this master list: report the header as read
            }
        }
        return raw;
    }

    /// <summary>
    /// Gives a record the header FormID of <see cref="GetHeaderFormIdAsStored"/> (null removes it, so the header is
    /// written in Mutagen's form) (C206)
    /// </summary>
    public static void SetHeaderFormIdAsStored(this IMajorRecord record, uint? raw)
    {
        RawHeaderFormIds.Set(record, raw);
    }
}
