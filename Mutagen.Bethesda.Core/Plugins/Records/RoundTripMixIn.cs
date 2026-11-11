using System.Buffers.Binary;
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
    /// Sets how this mod writes the FormIDs of its own records when it is a Small or Medium plugin.
    /// A mod read from a file already has the file's style; a mod built some other way has
    /// <see cref="ModOwnFormIdStyle.Default"/> until this is set.
    /// </summary>
    public static void SetOwnFormIdStyle(this IMod mod, ModOwnFormIdStyle style)
    {
        OwnFormIdStyle.Set(mod, style switch
        {
            ModOwnFormIdStyle.OwnStyle => true,
            ModOwnFormIdStyle.FullIndex => false,
            ModOwnFormIdStyle.Default => null,
            _ => throw new ArgumentOutOfRangeException(nameof(style)),
        });
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
}
