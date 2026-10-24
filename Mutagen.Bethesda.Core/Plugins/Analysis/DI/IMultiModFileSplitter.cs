using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Analysis.DI;

/// <summary>
/// Interface for splitting a mod into multiple files when master limits are exceeded.
/// </summary>
public interface IMultiModFileSplitter
{
    /// <summary>
    /// Splits a given input mod into multiple output mods, each containing at most the specified master limit.
    /// </summary>
    /// <typeparam name="TMod">Mutable mod type</typeparam>
    /// <typeparam name="TModGetter">Getter mod type</typeparam>
    /// <param name="inputMod">The mod to split</param>
    /// <param name="masterLimit">Maximum number of masters allowed per output mod</param>
    /// <returns>Collection of split mods</returns>
    IReadOnlyList<TMod> Split<TMod, TModGetter>(TMod inputMod, int masterLimit)
        where TMod : IMod, TModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TModGetter : IModGetter;

    /// <summary>
    /// Splits a given input mod into multiple output mods, each containing at most the specified master limit.
    /// On games with separated master load orders (Starfield), each output mod also keeps its Full masters
    /// within the Full indices its FormIDs can address.
    /// </summary>
    /// <typeparam name="TMod">Mutable mod type</typeparam>
    /// <typeparam name="TModGetter">Getter mod type</typeparam>
    /// <param name="inputMod">The mod to split</param>
    /// <param name="masterLimit">Maximum number of masters allowed per output mod</param>
    /// <param name="masterFlagsLookup">
    /// Master styles of the mods the input mod references.  Without it, only the master count is limited.
    /// </param>
    /// <returns>Collection of split mods</returns>
    IReadOnlyList<TMod> Split<TMod, TModGetter>(
        TMod inputMod,
        int masterLimit,
        IReadOnlyCache<IModMasterStyledGetter, ModKey>? masterFlagsLookup)
        where TMod : IMod, TModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TModGetter : IModGetter
    {
        return Split<TMod, TModGetter>(inputMod, masterLimit);
    }
}
