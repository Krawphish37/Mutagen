using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Loqui;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Internals;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Binary.Translations;

/// <summary>
/// Which fields a record's bytes held (PartialFormMixIn.TryGetPresentFields, C58/W7): the field index each subrecord was
/// read into, as the full import's reader reports it (the same report SubrecordOrder's field starts use), and every
/// field the readers note they fill, hand-written readers included (C75: the value-position notes, ValuePositionRecorder,
/// with a presence-only recording while the record is read). Noted while a record of a type that can be a partial form
/// is read by a full import; worked out for an overlay record, when asked, by reading its bytes again with the full
/// import's reader.
/// </summary>
internal static class PresentFields
{
    private static readonly ConditionalWeakTable<object, int[]> Noted = new();

    // Set while an overlay record's bytes are read again: the next record parsed on this thread notes its fields
    [ThreadStatic] private static bool _noteNext;

    /// <summary>
    /// At the start of a full import's read of a record: a list to note its fields in, or null when they aren't noted
    /// </summary>
    public static List<int>? Begin(IMajorRecordGetter record)
    {
        if (_noteNext)
        {
            _noteNext = false;
            return new List<int>();
        }
        return record.CanBePartialForm() ? new List<int>() : null;
    }

    public static void Remember(object record, IEnumerable<int> fields)
    {
        Noted.AddOrUpdate(record, fields.Distinct().OrderBy(x => x).ToArray());
    }

    /// <summary>
    /// A record's fields: the reader's reported indices, and the fields its readers noted by name (the property names of
    /// its class, as its FieldIndex enum names them)
    /// </summary>
    public static void Remember(IMajorRecordGetter record, IEnumerable<int> fields, IEnumerable<string> noted)
    {
        var byName = FieldIndices(record.Registration);
        var all = new List<int>(fields);
        foreach (var name in noted)
        {
            if (byName.TryGetValue(name, out var index)) all.Add(index);
        }
        Remember(record, all);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IReadOnlyDictionary<string, int>> FieldIndexCache = new();

    // A record class's field names and indices, from its FieldIndex enum (Location_FieldIndex, ...)
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The record classes and their FieldIndex enums are public types of the game assembly")]
    private static IReadOnlyDictionary<string, int> FieldIndices(ILoquiRegistration registration)
    {
        return FieldIndexCache.GetOrAdd(registration.ClassType, static cls =>
        {
            var ret = new Dictionary<string, int>(StringComparer.Ordinal);
            if (cls.Assembly.GetType($"{cls.Namespace}.{cls.Name}_FieldIndex") is { IsEnum: true } fieldIndex)
            {
                foreach (var value in Enum.GetValues(fieldIndex))
                {
                    ret[Enum.GetName(fieldIndex, value)!] = Convert.ToInt32(value);
                }
            }
            return ret;
        });
    }

    public static bool TryGet(object record, [MaybeNullWhen(false)] out int[] fields)
    {
        return Noted.TryGetValue(record, out fields);
    }

    /// <summary>
    /// An overlay record's fields: its bytes (header and content, decompressed) read again by the full import's reader of
    /// its class, which notes them. Kept with the overlay record.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The record classes are registered with Loqui and their registrations' fields are public")]
    public static int[]? FromOverlay(PluginBinaryOverlay overlay, IMajorRecordGetter record)
    {
        var meta = overlay._package.MetaData;
        var constants = meta.Constants;
        if (record.Registration.GetType().GetField(Constants.TriggeringRecordTypeMember, BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null) is not RecordType recordType)
        {
            return null;
        }
        var create = MajorRecordReader.GetCreator(constants.Release.ToCategory(), recordType);
        if (create == null) return null;
        var header = overlay._structData;
        var content = overlay._recordData;
        var majorConstants = constants.MajorConstants;
        if (header.Length != majorConstants.HeaderLength - majorConstants.TypeAndLengthLength) return null;
        var bytes = new byte[majorConstants.HeaderLength + content.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, recordType.TypeInt);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(majorConstants.TypeAndLengthLength - majorConstants.LengthLength), checked((uint)content.Length));
        header.Span.CopyTo(bytes.AsSpan(majorConstants.TypeAndLengthLength));
        content.Span.CopyTo(bytes.AsSpan(majorConstants.HeaderLength));
        // The overlay holds the content decompressed
        var flagsAt = majorConstants.FlagLocationOffset;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(flagsAt),
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(flagsAt)) & ~Constants.CompressedFlag);
        IMajorRecord read;
        _noteNext = true;
        try
        {
            read = create(new MutagenFrame(new MutagenMemoryReadStream(bytes, meta.CloneForParallelParse())));
        }
        finally
        {
            _noteNext = false;
        }
        if (!TryGet(read, out var fields)) return null;
        Noted.AddOrUpdate(record, fields);
        return fields;
    }
}
