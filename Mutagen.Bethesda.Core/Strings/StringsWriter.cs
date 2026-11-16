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
    // The tables the plugin's own strings were read from (TryRegisterOriginalKey): a table whose strings are as they
    // were read is written back as it was read, and one that changed keeps the bytes of the strings that did not
    private StringsFolderLookupOverlay? _original;
    private bool _originalLooked;

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
            if (_original == null && translated.StringsLookup is StringsFolderLookupOverlay folder) _original = folder;
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
        // The tables this plugin's strings were read from, opened again (a text keeps its ID and plugin, not its table)
        if (_original == null && !_originalLooked)
        {
            _originalLooked = true;
            _original = StringsFolderLookupOverlay.ReopenLastRead(ModKey);
        }

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
            // The table this language's strings were read from, when the plugin is written back under its own name
            var original = _original?.TryGetTable(source, language.Key);
            // Each string's bytes: a string that is as it was read keeps the bytes it was read from (a table can hold
            // bytes its encoding has no letter for: a Windows-1252 letter in a UTF-8 table would come back as a
            // replacement character), any other is encoded
            var bytes = new List<(uint Index, byte[] Bytes)>(language.Value.Count);
            bool asRead = original != null;
            var written = original != null ? new HashSet<uint>() : null;
            foreach (var item in language.Value)
            {
                if (original != null
                    && original.TryGetLocation(item.Index, out var loc)
                    && string.Equals(original.GetStringAtLocation(loc), item.String, StringComparison.Ordinal))
                {
                    bytes.Add((item.Index, original.GetStringBytesAtLocation(loc).ToArray()));
                }
                else
                {
                    asRead = false;
                    var encoded = new byte[encoding.GetByteCount(item.String)];
                    encoding.GetBytes(item.String, encoded);
                    bytes.Add((item.Index, encoded));
                }
                written?.Add(item.Index);
            }
            if (asRead)
            {
                // Nothing of this table changed: it is written back as it was read, byte for byte, with the strings no
                // record of the plugin names (stale translations, an English table's leftovers) and its own layout
                // (one string under several IDs stored once)
                FileSystem.File.WriteAllBytes(
                    Path.Combine(WriteDir.Path, StringsUtility.GetFileName(LanguageFormat, ModKey, language.Key, source)),
                    original!.FileData.ToArray());
                continue;
            }
            if (original != null)
            {
                // A changed table still keeps the strings no record names: nothing the plugin's tables held is dropped
                foreach (var kv in original.Locations)
                {
                    if (written!.Add(kv.Key)) bytes.Add((kv.Key, original.GetStringBytesAtLocation(kv.Value).ToArray()));
                }
                bytes.Sort((x, y) => x.Index.CompareTo(y.Index));
            }
            using var writer = new MutagenWriter(
                FileSystem.FileStream.New(
                    Path.Combine(WriteDir.Path, StringsUtility.GetFileName(LanguageFormat, ModKey, language.Key, source)), 
                    FileMode.Create, FileAccess.Write),
                meta: null!);
            // Write count
            writer.Write(bytes.Count);
            // Write filler for length later
            writer.WriteZeros(4);
            // Write Directory
            int size = 0;
            foreach (var item in bytes)
            {
                writer.Write(item.Index);
                var strLen = item.Bytes.Length;
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
            foreach (var item in bytes)
            {
                switch (source)
                {
                    case StringsSource.Normal:
                        writer.Write(item.Bytes);
                        writer.Write((byte)0);
                        break;
                    case StringsSource.IL:
                    case StringsSource.DL:
                        writer.Write(item.Bytes.Length + 1);
                        writer.Write(item.Bytes);
                        writer.Write((byte)0);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
        }
    }
}