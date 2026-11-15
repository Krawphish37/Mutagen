using Mutagen.Bethesda.Plugins.Binary.Translations;

namespace Mutagen.Bethesda.Plugins.Records;

/// <summary>
/// How a plugin's file was laid out beyond its records' fields (Starfield keeps it when reading), so that a mod or
/// record rebuilt from another form (JSON, a database) can be written back as the same file. Each value is opaque:
/// store it as it is and give it back to the matching Set method. The cell's child-group layout is
/// CellMixIn.GetChildGroupLayout / SetChildGroupLayout in the game's own namespace.
/// </summary>
public static class FileLayoutMixIn
{
    /// <summary>
    /// The empty top-level groups this mod had in its file, with where each sat (null if none, e.g. a new mod).
    /// The Creation Kit writes an empty group for some record types; Mutagen writes only groups with records, so these
    /// are kept and written back in their places.
    /// </summary>
    public static string? GetEmptyTopLevelGroups(this IModGetter mod) => EmptyTopLevelGroups.Export(mod);

    /// <summary>
    /// Sets the empty top-level groups to write with this mod (a value from <see cref="GetEmptyTopLevelGroups"/>);
    /// null removes them. A group is written right after the non-empty group it followed in the file, and left out
    /// if that group isn't written any more.
    /// </summary>
    /// <exception cref="FormatException">The value is not one GetEmptyTopLevelGroups returns</exception>
    public static void SetEmptyTopLevelGroups(this IMod mod, string? groups) => EmptyTopLevelGroups.Import(mod, groups);

    /// <summary>
    /// A compressed record's content as it was read: its uncompressed length (4 bytes), then the zlib stream
    /// (null if the record wasn't read compressed, for a game that doesn't keep it, or for a full import read without
    /// <see cref="Mutagen.Bethesda.Plugins.Binary.Parameters.BinaryReadParameters.KeepCompressedRecords"/> turned off; an overlay always keeps it).
    /// </summary>
    public static byte[]? GetOriginalCompressedData(this IMajorRecordGetter record) =>
        CompressedOriginals.TryGet(record, out var content) ? content.ToArray() : null;

    /// <summary>
    /// <see cref="GetOriginalCompressedData"/>, but only when Mutagen wouldn't write those bytes itself: when compressing
    /// the content it holds, as Mutagen compresses (zlib's best level), gives another stream (C213). A record the
    /// Creation Kit compressed usually comes back as read without it (Starfield.esm: all but 13 navmeshes); one made with
    /// another encoder or level needs it (N12_Watchtower.esm's NPC_, zlib's default level, header 78 9C). Give it back
    /// with <see cref="SetOriginalCompressedData"/>. For a record kept in another form (JSON, a database), this keeps
    /// only the compressed bytes that can't be made again.
    /// </summary>
    public static byte[]? GetOriginalCompressedDataIfNotReproduced(this IMajorRecordGetter record)
    {
        return CompressedOriginals.TryGetNotReproduced(record, out var content) ? content.ToArray() : null;
    }

    /// <summary>
    /// Sets the compressed content to write for this record (a value from <see cref="GetOriginalCompressedData"/>);
    /// null removes it. It is written only while the record is compressed and its content, written anew, is exactly
    /// what the value decompresses to; otherwise the record is compressed anew.
    /// </summary>
    /// <exception cref="FormatException">The value is not an uncompressed length followed by a zlib stream</exception>
    public static void SetOriginalCompressedData(this IMajorRecord record, byte[]? content)
    {
        if (content == null)
        {
            CompressedOriginals.Remove(record);
            return;
        }
        CompressedOriginals.Validate(content);
        CompressedOriginals.Remember(record, content.ToArray());
    }
}
