using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Noggog;
using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Strings.DI;

namespace Mutagen.Bethesda.Strings;

/// <summary>
/// Class for compiling strings of various languages, and exporting them to a .strings file
/// </summary>
public sealed class StringsWriter : IDisposable
{
    public DirectoryPath WriteDir { get; }
    private readonly GameRelease _release;
    public ModKey ModKey { get; }
    public IMutagenEncodingProvider EncodingProvider { get; }
    public IFileSystem FileSystem { get; }
    public StringsLanguageFormat LanguageFormat { get; }
    private readonly List<ValueTuple<Language, string, uint>[]> _strings = new();
    private readonly List<ValueTuple<Language, string, uint>[]> _ilStrings = new();
    private readonly List<ValueTuple<Language, string, uint>[]> _dlStrings = new();
    private uint _index = 0;
    private readonly object _indexLock = new();
    private readonly HashSet<uint> _usedIndices = new();
    private readonly Dictionary<(StringsSource Source, uint Key), ValueTuple<Language, string, uint>[]> _keptStrings = new();

    public StringsWriter(
        GameRelease release,
        ModKey modKey, 
        DirectoryPath writeDirectory,
        IMutagenEncodingProvider encodingProvider,
        IFileSystem? fileSystem = null)
    {
        _release = release;
        ModKey = modKey;
        EncodingProvider = encodingProvider;
        FileSystem = fileSystem.GetOrDefault();
        LanguageFormat = GameConstants.Get(release).StringsLanguageFormat ?? throw new ArgumentException($"Tried to get language format for an unsupported game: {release}", nameof(release));
        WriteDir = writeDirectory;
    }

    public uint Register(ITranslatedStringGetter str, StringsSource source)
    {
        if (TryRegisterOriginalKey(str, source, out var key))
        {
            return key;
        }
        // ToDo
        // Add Count member to TranslatedString, or something similar to short circuit array creation if unnecessary
        return Register(source, str);
    }

    public uint Register(string str, Language language, StringsSource source)
    {
        return Register(source, new KeyValuePair<Language, string>(language, str));
    }

    public uint Register(StringsSource source, params KeyValuePair<Language, string>[] strs)
    {
        return Register(source, (IEnumerable<KeyValuePair<Language, string>>)strs);
    }

    public uint Register(StringsSource source, IEnumerable<KeyValuePair<Language, string>> strs)
    {
        if (!strs.Any()) return 0;
        List<ValueTuple<Language, string, uint>[]> strsList = source switch
        {
            StringsSource.Normal => _strings,
            StringsSource.IL => _ilStrings,
            StringsSource.DL => _dlStrings,
            _ => throw new NotImplementedException(),
        };
        try
        {
            var nextIndex = NextIndex();
            lock (strsList)
            {
                strsList.Add(strs.Select(x => new ValueTuple<Language, string, uint>(x.Key, x.Value, nextIndex)).ToArray());
                return nextIndex;
            }
        }
        catch (OverflowException)
        {
            throw new OverflowException("Too many translated strings for current system to handle.");
        }
    }

    /// <summary>
    /// A string read from this same plugin keeps the ID it had, so saving a plugin back doesn't renumber its text.
    /// Several records can share one ID. If the text under a shared ID was edited, the edited one gets a new ID.
    /// </summary>
    private bool TryRegisterOriginalKey(ITranslatedStringGetter str, StringsSource source, out uint key)
    {
        key = 0;
        if (str is not TranslatedString { StringsKey: { } originalKey } translated
            || translated.StringsSource != source
            || translated.StringsMod != ModKey)
        {
            return false;
        }

        var entries = str.Select(x => new ValueTuple<Language, string, uint>(x.Key, x.Value, originalKey)).ToArray();
        lock (_indexLock)
        {
            if (_keptStrings.TryGetValue((source, originalKey), out var existing))
            {
                if (!existing.SequenceEqual(entries)) return false;
                key = originalKey;
                return true;
            }
            if (!_usedIndices.Add(originalKey)) return false;
            _keptStrings[(source, originalKey)] = entries;
        }
        // A key that holds no text in any language stays as it was, pointing at nothing
        if (entries.Length > 0)
        {
            var strsList = source switch
            {
                StringsSource.Normal => _strings,
                StringsSource.IL => _ilStrings,
                StringsSource.DL => _dlStrings,
                _ => throw new NotImplementedException(),
            };
            lock (strsList)
            {
                strsList.Add(entries);
            }
        }
        key = originalKey;
        return true;
    }

    private uint NextIndex()
    {
        lock (_indexLock)
        {
            do
            {
                _index = checked(_index + 1);
            }
            while (!_usedIndices.Add(_index));
            return _index;
        }
    }

    public void Dispose()
    {
        var languages = 
            _strings
                .Concat(_ilStrings)
                .Concat(_dlStrings)
                .SelectMany(x => x)
                .Select(x => x.Item1)
                .ToHashSet();
        WriteStrings(_strings, StringsSource.Normal, languages);
        WriteStrings(_ilStrings, StringsSource.IL, languages);
        WriteStrings(_dlStrings, StringsSource.DL, languages);
    }

    private void WriteStrings(List<ValueTuple<Language, string, uint>[]> strs, StringsSource source, IReadOnlyCollection<Language> requiredLanguages)
    {
        FileSystem.Directory.CreateDirectory(WriteDir);

        var subLists = new Dictionary<Language, List<(string String, uint Index)>>();
        foreach (var req in requiredLanguages)
        {
            subLists.GetOrAdd(req);
        }
        foreach (var item in strs)
        {
            foreach (var lang in item)
            {
                var list = subLists[lang.Item1];
                list.Add((lang.Item2, lang.Item3));
            }
        }

        // In ID order, so the table doesn't depend on which record was written first
        foreach (var list in subLists.Values)
        {
            list.Sort((x, y) => x.Index.CompareTo(y.Index));
        }

        foreach (var language in subLists)
        {
            var encoding = EncodingProvider.GetEncoding(_release, language.Key);
            using var writer = new MutagenWriter(
                FileSystem.FileStream.New(
                    Path.Combine(WriteDir.Path, StringsUtility.GetFileName(LanguageFormat, ModKey, language.Key, source)), 
                    FileMode.Create, FileAccess.Write),
                meta: null!);
            // Write count
            writer.Write(language.Value.Count);
            // Write filler for length later
            writer.WriteZeros(4);
            // Write Directory
            int size = 0;
            foreach (var item in language.Value)
            {
                writer.Write(item.Index);
                var strLen = encoding.GetByteCount(item.String);
                switch (source)
                {
                    case StringsSource.Normal:
                        writer.Write(size);
                        size += strLen + 1;
                        break;
                    case StringsSource.IL:
                    case StringsSource.DL:
                        writer.Write(size);
                        size += strLen + 5;
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
            // Go back and write content length;
            var pos = writer.Position;
            writer.Position = 4;
            writer.Write(size);
            writer.Position = pos;
            // Write strings
            foreach (var item in language.Value)
            {
                switch (source)
                {
                    case StringsSource.Normal:
                        writer.Write(item.String, StringBinaryType.NullTerminate, encoding);
                        break;
                    case StringsSource.IL:
                    case StringsSource.DL:
                        writer.Write(encoding.GetByteCount(item.String) + 1);
                        writer.Write(item.String, StringBinaryType.NullTerminate, encoding);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
        }
    }
}