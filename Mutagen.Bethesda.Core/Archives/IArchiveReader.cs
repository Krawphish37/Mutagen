using System.Diagnostics.CodeAnalysis;

namespace Mutagen.Bethesda.Archives;

/// <summary>
/// A readonly interface for retrieving data from a Bethesda Archive
/// </summary>
public interface IArchiveReader
{
    /// <summary>
    /// Attempts to locate and retrieve a folder from the archive
    /// </summary>
    /// <param name="path">Folder path to look for</param>
    /// <param name="folder">Retrieved folder object, if successful</param>
    /// <returns>True if folder with the given path was located in the archive</returns>
    bool TryGetFolder(string path, [MaybeNullWhen(false)] out IArchiveFolder folder);
    IEnumerable<IArchiveFile> Files { get; }

    /// <summary>
    /// The path of every file in the archive, in the same order and spelling as <see cref="Files"/>. A BA2 reads them
    /// from its name table alone, without reading the file records or building the files (C201).
    /// </summary>
    IEnumerable<string> EnumerateFilePaths() => Files.Select(f => f.Path);

    /// <summary>
    /// Attempts to locate and retrieve one file by its path, building only that file where the archive allows it (a
    /// BA2: its name table and that one file record; a BSA: the folder's files) (C201)
    /// </summary>
    /// <param name="path">File path to look for: case doesn't matter (ASCII letters), nor / or \</param>
    /// <param name="file">The file, if found</param>
    /// <returns>True if the archive holds a file with this path</returns>
    bool TryGetFile(string path, [MaybeNullWhen(false)] out IArchiveFile file)
    {
        foreach (var f in Files)
        {
            if (ArchivePaths.AreSame(f.Path, path))
            {
                file = f;
                return true;
            }
        }
        file = default;
        return false;
    }

    /// <summary>
    /// Whether the archive holds a file with this path, read as cheaply as the archive allows (a BA2: its name table
    /// only) (C201)
    /// </summary>
    /// <param name="path">File path to look for: case doesn't matter (ASCII letters), nor / or \</param>
    bool ContainsFile(string path) => TryGetFile(path, out _);
}

/// <summary>
/// How archive file paths compare: ignoring ASCII case, and / and \ alike
/// </summary>
internal static class ArchivePaths
{
    public static bool AreSame(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            char x = a[i], y = b[i];
            if (x == y) continue;
            if (x is '/' or '\\' && y is '/' or '\\') continue;
            if (x < 128 && y < 128 && char.ToLowerInvariant(x) == char.ToLowerInvariant(y)) continue;
            return false;
        }
        return true;
    }

    /// <summary>
    /// The same comparison on UTF-8 bytes (a BA2 name table), for a query already encoded
    /// </summary>
    public static bool AreSame(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            byte x = a[i], y = b[i];
            if (x == y) continue;
            if (x is (byte)'/' or (byte)'\\' && y is (byte)'/' or (byte)'\\') continue;
            if ((x | 0x20) == (y | 0x20) && (x | 0x20) is >= (byte)'a' and <= (byte)'z') continue;
            return false;
        }
        return true;
    }
}
