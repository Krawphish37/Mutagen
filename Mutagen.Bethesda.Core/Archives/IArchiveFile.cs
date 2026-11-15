using Noggog;

namespace Mutagen.Bethesda.Archives;

public interface IArchiveFile
{
    /// <summary>
    /// The path of the file inside the archive
    /// </summary>
    string Path { get; }

    /// <summary>
    /// The uncompressed file size
    /// </summary>
    uint Size { get; }

    /// <summary>
    /// Retrieves the data and returns it as a new array of bytes
    /// </summary>
    /// <returns>New array of bytes containing data</returns>
    byte[] GetBytes();

    /// <summary>
    /// Retrieves the data and returns it as a readonly span that may be part of a larger memory block
    /// </summary>
    /// <returns>span containing data that may be part of a larger memory block</returns>
    ReadOnlySpan<byte> GetSpan();

    /// <summary>
    /// Retrieves the data and returns it as a readonly span that may be part of a larger memory block
    /// </summary>
    /// <returns>span containing data that may be part of a larger memory block</returns>
    ReadOnlyMemorySlice<byte> GetMemorySlice();

    /// <summary>
    /// Retrieves the data as a Stream
    /// </summary>
    /// <returns>File data as a Stream</returns>
    Stream AsStream();

    /// <summary>
    /// Where the file's bytes are in its archive, how many there are on disk and whether (and how) they are compressed:
    /// its offset, stored size, compression and data chunks (a BA2 texture: one per chunk) (C42).<br/>
    /// A BA2 file answers from its file record, without reading the archive; a BSA file reads the few bytes the archive
    /// keeps ahead of its data (as <see cref="Size"/> does, and kept with it).<br/>
    /// Null from an implementation that can't tell (the default, for implementations outside Mutagen).
    /// </summary>
    ArchiveFileStorage? GetStorage() => null;
}