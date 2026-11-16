using System.IO.Abstractions;
using Mutagen.Bethesda.Environments.DI;
using Mutagen.Bethesda.Plugins;
using Noggog;

namespace Mutagen.Bethesda.Archives.DI;

public interface IGetApplicableArchivePaths
{
    /// <summary>
    /// Enumerates all Archives
    /// </summary>
    IEnumerable<FilePath> Get();

    /// <summary>
    /// Enumerates all applicable Archives for a given ModKey<br/>
    /// This call is intended to return Archives related to one specific mod.<br/>
    /// NOTE:  It is currently a bit experimental
    /// </summary>
    /// <param name="modKey">ModKey to query about</param>
    /// <returns></returns>
    IEnumerable<FilePath> Get(ModKey modKey);

    /// <summary>
    /// <see cref="Get(ModKey)"/> for many plugins (C74): what applies to each, in the same order. An implementation may
    /// list the data folder once for all of them.
    /// </summary>
    /// <param name="modKeys">The plugins; one listed twice is answered once</param>
    IReadOnlyDictionary<ModKey, IReadOnlyList<FilePath>> Get(IEnumerable<ModKey> modKeys)
    {
        var ret = new Dictionary<ModKey, IReadOnlyList<FilePath>>();
        foreach (var modKey in modKeys)
        {
            if (!ret.ContainsKey(modKey)) ret[modKey] = Get(modKey).ToArray();
        }
        return ret;
    }
}

public sealed class GetApplicableArchivePaths : IGetApplicableArchivePaths
{
    private readonly IFileSystem _fileSystem;
    private readonly ICheckArchiveApplicability _applicability;
    private readonly IDataDirectoryProvider _dataDirectoryProvider;
    private readonly IArchiveExtensionProvider _archiveExtension;
    private readonly IArchiveListingDetailsProvider _archiveListingDetailsProvider;

    public GetApplicableArchivePaths(
        IFileSystem fileSystem,
        ICheckArchiveApplicability applicability,
        IDataDirectoryProvider dataDirectoryProvider,
        IArchiveExtensionProvider archiveExtension,
        IArchiveListingDetailsProvider archiveListingDetailsProvider)
    {
        _fileSystem = fileSystem;
        _applicability = applicability;
        _dataDirectoryProvider = dataDirectoryProvider;
        _archiveExtension = archiveExtension;
        _archiveListingDetailsProvider = archiveListingDetailsProvider;
    }
        
    /// <inheritdoc />
    public IEnumerable<FilePath> Get()
    {
        return GetInternal(default(ModKey?));
    }

    /// <inheritdoc />
    public IEnumerable<FilePath> Get(ModKey modKey)
    {
        return GetInternal(modKey);
    }
        
    /// <summary>
    /// What applies to each plugin, the data folder listed once for all of them (C74): the same rule and order as
    /// <see cref="Get(ModKey)"/>
    /// </summary>
    public IReadOnlyDictionary<ModKey, IReadOnlyList<FilePath>> Get(IEnumerable<ModKey> modKeys)
    {
        var ret = new Dictionary<ModKey, IReadOnlyList<FilePath>>();
        var exists = _fileSystem.Directory.Exists(_dataDirectoryProvider.Path);
        FilePath[]? ini = null;
        Dictionary<string, List<FilePath>>? byName = null;
        foreach (var modKey in modKeys)
        {
            if (ret.ContainsKey(modKey)) continue;
            if (modKey.IsNull || !exists)
            {
                ret[modKey] = [];
                continue;
            }
            if (ini == null || byName == null)
            {
                var archives = ListArchives().ToArray();
                // The ini's archives apply to every plugin and come first, in the ini's order, whatever the plugin (the
                // order puts every ini archive before the others and compares two of them by the ini alone)
                ini = archives.Where(x => _archiveListingDetailsProvider.IsIni(x.Name))
                    .OrderBy(x => x.Name, _archiveListingDetailsProvider.GetComparerFor(modKey))
                    .ToArray();
                // The others by the plugin names they can apply to: the name, and the name before its last " - "
                byName = new Dictionary<string, List<FilePath>>(StringComparer.OrdinalIgnoreCase);
                foreach (var archive in archives)
                {
                    if (_archiveListingDetailsProvider.IsIni(archive.Name)) continue;
                    var bare = archive.Name.NameWithoutExtension;
                    var delim = bare.LastIndexOf(" - ", StringComparison.Ordinal);
                    foreach (var name in delim < 0 ? new[] { bare } : new[] { bare, bare[..delim] })
                    {
                        if (!byName.TryGetValue(name, out var list)) byName[name] = list = new List<FilePath>();
                        if (!list.Contains(archive)) list.Add(archive);
                    }
                }
            }
            var own = byName.TryGetValue(modKey.Name, out var named)
                ? named.Where(x => _applicability.IsApplicable(modKey, x.Name))
                    .OrderBy(x => x.Name, _archiveListingDetailsProvider.GetComparerFor(modKey))
                : Enumerable.Empty<FilePath>();
            ret[modKey] = ini.Concat(own).ToArray();
        }
        return ret;
    }

    private IEnumerable<FilePath> ListArchives() =>
        _fileSystem.Directory.EnumerateFilesIgnoringCase(_dataDirectoryProvider.Path, $"*{_archiveExtension.Get()}");

    private IEnumerable<FilePath> GetInternal(ModKey? modKey)
    {
        if (modKey.HasValue && modKey.Value.IsNull)
        {
            return [];
        }

        if (!_fileSystem.Directory.Exists(_dataDirectoryProvider.Path))
        {
            return [];
        }

        return Select(ListArchives(), modKey);
    }

    // The archives of the data folder that apply to a plugin (those named after it, and those the ini lists), or with no
    // plugin those the load order and the ini list, in Mutagen's archive order
    private IEnumerable<FilePath> Select(IEnumerable<FilePath> ret, ModKey? modKey)
    {
        if (modKey != null && !modKey.Value.IsNull)
        {
            ret = ret
                .Where(x => _archiveListingDetailsProvider.IsIni(x.Name) || _applicability.IsApplicable(modKey.Value, x.Name));
        }
        else
        {
            ret = ret
                .Where(x => _archiveListingDetailsProvider.Contains(x.Name));
        }
        return ret.OrderBy(x => x.Name, _archiveListingDetailsProvider.GetComparerFor(modKey));
    }
}