using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Analysis;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Utility;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.HandReadLayoutFile;

// C186 step 2: Mutagen.Bethesda.Starfield/Layouts/HandReadLayouts.xml describes the layouts the record xml can't (VMAD,
// OMOD/OBTS properties, perk entry points, weather clouds, ...). These tests hold it against Mutagen's own parse:
//   - the file is well formed and every record it names exists (always runs);
//   - RealData: every described subrecord of every record of those types in the plugins (and, C57, each record's 24-byte
//     header by the file's Header entries; TES4, the plugin header, at the start of each plugin), read by the file and compared
//     value by value with Mutagen's full reader, every byte described. Runs when MUTAGEN_STARFIELD_DATA names the Data
//     folder (MUTAGEN_LAYOUT_PLUGINS: a comma list, default Starfield.esm + the 32 official plugins; MUTAGEN_LAYOUT_TYPES:
//     a comma list of record signatures, default every type the file describes); a report of the values checked per
//     layout element goes to MUTAGEN_LAYOUT_REPORT when set.
public class HandReadLayoutFileTests
{
    public static readonly string[] OfficialPlugins =
    [
        "Starfield.esm", "BlueprintShips-SFBGS050.esm", "BlueprintShips-Starfield.esm", "Constellation.esm", "OldMars.esm",
        "SFBGS003.esm", "SFBGS004.esm", "SFBGS006.esm", "SFBGS007.esm", "SFBGS008.esm", "SFBGS00D.esm", "SFBGS047.esm",
        "SFBGS050.esm", "ShatteredSpace.esm", "blueprintships-sfta03.esm", "blueprintships-sfta06.esm",
        "blueprintships-shatteredspace.esm", "sfbgs00a_a.esm", "sfbgs00a_d.esm", "sfbgs00a_e.esm", "sfbgs00a_f.esm",
        "sfbgs00a_g.esm", "sfbgs00a_i.esm", "sfbgs00b.esm", "sfbgs019.esm", "sfbgs021.esm", "sfbgs023.esm",
        "sfta01.esm", "sfta02.esm", "sfta03.esm", "sfta04.esm", "sfta05.esm", "sfta06.esm",
    ];

