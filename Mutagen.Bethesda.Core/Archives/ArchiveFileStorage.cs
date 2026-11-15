namespace Mutagen.Bethesda.Archives;

/// <summary>
/// How an archived file's stored bytes are compressed (C42)
/// </summary>
public enum ArchiveCompression
{
    /// <summary>
    /// Stored as is
    /// </summary>
    None,

    /// <summary>
    /// A zlib stream (RFC 1950, deflate with a 2-byte header and an Adler-32 trailer): BA2 versions 1 and 2, version 3
    /// naming compression 0, and the BSAs of Oblivion, Fallout 3, New Vegas and Skyrim LE (versions 103 and 104)
    /// </summary>
    Zlib,

    /// <summary>
    /// One raw LZ4 block, no frame: BA2 version 3 naming compression 3 (Starfield's own archives)
    /// </summary>
    Lz4Block,

    /// <summary>
    /// An LZ4 frame: Skyrim SE BSAs (version 105)
    /// </summary>
    Lz4Frame,
}

/// <summary>
/// One stretch of an archived file's data, as the archive stores it (C42)
/// </summary>
/// <param name="Offset">Where the stretch begins in the archive file</param>
/// <param name="StoredSize">Its length in the archive file</param>
/// <param name="Size">Its length once inflated (equal to <paramref name="StoredSize"/> when stored as is)</param>
/// <param name="Compression">How it is compressed</param>
public readonly record struct ArchiveFileChunk(long Offset, uint StoredSize, uint Size, ArchiveCompression Compression)
{
    /// <summary>
    /// Whether the stretch is compressed: read <see cref="StoredSize"/> bytes at <see cref="Offset"/> and inflate them
    /// as <see cref="Compression"/> says to <see cref="Size"/> bytes
    /// </summary>
    public bool Compressed => Compression != ArchiveCompression.None;
}

/// <summary>
/// Where an archived file's bytes are in its archive, how many there are on disk and how they are compressed (C42).
/// The file's content (<see cref="IArchiveFile.GetBytes"/>) is <see cref="SynthesizedHeaderLength"/> bytes the reader
/// writes itself, followed by every chunk of <see cref="Chunks"/> in order, each read raw and inflated.
/// </summary>
public sealed class ArchiveFileStorage
{
    /// <summary>
    /// Where the file's stored bytes begin in the archive file. A BSA file's stretch begins with what the archive keeps
    /// ahead of the data (its path when the archive embeds names, and when compressed its 4-byte inflated size); a
    /// texture's is its first chunk's.
    /// </summary>
    public long Offset { get; }

    /// <summary>
    /// How many bytes of the archive file belong to it: a BSA file's whole stretch from <see cref="Offset"/>, a BA2
    /// general file's data, a BA2 texture's chunks together (read <see cref="Chunks"/> for where each one is)
    /// </summary>
    public long StoredSize { get; }

    /// <summary>
    /// The archive's compression when any of the file's data is compressed, otherwise
    /// <see cref="ArchiveCompression.None"/>. A texture can have some chunks compressed and others stored: see
    /// <see cref="Chunks"/>.
    /// </summary>
    public ArchiveCompression Compression { get; }

    /// <summary>
    /// Whether any of the file's data is compressed
    /// </summary>
    public bool Compressed => Compression != ArchiveCompression.None;

    /// <summary>
    /// How many bytes at the start of the file's content the reader writes itself and the archive doesn't store: a BA2
    /// texture's DDS magic and header (built from its texture record). 0 for every other file.
    /// </summary>
    public uint SynthesizedHeaderLength { get; }

    /// <summary>
    /// The file's data, stretch by stretch, in the order its content is made of: one for a general file or a BSA file
    /// (a BSA's begins after what <see cref="Offset"/>'s stretch keeps ahead of the data), one per chunk (mip range) for a
    /// BA2 texture
    /// </summary>
    public IReadOnlyList<ArchiveFileChunk> Chunks { get; }

    /// <summary>
    /// The file's inflated size: <see cref="SynthesizedHeaderLength"/> plus its chunks' inflated sizes, equal to
    /// <see cref="IArchiveFile.Size"/>
    /// </summary>
    public long Size => SynthesizedHeaderLength + Chunks.Sum(c => (long)c.Size);

    /// <summary>
    /// Describes a file's storage (the readers build these; see <see cref="IArchiveFile.GetStorage"/>)
    /// </summary>
    public ArchiveFileStorage(
        long offset,
        long storedSize,
        ArchiveCompression compression,
        IReadOnlyList<ArchiveFileChunk> chunks,
        uint synthesizedHeaderLength = 0)
    {
        Offset = offset;
        StoredSize = storedSize;
        Compression = compression;
        Chunks = chunks;
        SynthesizedHeaderLength = synthesizedHeaderLength;
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Offset}+{StoredSize} {Compression}, {Chunks.Count} chunk(s){(SynthesizedHeaderLength > 0 ? $", {SynthesizedHeaderLength} header bytes" : "")}";
}
