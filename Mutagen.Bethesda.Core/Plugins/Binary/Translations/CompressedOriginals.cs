using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Binary.Translations;

/// <summary>
/// Keeps a compressed record's content as it was read (the uncompressed length and the zlib stream), for games whose
/// files should come back as they were (<see cref="Mutagen.Bethesda.Plugins.Meta.GameConstants.KeepSubrecordOrder"/>;
/// Starfield) (C139). Mutagen compresses at zlib's best level, which gives back the Creation Kit's bytes for almost
/// every record, but not all: Starfield.esm has 13 large navmeshes the CK's zlib packed differently, and some plugins
/// were made with another encoder (darkstar.esm's 1,017 NPC_).<br/>
/// Writing such a record serializes it as usual. When the uncompressed bytes are exactly the ones the kept stream
/// holds, the kept stream is written; otherwise the new bytes are compressed. A changed record is never written
/// with its old content.<br/>
/// An overlay keeps a slice of the memory it reads from (no copy). A full import keeps a copy of each compressed
/// record's stream unless told not to (BinaryReadParameters.KeepCompressedRecords, on by default): 1.2 GB for
/// Starfield.esm.
/// A copy (DeepCopy, DeepCopyIn) takes over the kept stream of the record it copies.
/// </summary>
internal static class CompressedOriginals
{
    private sealed class Holder
    {
        public readonly ReadOnlyMemorySlice<byte> Content;
        // Whether Mutagen's compression gives Content again (Reproduces): 0 not asked yet, 1 yes, 2 no
        public int Reproduced;
        public Holder(ReadOnlyMemorySlice<byte> content) => Content = content;
    }

    private static readonly ConditionalWeakTable<object, Holder> Originals = new();

    /// <summary>
    /// Keeps a record's compressed content as read
    /// </summary>
    /// <param name="record">The record</param>
    /// <param name="content">The record's content after its header: the uncompressed length (4 bytes), then the zlib stream</param>
    internal static void Remember(object record, ReadOnlyMemorySlice<byte> content)
    {
        if (content.Length < 4) return;
        Originals.AddOrUpdate(record, new Holder(content));
    }

    internal static bool TryGet(object record, out ReadOnlyMemorySlice<byte> content)
    {
        if (Originals.TryGetValue(record, out var holder))
        {
            content = holder.Content;
            return true;
        }
        content = default;
        return false;
    }

    internal static void Remove(object record) => Originals.Remove(record);

    /// <summary>
    /// A record's kept compressed content, only when Mutagen's compression wouldn't give it again
    /// (FileLayoutMixIn.GetOriginalCompressedDataIfNotReproduced, C213). The answer is kept with the content: compressing
    /// at zlib's best level is the costly part, and a record is often asked more than once (its links, then its JSON).
    /// </summary>
    internal static bool TryGetNotReproduced(object record, out ReadOnlyMemorySlice<byte> content)
    {
        content = default;
        if (!Originals.TryGetValue(record, out var holder)) return false;
        if (holder.Reproduced == 0) holder.Reproduced = Reproduces(holder.Content) ? 1 : 2;
        if (holder.Reproduced == 1) return false;
        content = holder.Content;
        return true;
    }

    internal static void Copy(object from, object to)
    {
        if (ReferenceEquals(from, to)) return;
        if (Originals.TryGetValue(from, out var holder)) Originals.AddOrUpdate(to, holder);
        else Originals.Remove(to);
    }

    /// <summary>
    /// Whether a kept compressed content decompresses to exactly the given bytes
    /// </summary>
    internal static bool Holds(ReadOnlyMemorySlice<byte> original, ReadOnlySpan<byte> uncompressed)
    {
        if (original.Length < 4) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(original.Span) != (uint)uncompressed.Length) return false;
        var buf = ArrayPool<byte>.Shared.Rent(Math.Max(1, uncompressed.Length));
        try
        {
            int read = 0;
            using (var stream = new ZLibStream(new ByteMemorySliceStream(original.Slice(4)), CompressionMode.Decompress))
            {
                while (read < uncompressed.Length)
                {
                    var n = stream.Read(buf, read, uncompressed.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                // The stream must hold nothing more
                if (read == uncompressed.Length && stream.Read(buf, 0, 1) != 0) return false;
            }
            return read == uncompressed.Length && buf.AsSpan(0, read).SequenceEqual(uncompressed);
        }
        catch (InvalidDataException)
        {
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    /// <summary>
    /// Whether Mutagen, compressing the bytes a kept compressed content holds, writes exactly that content again (the
    /// same uncompressed length and zlib stream; CompressionExport compresses at zlib's best level). Then a rebuilt
    /// record needs nothing kept to come back as read (C213).
    /// </summary>
    internal static bool Reproduces(ReadOnlyMemorySlice<byte> original)
    {
        if (original.Length < 4) return false;
        var length = BinaryPrimitives.ReadUInt32LittleEndian(original.Span);
        byte[] uncompressed;
        try
        {
            uncompressed = new byte[checked((int)length)];
            int read = 0;
            using (var stream = new ZLibStream(new ByteMemorySliceStream(original.Slice(4)), CompressionMode.Decompress))
            {
                while (read < uncompressed.Length)
                {
                    var n = stream.Read(uncompressed, read, uncompressed.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read != uncompressed.Length || stream.Read(new byte[1], 0, 1) != 0) return false;
            }
        }
        catch (Exception e) when (e is InvalidDataException or OverflowException)
        {
            return false;
        }
        using var again = new MemoryStream(original.Length);
        using (var stream = new ZLibStream(again, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            stream.Write(uncompressed);
        }
        return again.Length == original.Length - 4
            && again.GetBuffer().AsSpan(0, checked((int)again.Length)).SequenceEqual(original.Span.Slice(4));
    }

    /// <summary>
    /// Checks that a value can be a record's compressed content: an uncompressed length, then a zlib stream
    /// </summary>
    internal static void Validate(ReadOnlySpan<byte> content)
    {
        if (content.Length < 6
            || (content[4] & 0x0F) != 8
            || ((content[4] << 8) | content[5]) % 31 != 0)
        {
            throw new FormatException("Not a record's compressed content (an uncompressed length, then a zlib stream)");
        }
    }
}
