using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Analysis.DI;

public class MultiModFileSplitter : IMultiModFileSplitter
{
    /// <summary>
    /// Record representing a record to export and what it references
    /// </summary>
    private class AnalyzedRecord<TMod, TModGetter>
        where TMod : IMod, TModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TModGetter : IModGetter
    {
        public required IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter> Context { get; init; }

        /// <summary>Whether this record is originally defined within the mod</summary>
        public required bool IsOriginatingRecord { get; init; }

        /// <summary>External mods this record references, parent records included.</summary>
        public required HashSet<ModKey> ExternalMasters { get; init; }

        /// <summary>The originating records this record references, parent records included.</summary>
        public required HashSet<FormKey> ReferencedOriginRecords { get; init; }
    }

    /// <summary>
    /// Record representing a mod fragment to export and what it references
    /// </summary>
    private class ModFragment<TMod, TModGetter>
        where TMod : IMod, TModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TModGetter : IModGetter
    {
        public required int Index { get; init; }
        public required ModKey Key { get; init; }
        public HashSet<ModKey> Masters { get; } = new();
        /// <summary>How many of the masters use Full indices, when that is limited</summary>
        public int FullMasterCount { get; set; }
        public List<AnalyzedRecord<TMod, TModGetter>> Records { get; } = new();
    }

    /// <summary>
    /// Limit on how many Full masters a mod fragment can have, for games with separated master load orders
    /// </summary>
    private class FullMasterLimit
    {
        /// <summary>
        /// Full indices end below the 0xFD Medium marker, and the mod fragment itself takes the index after its
        /// Full masters, so 252 Full masters at most
        /// </summary>
        public const int Limit = (int)FormID.MediumMasterMarker - 1;

        public required IReadOnlyCache<IModMasterStyledGetter, ModKey> Lookup { get; init; }
        public required MasterStyle InputStyle { get; init; }
        public required HashSet<ModKey> FragmentKeys { get; init; }

        public bool IsFull(ModKey modKey)
        {
            if (Lookup.TryGetValue(modKey, out var styled))
            {
                return styled.MasterStyle == MasterStyle.Full;
            }

            if (FragmentKeys.Contains(modKey))
            {
                return InputStyle == MasterStyle.Full;
            }

            // Unknown to the lookup, so the write will refuse it anyway.  Count it as Full to be safe
            return true;
        }
    }

    /// <summary>
    /// Collects a records information like its external masters and referenced records, walking the parent chain
    /// </summary>
    private static AnalyzedRecord<TMod, TModGetter> AnalyzeContext<TMod, TModGetter>(
        IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter> context,
        ModKey inputKey)
        where TMod : IMod, TModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TModGetter : IModGetter
    {
        var externalMods = new HashSet<ModKey>();
        var referencedOriginatedRecords = new HashSet<FormKey>();

        void Accumulate(IMajorRecordGetter record)
        {
            if (record.FormKey.ModKey != inputKey)
            {
                externalMods.Add(record.FormKey.ModKey);
            }

            foreach (var formLink in record.EnumerateFormLinks(iterateNestedRecords: false))
            {
                if (formLink.FormKey.IsNull) continue;
                
                if (formLink.FormKey.ModKey == inputKey)
                {
                    referencedOriginatedRecords.Add(formLink.FormKey);
                }
                else
                {
                    externalMods.Add(formLink.FormKey.ModKey);
                }
            }
        }

        Accumulate(context.Record);

        // Walk the full parent chain
        var parent = context.Parent;
        while (parent != null)
        {
            if (parent.Record is IMajorRecordGetter parentRecord)
            {
                Accumulate(parentRecord);

                // A parent created by the mod has to be in this record's mod fragment or an earlier one
                if (parentRecord.FormKey.ModKey == inputKey)
                {
                    referencedOriginatedRecords.Add(parentRecord.FormKey);
                }
            }
            parent = parent.Parent;
        }

        return new AnalyzedRecord<TMod, TModGetter>
        {
            Context = context,
            IsOriginatingRecord = context.Record.FormKey.ModKey == inputKey,
            ExternalMasters = externalMods,
            ReferencedOriginRecords = referencedOriginatedRecords,
        };
    }

    public IReadOnlyList<TMod> Split<TMod, TModGetter>(TMod inputMod, int masterLimit)
        where TMod : IMod, TModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TModGetter : IModGetter
    {
        return Split<TMod, TModGetter>(inputMod, masterLimit, masterFlagsLookup: null);
    }

