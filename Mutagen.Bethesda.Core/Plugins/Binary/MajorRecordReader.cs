using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Loqui;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Internals;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Mapping;
using Mutagen.Bethesda.Plugins.Utility;
using Mutagen.Bethesda.Strings;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Binary;

/// <summary>
/// Reads single major records of one plugin straight from their position in the file: no group is walked and nothing
/// else of the plugin is read. Each record is read by its own class's binary reader, as the full import reads it, with
/// the plugin's masters, strings files (for a localized plugin) and encodings, so it comes out the same as the record
/// a full import of the plugin holds.<br/>
/// Several threads can read through one reader at once. The file stays open, for reading, until the reader is disposed.
/// </summary>
public sealed class MajorRecordReader : IDisposable
{
    private readonly Stream _stream;
    private readonly object _streamLock = new();
    private readonly ParsingMeta _meta;
    private readonly GameCategory _category;

    /// <summary>The plugin</summary>
    public ModKey ModKey { get; }

    /// <summary>The game release the plugin is read as</summary>
    public GameRelease Release { get; }

    /// <summary>The length of the file when the reader was made</summary>
    public long Length { get; }

    private MajorRecordReader(Stream stream, ParsingMeta meta, ModKey modKey, GameRelease release)
    {
        _stream = stream;
        _meta = meta;
        ModKey = modKey;
        Release = release;
        _category = release.ToCategory();
        Length = stream.Length;
    }

