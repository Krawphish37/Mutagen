using System.Runtime.CompilerServices;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Order;

/// <summary>
/// A plugin a load order import left unloaded because it couldn't be read, or because a master of it couldn't
/// </summary>
/// <param name="ModKey">The plugin</param>
/// <param name="Exception">What reading it threw; null when it was dropped for a master</param>
/// <param name="Reason">Why, in a few words</param>
public sealed record PluginLoadError(ModKey ModKey, Exception? Exception, string Reason)
{
    public override string ToString() => $"{ModKey}: {Reason}";
}

/// <summary>
/// The plugins a load order import dropped (BinaryReadParameters.LoadOrderReadFailures = DropPlugin, the default; C204)
/// </summary>
public static class LoadOrderErrors
{
    private static readonly ConditionalWeakTable<object, IReadOnlyList<PluginLoadError>> Errors = new();

    /// <summary>
    /// The plugins this load order's import left unloaded, in load order, with why (empty when every plugin loaded, or for a
    /// load order not made by an import)
    /// </summary>
    public static IReadOnlyList<PluginLoadError> GetLoadErrors(this ILoadOrderGetter loadOrder)
    {
        return Errors.TryGetValue(loadOrder, out var list) ? list : Array.Empty<PluginLoadError>();
    }

    internal static void Set(object loadOrder, IReadOnlyList<PluginLoadError> errors)
    {
        if (errors.Count == 0) Errors.Remove(loadOrder);
        else Errors.AddOrUpdate(loadOrder, errors);
    }

    internal static string ReasonFor(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            switch (e)
            {
                case DuplicateFormKeyException dup:
                    return $"refused: {dup.Pairs.Count} pair{(dup.Pairs.Count == 1 ? "" : "s")} of its records share a FormKey ({string.Join("; ", dup.Pairs)})";
                case MissingModException missing:
                    return $"could not be read: {FirstLine(missing.Message)}";
            }
        }
        return $"could not be read: {FirstLine(ex.Message)}";
    }

    private static ModKey? DroppedMaster(object mod, HashSet<ModKey> dropped)
    {
        try
        {
            return (mod as IModGetter)?.MasterReferences
                .Select(m => m.Master)
                .FirstOrDefault(m => dropped.Contains(m));
        }
        catch (Exception)
        {
            // A mod that can't list its masters is kept
            return null;
        }
    }

    private static IEnumerable<ModKey> MissingMasters(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is MissingModException missing)
            {
                foreach (var path in missing.ModPaths) yield return path.ModKey;
            }
        }
    }

    private static string FirstLine(string text)
    {
        var i = text.IndexOfAny(new[] { '\r', '\n' });
        return i < 0 ? text : text.Substring(0, i);
    }

    /// <summary>
    /// Builds an import's listings: a plugin that failed is left unloaded with its error; then, in load order, a loaded
    /// plugin with a dropped master is disposed and left unloaded too ("master X was refused")
    /// </summary>
    internal static LoadOrder<IModListing<TMod>> Build<TMod>(
        IReadOnlyList<(ModKey ModKey, TMod? Mod, Exception? Error, bool Enabled)> results)
        where TMod : class, IModKeyed
    {
        var dropped = new HashSet<ModKey>();
        var errors = new List<PluginLoadError>();
        var listings = new List<IModListing<TMod>>();
        foreach (var r in results)
        {
            if (r.Error != null)
            {
                dropped.Add(r.ModKey);
                // A master that was dropped before it (and so is missing from the master sizes) says why
                var missingDropped = MissingMasters(r.Error).FirstOrDefault(m => dropped.Contains(m));
                errors.Add(new PluginLoadError(r.ModKey, r.Error,
                    !missingDropped.IsNull ? $"master {missingDropped} was refused" : ReasonFor(r.Error)));
                listings.Add(ModListing<TMod>.CreateUnloaded(r.ModKey, r.Enabled));
                continue;
            }
            if (r.Mod == null)
            {
                // Not on disk: unloaded, as before
                listings.Add(ModListing<TMod>.CreateUnloaded(r.ModKey, r.Enabled));
                continue;
            }
            var droppedMaster = dropped.Count == 0 ? null : DroppedMaster(r.Mod, dropped);
            if (droppedMaster is { } master && !master.IsNull)
            {
                (r.Mod as IDisposable)?.Dispose();
                dropped.Add(r.ModKey);
                errors.Add(new PluginLoadError(r.ModKey, null, $"master {master} was refused"));
                listings.Add(ModListing<TMod>.CreateUnloaded(r.ModKey, r.Enabled));
                continue;
            }
            listings.Add(new ModListing<TMod>(r.Mod, r.Enabled));
        }
        var ret = new LoadOrder<IModListing<TMod>>(listings);
        Set(ret, errors);
        return ret;
    }
}