    public IReadOnlyList<TMod> Split<TMod, TModGetter>(
        TMod inputMod,
        int masterLimit,
        IReadOnlyCache<IModMasterStyledGetter, ModKey>? masterFlagsLookup)
        where TMod : IMod, TModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TModGetter : IModGetter
    {
        var inputKey = inputMod.ModKey;

        FullMasterLimit? fullMasterLimit = null;
        if (masterFlagsLookup != null
            && GameConstants.Get(inputMod.GameRelease).SeparateMasterLoadOrders)
        {
            fullMasterLimit = new FullMasterLimit()
            {
                Lookup = masterFlagsLookup,
                InputStyle = inputMod.GetMasterStyle(),
                FragmentKeys = new HashSet<ModKey>(),
            };
        }
        var linkCache = inputMod.ToUntypedImmutableLinkCache();

        var records = inputMod
            .EnumerateMajorRecordContexts<IMajorRecord, IMajorRecordGetter>(linkCache)
            .Select(context => AnalyzeContext(context, inputKey))
            .ToList();

        var createdRecordsByKey = records
            .Where(x => x.IsOriginatingRecord)
            .ToDictionary(x => x.Context.Record.FormKey, x => x);

        // Order created records so that a record is placed only after every created record it references
        var clusterSets = OrderCreatedClusters(createdRecordsByKey);

        var modFragments = new List<ModFragment<TMod, TModGetter>>();
        var originIndexByRecord = new Dictionary<FormKey, int>();

        // Place created clusters with dependencies first
        foreach (var clusterSet in clusterSets)
        {
            var externalMasters = new HashSet<ModKey>();
            var referencedOutside = new HashSet<FormKey>();
            foreach (var key in clusterSet)
            {
                var record = createdRecordsByKey[key];
                externalMasters.UnionWith(record.ExternalMasters);
                foreach (var referenced in record.ReferencedOriginRecords)
                {
                    if (!clusterSet.Contains(referenced))
                    {
                        referencedOutside.Add(referenced);
                    }
                }
            }

            var modFragment = ReserveModFragment(modFragments, inputMod, externalMasters, referencedOutside, masterLimit, fullMasterLimit, originIndexByRecord);
            foreach (var key in clusterSet)
            {
                modFragment.Records.Add(createdRecordsByKey[key]);
                originIndexByRecord[key] = modFragment.Index;
            }
        }

        // Place overrides. Each must load no earlier than every created record it references.
        foreach (var record in records)
        {
            if (record.IsOriginatingRecord) continue;
            var modFragment = ReserveModFragment(modFragments, inputMod, record.ExternalMasters, record.ReferencedOriginRecords, masterLimit, fullMasterLimit, originIndexByRecord);
            modFragment.Records.Add(record);
        }

        // Records originating in the first mod fragment keep the input key, so they need no remap
        var remap = createdRecordsByKey.Keys
            .Select(x => (Key: x, Index: originIndexByRecord.GetValueOrDefault(x, 0)))
            .Where(x => x.Index != 0)
            .ToDictionary(
                x => x.Key,
                x => new FormKey(KeyForIndex(inputKey, x.Index), x.Key.ID));

        return WriteModFragments<TMod, TModGetter>(inputMod, modFragments, remap);
    }

