using System.Buffers.Binary;

namespace Mutagen.Bethesda.Starfield;

/// <summary>
/// Wwise-form strings for the 16-byte sound GUIDs. Mutagen shows a Guid in .NET byte order; xEdit and Wwise read the same
/// 16 bytes as two little-endian 64-bit halves. Example: the bytes that .NET shows as A97E454F-4307-41EE-45D0-7BD23FC3DE8D
/// are the Wwise event {41EE4307-A97E-454F-8DDE-C33FD27BD045}. Read-only views: bytes and equality are unchanged.
/// </summary>
public static class WwiseGuidMixIn
{
    /// <summary>The Guid in Wwise / xEdit form, e.g. {41EE4307-A97E-454F-8DDE-C33FD27BD045}.</summary>
    public static string ToWwiseString(this Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes);
        var high = BinaryPrimitives.ReadUInt64LittleEndian(bytes).ToString("X16");
        var low = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8)).ToString("X16");
        return $"{{{high[..8]}-{high[8..12]}-{high[12..]}-{low[..4]}-{low[4..]}}}";
    }

    /// <summary>The start event's GUID in Wwise / xEdit form.</summary>
    public static string StartWwiseForm(this ISoundReferenceGetter sound) => sound.Start.ToWwiseString();

    /// <summary>The stop event's GUID in Wwise / xEdit form.</summary>
    public static string StopWwiseForm(this ISoundReferenceGetter sound) => sound.Stop.ToWwiseString();
}
