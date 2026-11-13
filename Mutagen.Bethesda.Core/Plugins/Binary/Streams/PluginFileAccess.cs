using System.IO.Abstractions;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Binary.Streams;

/// <summary>
/// Opens plugin files for reading
/// </summary>
public static class PluginFileAccess
{
    /// <summary>
    /// The sharing Mutagen opens a plugin with by default: others may read it, and delete, rename or replace it (a mod
    /// manager installing a new version) while Mutagen holds it. A reader that holds the old file (an overlay) keeps
    /// reading the old file's bytes. Writing into the file in place is not allowed: an open overlay would read bytes
    /// that changed under it.
    /// </summary>
    public const FileShare DefaultShare = FileShare.Read | FileShare.Delete;

    /// <summary>
    /// Opens a plugin file for reading with the given sharing
    /// </summary>
    public static Stream OpenRead(IFileSystem? fileSystem, string path, FileShare share = DefaultShare)
    {
        return fileSystem.GetOrDefault().FileStream.New(path, FileMode.Open, FileAccess.Read, share);
    }
}
