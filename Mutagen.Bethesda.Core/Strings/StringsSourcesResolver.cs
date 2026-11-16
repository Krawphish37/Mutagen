using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Strings.DI;
using Noggog;

namespace Mutagen.Bethesda.Strings;

/// <summary>
/// Where plugins' strings tables come from, for a host that keeps one resolver for a data folder (C77). The answers are
/// those of <see cref="StringsFolderLookupOverlay.ResolveSources(GameRelease, ModKey, DirectoryPath, StringsReadParameters?, IFileSystem?)"/>;
/// what a resolver keeps between calls is each archive's strings listing (from its name table), so a later call reads no
/// name table again.<br/>
/// Every call looks again at what can change: the Strings folder's file list, the data folder's archives (an archive
/// added or removed), and each archive's size and last write time (a changed archive's listing is read again). The
/// game's ini and the load order, which decide which archives apply to a plugin and their order, are read once, when
/// the resolver first needs them: make a new resolver when they change.<br/>
/// Safe to use from several threads.
/// </summary>
public sealed class StringsSourcesResolver
{
    private readonly IStringsFolderLookupFactory _factory;

    /// <summary>The data folder the resolver looks in</summary>
    public DirectoryPath DataPath { get; }

    /// <summary>
    /// A resolver for one data folder
    /// </summary>
    /// <param name="release">The game release</param>
    /// <param name="dataPath">The data folder (its Strings folder and archives)</param>
    /// <param name="instructions">Strings folder or archive folder overrides, as for
    /// <see cref="StringsFolderLookupOverlay.TypicalFactory"/></param>
    /// <param name="fileSystem">The file system</param>
    public StringsSourcesResolver(
        GameRelease release,
        DirectoryPath dataPath,
        StringsReadParameters? instructions = null,
        IFileSystem? fileSystem = null)
    {
        DataPath = dataPath;
        _factory = new StringsFolderLookupOverlayFactoryContainer(release, fileSystem, dataPath, instructions).Resolve().Value;
    }

    /// <summary>
    /// Where each of a plugin's strings tables is read from, as
    /// <see cref="StringsFolderLookupOverlay.ResolveSources(GameRelease, ModKey, DirectoryPath, StringsReadParameters?, IFileSystem?)"/>
    /// gives it
    /// </summary>
    public IReadOnlyList<StringsTableSource> ResolveSources(ModKey modKey) => _factory.ResolveSources(modKey);

    /// <summary>
    /// The same for many plugins at once (the folders listed once, each archive looked at once), as
    /// <see cref="StringsFolderLookupOverlay.ResolveSources(GameRelease, IEnumerable{ModKey}, DirectoryPath, StringsReadParameters?, IFileSystem?)"/>
    /// gives it
    /// </summary>
    /// <param name="modKeys">The plugins; one listed twice is resolved once</param>
    public IReadOnlyDictionary<ModKey, IReadOnlyList<StringsTableSource>> ResolveSources(IEnumerable<ModKey> modKeys) =>
        _factory.ResolveSources(modKeys);

    /// <summary>
    /// A plugin's strings lookup, as <see cref="StringsFolderLookupOverlay.TypicalFactory"/> makes it, resolved through
    /// this resolver's kept listings
    /// </summary>
    public IStringsFolderLookup CreateLookup(ModKey modKey) => _factory.InternalFactory(modKey);
}
