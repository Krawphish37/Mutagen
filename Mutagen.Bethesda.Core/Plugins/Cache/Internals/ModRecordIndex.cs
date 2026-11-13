using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Mapping;
using Mutagen.Bethesda.Plugins.Utility;

namespace Mutagen.Bethesda.Plugins.Cache.Internals;

/// <summary>
/// Lets an untyped link lookup (a FormKey without a record type) on a plugin read as an overlay find the record's type
/// from the plugin's record headers (RecordTypeInfoCacheReader.TryGetRecordType), and then use that one type's cache,
/// instead of building one cache of every record of the plugin (C177: 14-16.5 s and 8.4 GB for Starfield.esm).
/// Only overlays are registered: they are read-only, so the file's headers describe them. A mod in memory can gain or
/// lose records, so its untyped lookups keep the cache of every record.
/// </summary>
internal static class ModRecordIndex
{
    private static readonly ConditionalWeakTable<IModGetter, RecordTypeInfoCacheReader> Indexes = new();
    private static readonly ConcurrentDictionary<GameCategory, IReadOnlyDictionary<RecordType, Type[]>> GetterTypes = new();

    public static void Register(IModGetter mod, RecordTypeInfoCacheReader reader) => Indexes.AddOrUpdate(mod, reader);

    public static bool TryGet(IModGetter mod, out RecordTypeInfoCacheReader reader) => Indexes.TryGetValue(mod, out reader!);

    /// <summary>
    /// The getter types a record type is read as in a game (e.g. GMST: each game setting type), the ones that stand for
    /// one record type first
    /// </summary>
    public static IReadOnlyList<Type> GetterTypesFor(GameCategory category, RecordType recordType)
    {
        var map = GetterTypes.GetOrAdd(category, BuildGetterTypes);
        return map.TryGetValue(recordType, out var types) ? types : Array.Empty<Type>();
    }

    private static IReadOnlyDictionary<RecordType, Type[]> BuildGetterTypes(GameCategory category)
    {
        var entries = new List<(RecordType RecordType, Type Getter, int Width)>();
        foreach (var types in MajorRecordTypeEnumerator.GetMajorRecordTypesFor(category))
        {
            // Only the getter's own attribute: the interfaces it implements (link interfaces) list many record types
            foreach (var attr in types.GetterType.GetCustomAttributes<AssociatedRecordTypesAttribute>(inherit: false))
            {
                foreach (var recordType in attr.Types)
                {
                    entries.Add((recordType, types.GetterType, attr.Types.Length));
                }
            }
        }
        return entries
            .GroupBy(e => e.RecordType)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(e => e.Width).Select(e => e.Getter).Distinct().ToArray());
    }

    /// <summary>
    /// Finds an overlay's record by FormKey through its record type
    /// </summary>
    /// <param name="mod">The plugin</param>
    /// <param name="category">Its game</param>
    /// <param name="formKey">The record</param>
    /// <param name="typedLookup">Looks a FormKey up in one type's cache</param>
    /// <param name="found">The record found, or null</param>
    /// <returns>True if the index answered (found is the record, or null if the plugin has no such record); false if
    /// the plugin has no index or the index couldn't place the record, so the caller should use its untyped cache</returns>
    public static bool TryResolve<TItem>(
        IModGetter mod,
        GameCategory category,
        FormKey formKey,
        TypedLookup<TItem> typedLookup,
        out TItem? found)
        where TItem : class
    {
        found = null;
        if (!TryGet(mod, out var index)) return false;
        if (!index.TryGetRecordType(formKey, out var recordType)) return true;
        foreach (var type in GetterTypesFor(category, recordType))
        {
            if (typedLookup(formKey, type, out found)) return true;
        }
        found = null;
        return false;
    }

    public delegate bool TypedLookup<TItem>(FormKey formKey, Type type, out TItem? found) where TItem : class;
}