    public static string LayoutPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Mutagen.Bethesda.Starfield", "Layouts", "HandReadLayouts.xml");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Mutagen.Bethesda.Starfield/Layouts/HandReadLayouts.xml not found above the test output");
    }

    private static readonly Assembly Starfield = typeof(StarfieldMod).Assembly;

    [Fact]
    public void LayoutFileNamesRealRecordsAndProperties()
    {
        var doc = XDocument.Load(LayoutPath());
        doc.Root!.Name.LocalName.ShouldBe("HandReadLayouts");
        var records = doc.Root.Elements("Record").ToList();
        records.ShouldNotBeEmpty();
        foreach (var record in records)
        {
            var type = Starfield.GetType($"Mutagen.Bethesda.Starfield.{(string)record.Attribute("object")!}");
            type.ShouldNotBeNull((string)record.Attribute("object")!);
            ((string)record.Attribute("sig")!).Length.ShouldBe(4);
        }
        foreach (var type in doc.Descendants("Case").Select(c => (string?)c.Attribute("type")).OfType<string>().SelectMany(t => t.Split(',')))
        {
            Starfield.GetType($"Mutagen.Bethesda.Starfield.{type}").ShouldNotBeNull(type);
        }
    }

    [Fact]
    [Trait("Category", "RealData")]
    public void RealData_EveryDescribedSpanMatchesMutagen()
    {
        var data = Environment.GetEnvironmentVariable("MUTAGEN_STARFIELD_DATA");
        if (string.IsNullOrEmpty(data)) return; // needs the game's Data folder
        var plugins = Environment.GetEnvironmentVariable("MUTAGEN_LAYOUT_PLUGINS") is { Length: > 0 } list
            ? list.Split(',')
            : OfficialPlugins;
        // MUTAGEN_LAYOUT_TYPES: a comma list of record signatures to read (default: every type the file describes)
        var types = Environment.GetEnvironmentVariable("MUTAGEN_LAYOUT_TYPES") is { Length: > 0 } typeList
            ? typeList.Split(',')
            : null;
        var result = CheckPlugins(data, plugins, types);
        if (Environment.GetEnvironmentVariable("MUTAGEN_LAYOUT_REPORT") is { Length: > 0 } report)
        {
            File.WriteAllText(report, result.Report());
        }
        result.Errors.ShouldBeEmpty(string.Join(Environment.NewLine, result.Errors.Take(40)));
    }

    public sealed record CheckResult(List<string> Errors, Dictionary<string, long> Checked, Dictionary<string, long> Records, List<string> Never)
    {
        public string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# records read: " + string.Join(", ", Records.Select(r => $"{r.Key} {r.Value}")));
            sb.AppendLine($"# errors: {Errors.Count}");
            foreach (var e in Errors.Take(300)) sb.AppendLine("ERROR " + e);
            sb.AppendLine($"# layout elements never met in these plugins: {Never.Count}");
            foreach (var n in Never) sb.AppendLine("NEVER " + n);
            foreach (var (k, v) in Checked.OrderBy(k => k.Key)) sb.AppendLine($"{v}\t{k}");
            return sb.ToString();
        }
    }

    // Reads every record of the described types (and every record holding an AnyRecord subrecord) in the plugins, raw
    // and with Mutagen's full reader, and checks one against the other by the layout file
    public static CheckResult CheckPlugins(string data, IEnumerable<string> plugins, IReadOnlyCollection<string>? types = null)
    {
        var doc = XDocument.Load(LayoutPath());
        var layouts = doc.Root!.Elements("Record").ToDictionary(r => (string)r.Attribute("sig")!);
        // AnyRecord sig="SIG": checked on every record that has that subrecord, whatever its type
        var anywhere = doc.Root.Elements("AnyRecord").ToList();
        // Header [sig="SIG"] (C57): the record header; with sig, that type's (TES4: the plugin header), else every record's
        var headers = doc.Root.Elements("Header").ToDictionary(h => (string?)h.Attribute("sig") ?? "");
        var classes = RecordClasses();
        var scanned = layouts.Keys.ToHashSet();
        if (anywhere.Count > 0)
        {
            foreach (var (sig, type) in classes)
            {
                if (anywhere.Any(a => a.Elements().Select(x => (string?)x.Attribute("field")).OfType<string>()
                        .Any(f => type.GetProperties().Any(p => p.Name == f)))) scanned.Add(sig);
            }
        }
        if (types != null) scanned.IntersectWith(types);
        var flags = new HeaderMasterFlagCache(data);
        var checkedTotals = new Dictionary<string, long>();
        var errors = new List<string>();
        var records = new Dictionary<string, long>();

        foreach (var plugin in plugins)
        {
            var path = Path.Combine(data, plugin);
            if (!File.Exists(path)) continue;
            var modPath = new ModPath(ModKey.FromFileName(plugin), path);
            var meta = ParsingMeta.Factory(new BinaryReadParameters { MasterFlagsLookup = flags }, GameRelease.Starfield, modPath);
            using var stream = new MutagenBinaryReadStream(path, meta);
            var locations = RecordLocator.GetLocations(stream, new RecordInterest(scanned.Select(k => new RecordType(k))));
            var fileHead = new byte[12];
            using (var fs = File.OpenRead(path)) fs.ReadExactly(fileHead);
            var localized = (BitConverter.ToUInt32(fileHead, 8) & 0x80) != 0;
            if (localized)
            {
                // as a mod import does: translated strings carry their strings ID
                meta.StringsLookup = Mutagen.Bethesda.Strings.StringsFolderLookupOverlay.TypicalFactory(GameRelease.Starfield, modPath.ModKey, data, null);
            }
            var checker = new LayoutChecker(meta.MasterReferences, localized);
            // TES4, the plugin header, isn't in the record locations: read at the file's start
            if (scanned.Contains("TES4"))
            {
                stream.Position = 0;
                var tes4 = ReadRecordBytes(stream);
                var header = Parse(typeof(StarfieldModHeader), new MutagenFrame(new MutagenMemoryReadStream(tes4, meta)));
                checker.Where = $"{plugin} TES4";
                if (layouts.TryGetValue("TES4", out var tes4Layout)) checker.CheckRecord(tes4Layout, RawSubrecords(new MemoryStream(tes4), out _), header);
                if (headers.TryGetValue("TES4", out var tes4Header)) checker.CheckHeader(tes4Header, tes4[..24], header);
                records["TES4"] = records.GetValueOrDefault("TES4") + 1;
            }
            foreach (var marker in locations.ListedRecords.Values)
            {
                if (!scanned.Contains(marker.Record.Type)) continue;
                layouts.TryGetValue(marker.Record.Type, out var layout);
                // the record's bytes, read into memory as a mod import does (hand-written code may need RemainingMemory)
                stream.Position = marker.Location.Min;
                var recordBytes = ReadRecordBytes(stream);
                var subs = RawSubrecords(new MemoryStream(recordBytes), out var raw);
                if (raw.Deleted) continue;
                var applies = anywhere.Where(a => subs.Any(x => x.Sig == (string)a.Attribute("sig")!)).ToList();
                if (layout == null && applies.Count == 0) continue;
                var type = layout != null
                    ? Starfield.GetType($"Mutagen.Bethesda.Starfield.{(string)layout.Attribute("object")!}")!
                    : classes[marker.Record.Type];
                object record;
                try
                {
                    record = Parse(type, new MutagenFrame(new MutagenMemoryReadStream(recordBytes, meta)));
                }
                catch (Exception ex)
                {
                    errors.Add($"{plugin} {marker.FormKey}: Mutagen could not read it: {ex.GetBaseException().Message} at {ex.GetBaseException().StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
                    continue;
                }
                checker.Where = $"{plugin} {marker.Record} {marker.FormKey}";
                if (layout != null) checker.CheckRecord(layout, subs, record);
                foreach (var a in applies) checker.CheckRecord(a, subs, record);
                if ((headers.GetValueOrDefault(marker.Record.Type) ?? headers.GetValueOrDefault("")) is { } recordHeader)
                {
                    checker.CheckHeader(recordHeader, recordBytes[..24], record);
                }
                records[marker.Record.Type] = records.GetValueOrDefault(marker.Record.Type) + 1;
            }
            errors.AddRange(checker.Errors);
            foreach (var (k, v) in checker.Checked) checkedTotals[k] = checkedTotals.GetValueOrDefault(k) + v;
        }

        var never = doc.Descendants()
            .Where(e => e.Attribute("field") != null || e.Name.LocalName is "Case" or "Sub" or "SubRepeat" or "Segments" or "Repeat")
            .Select(LayoutChecker.LayoutKey)
            .Concat(doc.Descendants().Where(e => e.Attribute("keyList") != null)
                .Select(e => $"{LayoutChecker.LayoutKey(e)}/keyList{(string)e.Attribute("keyList")!}"))
            .Distinct().Where(k => !checkedTotals.ContainsKey(k)).ToList();
        return new CheckResult(errors, checkedTotals, records, never);
    }

    // The record at the stream's position, its 24-byte header and its data, as stored
    private static byte[] ReadRecordBytes(Stream stream)
    {
        var head = new byte[24];
        stream.ReadExactly(head);
        var recordBytes = new byte[24 + BitConverter.ToInt32(head, 4)];
        head.CopyTo(recordBytes, 0);
        stream.ReadExactly(recordBytes.AsSpan(24));
        return recordBytes;
    }

    // Record signature -> the Mutagen class that reads it (where several share one, the dispatching base, e.g. OMOD)
    private static Dictionary<string, Type> RecordClasses()
    {
        var ret = new Dictionary<string, Type>();
        foreach (var reg in Starfield.GetTypes().Where(t => t.Name.EndsWith("_Registration")))
        {
            if (reg.GetField("TriggeringRecordType", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is not RecordType sig) continue;
            var cls = Starfield.GetType($"Mutagen.Bethesda.Starfield.{reg.Name[..^"_Registration".Length]}");
            if (cls == null || !typeof(IMajorRecordGetter).IsAssignableFrom(cls)) continue;
            var dispatches = cls.IsAbstract && cls.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(m => m.Name == "CreateFromBinary" && m.GetParameters().Length == 2);
            if (cls.IsAbstract && !dispatches) continue;
            if (ret.TryGetValue(sig.Type, out var existing) && !(dispatches && !existing.IsAbstract)) continue;
            ret[sig.Type] = cls;
        }
        return ret;
    }

    private static readonly Dictionary<Type, MethodInfo> Creators = new();

    private static object Parse(Type type, MutagenFrame frame)
    {
        if (!Creators.TryGetValue(type, out var create))
        {
            create = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "CreateFromBinary" && m.GetParameters().Length == 2
                            && m.GetParameters()[0].ParameterType == typeof(MutagenFrame)
                            && m.GetParameters()[1].ParameterType == typeof(TypedParseParams))
                .OrderBy(m => m.DeclaringType == type ? 0 : 1).First();
            Creators[type] = create;
        }
        return create.Invoke(null, [frame, default(TypedParseParams)])!;
    }

    public sealed record RawHeader(uint Flags, bool Deleted);

    // The record at the stream's position: its subrecords (inflated, XXXX resolved), as the file stores them
    public static List<LayoutChecker.Sub> RawSubrecords(Stream stream, out RawHeader header)
    {
        var head = new byte[24];
        stream.ReadExactly(head);
        var size = BitConverter.ToInt32(head, 4);
        var flags = BitConverter.ToUInt32(head, 8);
        header = new RawHeader(flags, (flags & 0x20) != 0);
        var content = new byte[size];
        stream.ReadExactly(content);
        if ((flags & 0x40000) != 0)
        {
            var inflated = new byte[BitConverter.ToInt32(content, 0)];
            using var z = new ZLibStream(new MemoryStream(content, 4, content.Length - 4), CompressionMode.Decompress);
            z.ReadExactly(inflated);
            content = inflated;
        }
        return Subrecords(content);
    }

    public static List<LayoutChecker.Sub> Subrecords(byte[] content)
    {
        var ret = new List<LayoutChecker.Sub>();
        var pos = 0;
        int? overflow = null;
        while (pos < content.Length)
        {
            var sig = Encoding.ASCII.GetString(content, pos, 4);
            int len = BitConverter.ToUInt16(content, pos + 4);
            if (sig == "XXXX")
            {
                overflow = BitConverter.ToInt32(content, pos + 6);
                pos += 6 + len;
                continue;
            }
            if (overflow != null)
            {
                len = overflow.Value;
                overflow = null;
            }
            ret.Add(new LayoutChecker.Sub(sig, content.AsSpan(pos + 6, len).ToArray()));
            pos += 6 + len;
        }
        return ret;
    }

    // How each master stores its own FormIDs, from its header flags (as the m3 tool does)
    private sealed class HeaderMasterFlagCache(string dataFolder) : Noggog.IReadOnlyCache<IModMasterStyledGetter, ModKey>
    {
        private readonly Dictionary<ModKey, IModMasterStyledGetter> _cache = new();

        public IModMasterStyledGetter TryGetValue(ModKey key)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
            var style = MasterStyle.Full;
            var path = Path.Combine(dataFolder, key.FileName);
            if (File.Exists(path))
            {
                using var fs = File.OpenRead(path);
                var header = new byte[12];
                fs.ReadExactly(header);
                var flags = (StarfieldModHeader.HeaderFlag)BitConverter.ToInt32(header, 8);
                if (flags.HasFlag(StarfieldModHeader.HeaderFlag.Light)) style = MasterStyle.Small;
                else if (flags.HasFlag(StarfieldModHeader.HeaderFlag.Medium)) style = MasterStyle.Medium;
            }
            return _cache[key] = new MasterStyled(key, style);
        }

        private sealed record MasterStyled(ModKey ModKey, MasterStyle MasterStyle) : IModMasterStyledGetter;

        public int Count => _cache.Count;
        public IEnumerable<ModKey> Keys => _cache.Keys;
        public IEnumerable<IModMasterStyledGetter> Items => _cache.Values;
        public bool ContainsKey(ModKey key) => true;
        public IModMasterStyledGetter this[ModKey key] => TryGetValue(key);
        public IEnumerator<Noggog.IKeyValue<ModKey, IModMasterStyledGetter>> GetEnumerator() { yield break; }
        IEnumerator IEnumerable.GetEnumerator() { yield break; }
    }
}