    /// <summary>
    /// Tarjan strongly-connected-components over the created-record reference graph. Each component becomes a
    /// cluster of records that must share a mod fragment, returned in dependency-first order (a cluster is emitted
    /// only after everything it references). Edges: a created record to each created record it links to, plus
    /// bidirectional edges across created parent chains so a created parent and its created children always
    /// share a cluster.
    /// </summary>
    private static List<HashSet<FormKey>> OrderCreatedClusters<TMod, TModGetter>(
        Dictionary<FormKey, AnalyzedRecord<TMod, TModGetter>> createdRecordsByKey)
        where TMod : IMod, TModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TModGetter : IModGetter
    {
        var adjacency = new Dictionary<FormKey, HashSet<FormKey>>();
        foreach (var key in createdRecordsByKey.Keys)
        {
            adjacency.GetOrAdd(key);
        }

        foreach (var (key, record) in createdRecordsByKey)
        {
            foreach (var referenced in record.ReferencedOriginRecords)
            {
                if (referenced.Equals(key)) continue;
                if (createdRecordsByKey.ContainsKey(referenced))
                {
                    adjacency.GetOrAdd(key).Add(referenced);
                }
            }

            var parent = record.Context.Parent;
            while (parent != null)
            {
                if (parent.Record is IMajorRecordGetter parentRecord
                    && createdRecordsByKey.ContainsKey(parentRecord.FormKey)
                    && !parentRecord.FormKey.Equals(key))
                {
                    adjacency.GetOrAdd(key).Add(parentRecord.FormKey);
                    adjacency.GetOrAdd(parentRecord.FormKey).Add(key);
                }
                parent = parent.Parent;
            }
        }

        var nextVisitOrder = 0;
        var visitOrderByKey = new Dictionary<FormKey, int>();
        var lowLinkByKey = new Dictionary<FormKey, int>();
        var onStack = new HashSet<FormKey>();
        var stack = new Stack<FormKey>();
        var clusters = new List<HashSet<FormKey>>();

        void StrongConnect(FormKey node)
        {
            visitOrderByKey[node] = nextVisitOrder;
            lowLinkByKey[node] = nextVisitOrder;
            nextVisitOrder++;
            stack.Push(node);
            onStack.Add(node);

            foreach (var neighbor in adjacency.GetOrAdd(node))
            {
                if (!visitOrderByKey.TryGetValue(neighbor, out var neighborVisitOrder))
                {
                    StrongConnect(neighbor);
                    lowLinkByKey[node] = Math.Min(lowLinkByKey[node], lowLinkByKey[neighbor]);
                }
                else if (onStack.Contains(neighbor))
                {
                    lowLinkByKey[node] = Math.Min(lowLinkByKey[node], neighborVisitOrder);
                }
            }

            if (lowLinkByKey[node] == visitOrderByKey[node])
            {
                var cluster = new HashSet<FormKey>();
                FormKey popped;
                do
                {
                    popped = stack.Pop();
                    onStack.Remove(popped);
                    cluster.Add(popped);
                }
                while (!popped.Equals(node));
                clusters.Add(cluster);
            }
        }

        foreach (var key in createdRecordsByKey.Keys)
        {
            if (!visitOrderByKey.ContainsKey(key))
            {
                StrongConnect(key);
            }
        }

        return clusters;
    }

    /// <summary>
    /// Reserves the mod fragment for a set of external masters and referenced formkeys: the first mod fragment that can
    /// hold it without exceeding the master limit and without referencing a later mod fragment, else a new mod
    /// fragment. The unit's masters are registered on the chosen mod fragment. 
    /// </summary>
    private static ModFragment<TMod, TModGetter> ReserveModFragment<TMod, TModGetter>(
        List<ModFragment<TMod, TModGetter>> modFragments,
        TMod inputMod,
        HashSet<ModKey> externals,
        HashSet<FormKey> referencedCreated,
        int masterLimit,
        FullMasterLimit? fullMasterLimit,
        Dictionary<FormKey, int> originIndexByRecord)
        where TMod : IMod, TModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TModGetter : IModGetter
    {
        var requiredMin = 0;
        foreach (var created in referencedCreated)
        {
            requiredMin = Math.Max(requiredMin, originIndexByRecord.GetValueOrDefault(created, 0));
        }

        HashSet<ModKey> MastersForIndex(int index)
        {
            var set = new HashSet<ModKey>(externals);
            foreach (var created in referencedCreated)
            {
                var createdOriginIndex = originIndexByRecord.GetValueOrDefault(created, 0);
                if (createdOriginIndex < index)
                {
                    set.Add(KeyForIndex(inputMod.ModKey, createdOriginIndex));
                }
            }
            return set;
        }

        void AddMasters(ModFragment<TMod, TModGetter> modFragment, HashSet<ModKey> needed)
        {
            foreach (var master in needed)
            {
                if (modFragment.Masters.Add(master)
                    && fullMasterLimit != null
                    && fullMasterLimit.IsFull(master))
                {
                    modFragment.FullMasterCount++;
                }
            }
        }

        for (var i = requiredMin; i < modFragments.Count; i++)
        {
            var modFragment = modFragments[i];
            var needed = MastersForIndex(i);
            var missing = 0;
            var missingFull = 0;
            foreach (var master in needed)
            {
                if (modFragment.Masters.Contains(master)) continue;
                missing++;
                if (fullMasterLimit != null && fullMasterLimit.IsFull(master))
                {
                    missingFull++;
                }
            }
            if (modFragment.Masters.Count + missing <= masterLimit
                && (fullMasterLimit == null
                    || modFragment.FullMasterCount + missingFull <= FullMasterLimit.Limit))
            {
                AddMasters(modFragment, needed);
                return modFragment;
            }
        }

        var newIndex = modFragments.Count;
        var newModFragment = new ModFragment<TMod, TModGetter>
        {
            Index = newIndex,
            Key = KeyForIndex(inputMod.ModKey, newIndex),
        };
        modFragments.Add(newModFragment);
        fullMasterLimit?.FragmentKeys.Add(newModFragment.Key);

        var newNeeded = MastersForIndex(newIndex);
        if (newNeeded.Count > masterLimit)
        {
            throw new TooManyMastersException(
                inputMod.ModKey,
                newNeeded.ToArray(),
                $"{inputMod.ModKey} has a record that needs too many masters on its own. {newNeeded.Count} masters, but the limit is {masterLimit}.");
        }
        if (fullMasterLimit != null)
        {
            var newFull = newNeeded.Where(fullMasterLimit.IsFull).ToArray();
            if (newFull.Length > FullMasterLimit.Limit)
            {
                throw new TooManyMastersException(
                    inputMod.ModKey,
                    newFull,
                    $"{inputMod.ModKey} has a record that needs too many Full masters on its own. {newFull.Length} Full masters, but a mod can only have {FullMasterLimit.Limit} next to its own Full index.");
            }
        }
        AddMasters(newModFragment, newNeeded);
        return newModFragment;
    }

