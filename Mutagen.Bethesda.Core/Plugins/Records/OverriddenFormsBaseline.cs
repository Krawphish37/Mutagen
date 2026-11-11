using System.Runtime.CompilerServices;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Internals;

namespace Mutagen.Bethesda.Plugins.Records;

/// <summary>
/// The overridden-forms list (TES4 ONAM) of a Starfield plugin, kept the way the Creation Kit wrote it.<br/>
/// The CK lists every record of another plugin that sits under a parent record (placed references and navmeshes in a
/// cell, topics in a quest, responses in a topic), is not persistent, and is not a worldspace's cell. Its order is its
/// own (neither sorted nor file order), and it leaves out overrides of FormIDs the master doesn't have. Mutagen can
/// compute the set (OverriddenFormsOption.Iterate's rule) but not those two choices, so for a mod read from a file it
/// remembers the records that met the rule at read time (the baseline). On write, while the records meeting the rule
/// are still the same, the list is written as the mod holds it (the file's list); when they changed, the list is
/// updated by the difference only: entries for records that no longer meet the rule go, new ones are added at the
/// end, sorted. A mod with no baseline (a new mod) gets the computed list.
/// </summary>
internal static class OverriddenFormsBaseline
{
    private sealed class Holder
    {
        private Lazy<IReadOnlySet<FormKey>> _set;

        /// <summary>
        /// The mod can't change (an overlay): its list is written as read, without computing anything
        /// </summary>
        public bool Unchangeable { get; }

        public Holder(Lazy<IReadOnlySet<FormKey>> set, bool unchangeable)
        {
            _set = set;
            Unchangeable = unchangeable;
        }

        public IReadOnlySet<FormKey> Set => _set.Value;
    }

    private static readonly ConditionalWeakTable<object, Holder> Mods = new();

    /// <summary>
    /// Whether Mutagen keeps the list this way for the mod's game (Starfield)
    /// </summary>
    public static bool Applies(IModGetter mod) =>
        mod.ListsOverriddenForms && mod.GameRelease.ToCategory() == GameCategory.Starfield;

    /// <summary>
    /// Whether a record met the rule: a record of another plugin, not persistent, under a parent record that isn't a
    /// worldspace. (The rule OverriddenFormsOption.Iterate has always used.)
    /// </summary>
    public static bool IsCandidate(IModContext<IMajorRecordGetter> context, ModKey modKey)
    {
        if (context.Record.FormKey.ModKey == modKey) return false;
        if (Noggog.Enums.HasFlag(context.Record.MajorRecordFlagsRaw, Constants.Persistent)) return false;
        if (context.Parent?.Record is not IMajorRecordGetter parentMaj) return false;
        if (parentMaj.Registration.Name == "Worldspace") return false;
        return true;
    }

    /// <summary>
    /// The records of a mod that meet the rule now
    /// </summary>
    public static HashSet<FormKey> Compute(IModGetter mod)
    {
        var ret = new HashSet<FormKey>();
        foreach (var context in mod.EnumerateMajorRecordSimpleContexts())
        {
            if (IsCandidate(context, mod.ModKey)) ret.Add(context.Record.FormKey);
        }
        return ret;
    }

    /// <summary>
    /// Notes a mod read in full (mutable): the records meeting the rule right after the read
    /// </summary>
    public static void RememberRead(IModGetter mod)
    {
        if (!Applies(mod)) return;
        IReadOnlySet<FormKey> set = Compute(mod);
        Mods.AddOrUpdate(mod, new Holder(new Lazy<IReadOnlySet<FormKey>>(() => set), unchangeable: false));
    }

    /// <summary>
    /// Notes an overlay: it can't change, so its baseline is computed from it only if asked for (a copy, the public
    /// getter)
    /// </summary>
    public static void RememberOverlay(IModGetter mod)
    {
        if (!Applies(mod)) return;
        Mods.AddOrUpdate(mod, new Holder(
            new Lazy<IReadOnlySet<FormKey>>(() => Compute(mod), LazyThreadSafetyMode.ExecutionAndPublication),
            unchangeable: true));
    }

    public static void Set(object mod, IEnumerable<FormKey>? formKeys)
    {
        if (formKeys == null)
        {
            Mods.Remove(mod);
            return;
        }
        IReadOnlySet<FormKey> set = new HashSet<FormKey>(formKeys);
        Mods.AddOrUpdate(mod, new Holder(new Lazy<IReadOnlySet<FormKey>>(() => set), unchangeable: false));
    }

    public static IReadOnlySet<FormKey>? Get(object mod) => Mods.TryGetValue(mod, out var holder) ? holder.Set : null;

    public static bool IsUnchangeable(object mod) => Mods.TryGetValue(mod, out var holder) && holder.Unchangeable;

    /// <summary>
    /// A copy of a mod (DeepCopy) takes over its baseline; the copy can change, so an overlay's is computed now
    /// </summary>
    public static void Copy(object from, object to)
    {
        if (ReferenceEquals(from, to)) return;
        Set(to, Get(from));
    }

    /// <summary>
    /// The list to write: <paramref name="current"/> (the mod's list) when the records meeting the rule are those of
    /// the baseline; otherwise the current list without the records that left, plus the ones that came, sorted.
    /// Null for an empty list.
    /// </summary>
    public static IReadOnlyList<FormKey>? Update(
        IReadOnlyList<FormKey>? current,
        IReadOnlySet<FormKey> baseline,
        IReadOnlySet<FormKey> now)
    {
        if (baseline.SetEquals(now)) return current;
        var ret = new List<FormKey>();
        var listed = new HashSet<FormKey>();
        foreach (var fk in current ?? Array.Empty<FormKey>())
        {
            // Left: met the rule when read, doesn't now
            if (baseline.Contains(fk) && !now.Contains(fk)) continue;
            if (listed.Add(fk)) ret.Add(fk);
        }
        // Came: meets the rule now, didn't when read
        ret.AddRange(Sorted(now.Where(fk => !baseline.Contains(fk) && !listed.Contains(fk))));
        return ret.Count == 0 ? null : ret;
    }

    /// <summary>
    /// OverriddenFormsOption.Iterate's order: by plugin file name, then ID
    /// </summary>
    public static IEnumerable<FormKey> Sorted(IEnumerable<FormKey> formKeys) =>
        formKeys.OrderBy(x => x.ModKey.FileName.String).ThenBy(x => x.ID);
}
