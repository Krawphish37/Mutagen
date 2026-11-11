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
/// record's stream only when asked (BinaryReadParameters.KeepCompressedRecords): 1.2 GB for Starfield.esm.
/// A copy (DeepCopy, DeepCopyIn) takes over the kept stream of the record it copies.
/// </summary>
internal static class CompressedOriginals
{
    private sealed class Holder
    {
        public readonly ReadOnlyMemorySlice<byte> Content;
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