    /// <summary>
    /// Opens a plugin to read records from it by position
    /// </summary>
    /// <param name="path">The plugin's ModKey and path</param>
    /// <param name="release">The game release to read it as</param>
    /// <param name="param">The reading parameters (file system, masters, strings, encodings), as for a full import</param>
    public static MajorRecordReader FromPath(ModPath path, GameRelease release, BinaryReadParameters? param = null)
    {
        param ??= BinaryReadParameters.Default;
        var meta = ParsingMeta.Factory(param, release, path);
        meta.Parallel = false;
        var fileSystem = param.FileSystem.GetOrDefault();
        meta.RecordInfoCache = new RecordTypeInfoCacheReader(
            () => new MutagenBinaryReadStream(path, meta), path.ModKey, meta.LinkCache);
        var stream = fileSystem.FileStream.New(path.Path, FileMode.Open, FileAccess.Read);
        try
        {
            var header = new ModHeader(meta.Constants, ReadExactly(stream, 0, meta.Constants.MajorConstants.HeaderLength));
            if (meta.Constants.UsesStrings && Enums.HasFlag(header.Flags, Constants.Localized))
            {
                meta.StringsLookup = StringsFolderLookupOverlay.TypicalFactory(release, path.ModKey,
                    Path.GetDirectoryName(path.Path)!, param.StringsParam, fileSystem: param.FileSystem);
            }
            return new MajorRecordReader(stream, meta, path.ModKey, release);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads the record whose header starts at a position of the file
    /// </summary>
    /// <param name="position">Where the record's header starts</param>
    /// <param name="withChildren">For a record that holds others (a cell, a worldspace, a dialog topic, ...): also read
    /// the children group that follows it in the file, so the record comes with its children, as a full import reads
    /// it. Without it the record is read alone, and its children lists are empty.</param>
    /// <exception cref="ArgumentException">There is a group, not a record, at the position, or the record type is not
    /// one of the game's</exception>
    public IMajorRecord ReadAt(long position, bool withChildren = false)
    {
        return Read(ReadBytesAt(position, withChildren), position);
    }

    /// <summary>
    /// The bytes of the record whose header starts at a position of the file, as <see cref="Read"/> takes them: the
    /// record, and with <paramref name="withChildren"/> the children group that follows it, when one does.
    /// </summary>
    public byte[] ReadBytesAt(long position, bool withChildren = false)
    {
        var constants = _meta.Constants;
        lock (_streamLock)
        {
            var headerBytes = ReadExactly(_stream, position, constants.MajorConstants.HeaderLength);
            if (new GroupHeader(constants, headerBytes).IsGroup)
            {
                throw new ArgumentException($"There is a group, not a record, at 0x{position:X} of {ModKey}");
            }
            var header = new MajorRecordHeader(constants, headerBytes);
            long length = header.TotalLength;
            if (withChildren)
            {
                long next = position + length;
                if (next + constants.GroupConstants.HeaderLength <= _stream.Length)
                {
                    var groupHeader = new GroupHeader(constants, ReadExactly(_stream, next, constants.GroupConstants.HeaderLength));
                    // A holder's children group follows it and is labelled with the holder's FormID
                    if (groupHeader.IsGroup
                        && !groupHeader.IsTopLevel
                        && groupHeader.ContainedRecordTypeData.Span.SequenceEqual(headerBytes.AsSpan(12, 4)))
                    {
                        length += groupHeader.TotalLength;
                    }
                }
            }
            return ReadExactly(_stream, position, checked((int)length));
        }
    }

    /// <summary>
    /// Reads a record from its bytes: they start with the record's header, and may go on with its children group
    /// </summary>
    /// <param name="bytes">The record's bytes, as <see cref="ReadBytesAt"/> returns them</param>
    /// <param name="position">Where the bytes start in the file, for the positions errors report</param>
    public IMajorRecord Read(ReadOnlyMemorySlice<byte> bytes, long position = 0)
    {
        var constants = _meta.Constants;
        if (bytes.Length < constants.MajorConstants.HeaderLength || new GroupHeader(constants, bytes).IsGroup)
        {
            throw new ArgumentException($"The bytes at 0x{position:X} of {ModKey} are not a record");
        }
        var recordType = new MajorRecordHeader(constants, bytes).RecordType;
        var create = GetCreator(_category, recordType)
            ?? throw new ArgumentException($"{recordType} at 0x{position:X} of {ModKey} is not a record type Mutagen reads for {_category}");
        // The reading state holds per-record trackers (the form version, ...), so each read has its own copy
        var stream = new MutagenMemoryReadStream(bytes, _meta.CloneForParallelParse(), offsetReference: position);
        return create(new MutagenFrame(stream));
    }

    /// <summary>
    /// The class a record type is read as on its own: the one the full import reads its records as (GMST: GameSetting,
    /// which picks the game setting class from the record's content)
    /// </summary>
    /// <returns>Null if Mutagen has no record of this type for the game</returns>
    public static Type? GetRecordClass(GameCategory category, RecordType recordType)
    {
        return Creators.GetOrAdd(category, BuildCreators).TryGetValue(recordType, out var creator) ? creator.Class : null;
    }

    private static Func<MutagenFrame, IMajorRecord>? GetCreator(GameCategory category, RecordType recordType)
    {
        return Creators.GetOrAdd(category, BuildCreators).TryGetValue(recordType, out var creator) ? creator.Create : null;
    }

    private record Creator(Type Class, Func<MutagenFrame, IMajorRecord> Create);

    private static readonly ConcurrentDictionary<GameCategory, IReadOnlyDictionary<RecordType, Creator>> Creators = new();

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The record classes are registered with Loqui and their readers are public")]
    private static IReadOnlyDictionary<RecordType, Creator> BuildCreators(GameCategory category)
    {
        // Every major record class with its own binary reader, by the record type it is read from
        var candidates = new Dictionary<RecordType, List<(Type Class, MethodInfo Method)>>();
        foreach (var types in MajorRecordTypeEnumerator.GetMajorRecordTypesFor(category))
        {
            if (!LoquiRegistration.TryGetRegister(types.ClassType, out var registration)) continue;
            if (registration.GetType().GetField(Constants.TriggeringRecordTypeMember, BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) is not RecordType recordType) continue;
            var method = types.ClassType.GetMethod(
                "CreateFromBinary",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                new[] { typeof(MutagenFrame), typeof(TypedParseParams) });
            if (method == null || !method.ReturnType.IsAssignableTo(typeof(IMajorRecord))) continue;
            if (!candidates.TryGetValue(recordType, out var list))
            {
                candidates[recordType] = list = new();
            }
            list.Add((types.ClassType, method));
        }

        var topLevel = MajorRecordTypeEnumerator.GetTopLevelMajorRecordTypesFor(category).Select(t => t.ClassType).ToHashSet();
        var ret = new Dictionary<RecordType, Creator>();
        foreach (var (recordType, classes) in candidates)
        {
            // A record type read as several classes (GMST: each game setting class) is read through the base class
            // they share, whose reader picks the class from the record's content
            var roots = classes
                .Where(c => !classes.Any(other => other.Class != c.Class && c.Class.IsSubclassOf(other.Class)))
                .ToArray();
            if (roots.Length > 1)
            {
                // Two unrelated classes of one record type (Oblivion LVSP: LeveledSpell and SpellLeveled; Fallout 3
                // EYES: Eyes and Eye): the one the plugin's group holds
                roots = roots.Where(r => topLevel.Contains(r.Class)).ToArray();
            }
            if (roots.Length != 1) continue;
            var (cls, method) = roots[0];
            var frame = Expression.Parameter(typeof(MutagenFrame), "frame");
            var create = Expression.Lambda<Func<MutagenFrame, IMajorRecord>>(
                Expression.Convert(
                    Expression.Call(method, frame, Expression.Default(typeof(TypedParseParams))),
                    typeof(IMajorRecord)),
                frame).Compile();
            ret[recordType] = new Creator(cls, create);
        }
        return ret;
    }

    private static byte[] ReadExactly(Stream stream, long position, int length)
    {
        var ret = new byte[length];
        stream.Position = position;
        stream.ReadExactly(ret);
        return ret;
    }

    public void Dispose()
    {
        _stream.Dispose();
    }
}
