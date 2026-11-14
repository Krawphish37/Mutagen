using System.Buffers.Binary;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Translations.Binary;

namespace Mutagen.Bethesda.Plugins.Binary.Translations;

/// <summary>
/// A 16-bit IEEE 754 half-precision float, little-endian. Read and written by its bits, so every pattern (NaN payloads,
/// -0, subnormals, infinities) comes back unchanged.
/// </summary>
public sealed class HalfBinaryTranslation : PrimitiveBinaryTranslation<Half, MutagenFrame, MutagenWriter>
{
    public static readonly HalfBinaryTranslation Instance = new();
    public override int ExpectedLength => 2;

    public override Half Parse(MutagenFrame reader)
    {
        return BitConverter.UInt16BitsToHalf(reader.ReadUInt16());
    }

    public override void Write(MutagenWriter writer, Half item)
    {
        writer.Write(BitConverter.HalfToUInt16Bits(item));
    }

    /// <summary>The half stored in the first two bytes of a span (overlay reads).</summary>
    public static Half Read(ReadOnlySpan<byte> span)
    {
        return BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(span));
    }

    /// <summary>
    /// Equality by bits: NaN equals the same NaN pattern, and -0 differs from +0 (Half's == says the opposite of both),
    /// so a record's equality sees every stored difference.
    /// </summary>
    public static bool BitEquals(Half lhs, Half rhs)
    {
        return BitConverter.HalfToUInt16Bits(lhs) == BitConverter.HalfToUInt16Bits(rhs);
    }

    /// <summary>A hash that agrees with <see cref="BitEquals"/>.</summary>
    public static int BitHash(Half item)
    {
        return BitConverter.HalfToUInt16Bits(item);
    }
}