    private static ModKey KeyForIndex(ModKey inputKey, int index)
    {
        if (index == 0) return inputKey;
        var fileName = inputKey.FileName;
        return ModKey.FromFileName($"{fileName.NameWithoutExtension}_{index + 1}{fileName.Extension}");
    }

    private static IReadOnlyList<TMod> WriteModFragments<TMod, TModGetter>(
        TMod inputMod,
        List<ModFragment<TMod, TModGetter>> modFragments,
        Dictionary<FormKey, FormKey> remap)
        where TMod : IMod, TModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TModGetter : IModGetter
    {
        var release = inputMod.GameRelease;
        var result = new List<TMod>(modFragments.Count);
        foreach (var modFragment in modFragments)
        {
            // Header version has to go in via the activator, as it drives the initial NextFormID
            var newMod = ModFactory<TMod>.Activator(
                modFragment.Key,
                release,
                headerVersion: inputMod.ModHeader.HeaderVersion);
            newMod.ModHeader.RawFlags = inputMod.ModHeader.RawFlags;
            newMod.ModHeader.Author = inputMod.ModHeader.Author;
            newMod.ModHeader.Description = inputMod.ModHeader.Description;

            // Records go in under their input FormKeys first, parents before the records nested in them.
            // A nested record finds or adds its parent by the parent's input FormKey, so a created parent has to
            // be in place, under that same FormKey, before its nested records are added to it
            var created = new HashSet<FormKey>();
            foreach (var record in modFragment.Records.OrderBy(x => ParentDepth(x.Context)))
            {
                if (record.IsOriginatingRecord)
                {
                    // A duplicated parent can bring its nested records along
                    if (!created.Add(record.Context.Record.FormKey)) continue;
                    var duplicate = record.Context.DuplicateIntoAsNewRecord(newMod, record.Context.Record.FormKey);
                    foreach (var nested in duplicate.EnumerateMajorRecords())
                    {
                        created.Add(nested.FormKey);
                    }
                }
                else
                {
                    record.Context.GetOrAddAsOverride(newMod);
                }
            }

            if (remap.Count > 0)
            {
                // Records created in a later mod fragment then take that fragment's FormKeys, and links follow them
                RekeyRecords(newMod, remap);
                newMod.RemapLinks(remap);
            }

            result.Add(newMod);
        }

        return result;
    }

    private static int ParentDepth(IModContext context)
    {
        var depth = 0;
        for (var parent = context.Parent; parent != null; parent = parent.Parent)
        {
            depth++;
        }
        return depth;
    }

    /// <summary>
    /// Gives every record in the mod whose FormKey is remapped its new FormKey, nested records included
    /// </summary>
    private static void RekeyRecords(IMod mod, Dictionary<FormKey, FormKey> remap)
    {
        var toRekey = mod.EnumerateMajorRecords()
            .Where(x => remap.ContainsKey(x.FormKey))
            .ToList();
        foreach (var record in toRekey)
        {
            var oldKey = record.FormKey;
            var newKey = remap[oldKey];
            var type = record.GetType();

            // A top-level group is keyed by FormKey, so its records are taken out and put back in.
            // Nested records sit in plain lists and can be re-keyed where they are.
            // The removal goes by the group's own record type, which the record's class can derive from (OMODs)
            var group = mod.TryGetTopLevelGroup(type);
            if (group != null
                && group.RecordCache.TryGetValue(oldKey, out var grouped)
                && ReferenceEquals(grouped, record))
            {
                mod.Remove(oldKey, group.ContainedRecordType);
                ((IMajorRecordInternal)record).FormKey = newKey;
                group.AddUntyped(record);
            }
            else
            {
                ((IMajorRecordInternal)record).FormKey = newKey;
            }
        }
    }
}
