using System.Runtime.CompilerServices;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Internals;

namespace Mutagen.Bethesda.Plugins.Records;

/// <summary>
/// What a Starfield plugin read from a file looked like, so the parts of its header that Mutagen otherwise recomputes
/// are written back as the file had them while what they describe hasn't changed.<br/>
/// <b>Overridden forms (TES4 ONAM).</b> The Creation Kit lists every record of another plugin that sits under a parent
/// record (placed references and navmeshes in a cell, topics in a quest, responses in a topic), is not persistent, and
/// is not a worldspace's cell. Its order is its own (neither sorted nor file order), and it leaves out overrides of
/// FormIDs the master doesn't have. Mutagen can compute the set (OverriddenFormsOption.Iterate's rule) but not those
/// two choices, so for a mod read from a file it remembers the records that met the rule at read time (the baseline).
/// On write, while the records meeting the rule are still the same, the list is written as the mod holds it (the
/// file's list); when they changed, the list is updated by the difference only: entries for records that no longer
/// meet the rule go, new ones are added at the end, sorted. A mod with no baseline (a new mod) gets the computed list.
/// <br/>
/// <b>Record count and next object ID (TES4 HEDR).</b> The same walk notes how many records the mod had and the
/// highest ID of its own records (see <see cref="HeaderCounts"/>).
/// </summary>
internal static class OverriddenFormsBaseline
{
    /// <summary>
    /// The read state: the records that met the overridden-forms rule, and (when known) how many records the mod had
    /// and the highest ID among its own records
    /// </summary>
    internal sealed record Snapshot(IReadOnlySet<FormKey> Candidates, uint? Records, uint? MaxOwnId);

    private sealed class Holder
    {
        private readonly Lazy<Snapshot> _snapshot;

        /// <summary>
        /// The mod can't change (an overlay): its header is written as read, without computing anything
        /// </summary>
        public bool Unchangeable { get; }

        public Holder(Lazy<Snapshot> snapshot, bool unchangeable)
        {
            _snapshot = snapshot;
            Unchangeable = unchangeable;
        }

        public Snapshot Snapshot => _snapshot.Value;
    }

    private static readonly ConditionalWeakTable<object, Holder> Mods = new();

    /// <summary>
    /// Whether Mutagen keeps the header this way for the mod's game (Starfield)
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
    /// The mod as it is now: the records meeting the rule, the number of records, the highest own ID. One walk over
    /// its record contexts.
    /// </summary>
    public static Snapshot Compute(IModGetter mod)
    {
        var candidates = new HashSet<FormKey>();
        uint records = 0;
        uint? maxOwn = null;
        foreach (var context in mod.EnumerateMajorRecordSimpleContexts())
        {
            records++;
            var fk = context.Record.FormKey;
            if (fk.ModKey == mod.ModKey)
            {
                if (maxOwn == null || fk.ID > maxOwn) maxOwn = fk.ID;
            }
            else if (IsCandidate(context, mod.ModKey))
            {
                candidates.Add(fk);
            }
        }
        return new Snapshot(candidates, records, maxOwn);
    }

    /// <summary>
    /// Notes a mod read in full (mutable): its state right after the read
    /// </summary>
    public static void RememberRead(IModGetter mod)
    {
        if (!Applies(mod)) return;
        var snapshot = Compute(mod);
        Mods.AddOrUpdate(mod, new Holder(new Lazy<Snapshot>(() => snapshot), unchangeable: false));
    }

    /// <summary>
    /// Notes an overlay: it can't change, so its state is computed from it only if asked for (a copy, the public
    /// getter)
    /// </summary>
    public static void RememberOverlay(IModGetter mod)
    {
        if (!Applies(mod)) return;
        Mods.AddOrUpdate(mod, new Holder(
            new Lazy<Snapshot>(() => Compute(mod), LazyThreadSafetyMode.ExecutionAndPublication),
            unchangeable: true));
    }

    /// <summary>
    /// Sets the overridden-forms baseline (the public API). The record count and highest own ID are then not known.
    /// </summary>
    public static void Set(object mod, IEnumerable<FormKey>? formKeys)
    {
        if (formKeys == null)
        {
            Mods.Remove(mod);
            return;
        }
        var snapshot = new Snapshot(new HashSet<FormKey>(formKeys), null, null);
        Mods.AddOrUpdate(mod, new Holder(new Lazy<Snapshot>(() => snapshot), unchangeable: false));
    }

    public static IReadOnlySet<FormKey>? Get(object mod) => GetSnapshot(mod)?.Candidates;

    public static Snapshot? GetSnapshot(object mod) => Mods.TryGetValue(mod, out var holder) ? holder.Snapshot : null;

    public static bool IsUnchangeable(object mod) => Mods.TryGetValue(mod, out var holder) && holder.Unchangeable;

    /// <summary>
    /// Whether the mod was read from a file (or given its read state): its master list is then the file's
    /// </summary>
    public static bool WasRead(object mod) => Mods.TryGetValue(mod, out _);

    /// <summary>
    /// A copy of a mod (DeepCopy) takes over its read state; the copy can change, so an overlay's is computed now
    /// </summary>
    public static void Copy(object from, object to)
    {
        if (ReferenceEquals(from, to)) return;
        if (GetSnapshot(from) is not { } snapshot)
        {
            Mods.Remove(to);
            return;
        }
        Mods.AddOrUpdate(to, new Holder(new Lazy<Snapshot>(() => snapshot), unchangeable: false));
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

    /// <summary>
    /// The Starfield header's record count and next object ID (HEDR).<br/>
    /// <b>Record count:</b> the Creation Kit stores the number of records plus one (the TES4 header itself), with no
    /// groups counted, deleted and compressed records included (Starfield.esm 3,829,247 = 3,829,246 + 1; all 29
    /// CK-made official plugins, kgcdoom.esm). xEdit stores records plus groups (darkstar.esm 47,867 = 47,021 + 846;
    /// 278 of 370 plugins in one Data folder). A mod read from a file keeps its count when it has the same number of
    /// records, and moves it by the difference otherwise (so either convention is kept); a new mod gets the CK's
    /// count.<br/>
    /// <b>Next object ID:</b> the CK stores FF001000 in every Starfield plugin, whatever its highest own ID (a value it
    /// doesn't use; Starfield.esm's own IDs go up to 3FFD59); xEdit stores the highest own ID + 1 (weap_starlight.esm
    /// 00000888) or a stale value (darkstar.esm 02000800). A mod read from a file keeps the value while it has no own
    /// record above the highest it was read with, and also when the value is the CK's (FF high byte) or still above
    /// every own ID; otherwise, and for a new mod, it gets the highest own ID + 1, as before.
    /// </summary>
    internal static class HeaderCounts
    {
        public static uint RecordCount(Snapshot? read, uint stored, uint recordsNow)
        {
            if (read?.Records is not { } recordsRead) return recordsNow + 1;
            return unchecked((uint)(stored + ((long)recordsNow - recordsRead)));
        }

        /// <returns>The value to write, or null to compute it as before (highest own ID + 1, or the default)</returns>
        public static uint? NextObjectId(Snapshot? read, uint stored, uint? maxOwnNow)
        {
            if (read?.Records == null) return null;
            if (maxOwnNow == null) return stored;
            if (read.MaxOwnId is { } maxRead && maxOwnNow <= maxRead) return stored;
            if ((stored >> 24) == 0xFF) return stored;
            if ((stored & 0xFFFFFF) > maxOwnNow) return stored;
            return null;
        }
    }
}
