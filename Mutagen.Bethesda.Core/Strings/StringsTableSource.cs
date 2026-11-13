namespace Mutagen.Bethesda.Strings;

/// <summary>
/// Where one strings table of a plugin (a source in a language) is read from
/// </summary>
/// <param name="Source">Strings source (.strings, .dlstrings or .ilstrings)</param>
/// <param name="Language">Language</param>
/// <param name="SourcePath">
/// The loose file's path, or the archive's path joined with the entry inside it.
/// The same value <see cref="StringsFolderLookupOverlay"/>'s TryLookup gives as its source path.
/// </param>
/// <param name="ArchivePath">The archive's path, or null for a loose file</param>
/// <param name="ArchiveEntryPath">The entry's path inside the archive, or null for a loose file</param>
public sealed record StringsTableSource(
    StringsSource Source,
    Language Language,
    string SourcePath,
    string? ArchivePath,
    string? ArchiveEntryPath)
{
    /// <summary>
    /// True when the table is read from an archive, false for a loose file
    /// </summary>
    public bool FromArchive => ArchivePath != null;
}
