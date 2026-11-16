using System.Buffers.Binary;
using System.Collections;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.HandReadLayoutFile;

// C186 step 2: reads a record's subrecords by Mutagen.Bethesda.Starfield/Layouts/HandReadLayouts.xml and compares every
// value that names a field with what Mutagen parsed. Every byte of a described subrecord has to be described (a
// leftover is an error), so the file can't silently skip a span. The language is documented at the top of the file.
// C84: with Positions set (Mutagen's value positions of the record, C60), every value read with bytes is also held to
// where Mutagen's reader read it: the value's own position when Mutagen notes one, else inside the nearest position of
// what holds it; a counted list's count to the list's Count position.
public sealed class LayoutChecker
{
    public sealed record Sub(string Sig, byte[] Data);

    private readonly IReadOnlySeparatedMasterPackage _masters;
    public List<string> Errors { get; } = new();
    public Dictionary<string, long> Checked { get; } = new();
    public string Where { get; set; } = "";

    private readonly bool _localized;

    private Dictionary<string, XElement> _defines = new();

    /// <summary>
    /// C84: Mutagen's value positions of the record being checked (C60), or null. Set before CheckRecord and CheckHeader.
    /// </summary>
    public RecordValuePositions? Positions { get; set; }

    // The positions' subrecords don't line up with the walk's (reported once per record; then no position checks)
    private bool _positionsBroken;

    public LayoutChecker(IReadOnlySeparatedMasterPackage masters, bool localized)
    {
        _masters = masters;
        _localized = localized;
    }

    private sealed class Ctx
    {
        public object? Obj;
        public string Path = "";
        public Dictionary<string, double> Vars = new();
        public Dictionary<string, string> Texts = new();
        // C84: where a named value's bytes sit in the record's bytes (start, length), when positions are checked
        public Dictionary<string, (int Start, int Length)> Spans = new();
        public Ctx With(object? obj, string path) => new() { Obj = obj, Path = path, Vars = new(Vars), Texts = new(Texts), Spans = new(Spans) };
    }

    private sealed class Cursor
    {
        public required List<Sub> Subs;
        public int Pos;
        public int End;
    }

    // A Repeat count="fit" tries an item first with this set: no errors, nothing counted
    private bool _trial;

    private void Fail(Ctx ctx, XElement e, string message)
    {
        if (_trial) return;
        if (Errors.Count < 2000) Errors.Add($"{Where} {ctx.Path} <{e.Name.LocalName} {Describe(e)}>: {message}");
        else if (Errors.Count == 2000) Errors.Add("... more errors");
    }

    private static string Describe(XElement e) =>
        string.Join(" ", e.Attributes().Select(a => $"{a.Name}=\"{a.Value}\""));

    private void Count(XElement e) => CountKey(LayoutKey(e));

    private void CountKey(string key)
    {
        if (_trial) return;
        Checked[key] = Checked.GetValueOrDefault(key) + 1;
    }

    public static string LayoutKey(XElement e)
    {
        var parts = e.AncestorsAndSelf().Reverse().Skip(1)
            .Select(x => x.Name.LocalName + ((string?)x.Attribute("sig") ?? (string?)x.Attribute("field") ?? (string?)x.Attribute("value")
                ?? (x.Name.LocalName == "Case" ? (string?)x.Attribute("type") : null) ?? ""));
        return string.Join("/", parts);
    }

    public void CheckRecord(XElement recordLayout, List<Sub> subs, object record)
    {
        if (_defines.Count == 0)
        {
            _defines = recordLayout.Document!.Root!.Elements("Define").ToDictionary(d => (string)d.Attribute("name")!);
        }
        var cursor = new Cursor { Subs = subs, Pos = 0, End = subs.Count };
        var ctx = new Ctx { Obj = record, Path = record.GetType().Name };
        // C84: the record's form version (its header's), for Versioning
        if (Resolve(record, "FormVersion", out var formVersion) && formVersion != null)
        {
            ctx.Vars[FormVersionVar] = Convert.ToDouble(formVersion, CultureInfo.InvariantCulture);
        }
        _positionsBroken = false;
        if (Positions != null)
        {
            var theirs = Positions.Subrecords;
            if (theirs.Count != subs.Count || theirs.Where((t, i) => t.Type.Type != subs[i].Sig || t.ContentLength != subs[i].Data.Length).Any())
            {
                _positionsBroken = true;
                Errors.Add($"{Where}: the value positions' subrecords ({theirs.Count}) don't line up with the record's ({subs.Count})");
            }
        }
        RunSubs(recordLayout.Elements(), cursor, ctx);
    }

    // C84: the record's form version, kept for Versioning
    private const string FormVersionVar = "\u0001formVersion";

    // C57: the record header (a Header entry), its 24 bytes read at the content level against the record (or TES4)
    public void CheckHeader(XElement headerLayout, byte[] head, object record)
    {
        if (_defines.Count == 0)
        {
            _defines = headerLayout.Document!.Root!.Elements("Define").ToDictionary(d => (string)d.Attribute("name")!);
        }
        _positionsBroken = false;
        ReadContent(headerLayout, head, new Ctx { Obj = record, Path = record.GetType().Name + ".(header)" }, subIndex: -1);
        Count(headerLayout);
    }

    // ---- subrecord level ----

    private void RunSubs(IEnumerable<XElement> items, Cursor cur, Ctx ctx)
    {
        foreach (var e in items)
        {
            switch (e.Name.LocalName)
            {
                case "Sub": RunSub(e, cur, ctx); break;
                case "SubRepeat": RunSubRepeat(e, cur, ctx); break;
                case "AnyOrder": RunAnyOrder(e, cur, ctx); break;
                case "Segments": RunSegments(e, cur, ctx); break;
                case "Switch": RunSwitch(e, ctx, children => RunSubs(children, cur, ctx)); break;
                case "TypeSwitch": RunTypeSwitch(e, ctx, children => RunSubs(children, cur, ctx)); break;
                case "Object":
                {
                    // reader="generated": the class is read by Mutagen's generated reader from the subrecords its
                    // children pass over; checked as any object
                    if ((bool?)e.Attribute("optional") == true
                        && Resolve(ctx.Obj, (string)e.Attribute("field")!, out var maybe) && maybe == null)
                    {
                        // a Sub at="cursor" can only follow the object's own subrecords, which aren't there
                        foreach (var sig in e.Descendants().Where(d => !AtCursor(d)).Select(d => (string?)d.Attribute("sig")).OfType<string>().Distinct())
                        {
                            if (Find(cur, sig) >= 0) Fail(ctx, e, $"Mutagen's is null, but {sig} follows");
                        }
                        break;
                    }
                    var (child, ok) = Child(e, ctx);
                    if (ok && IsClass(e, child)) RunSubs(e.Elements(), cur, child);
                    break;
                }
                case "Use": RunSubs(_defines[(string)e.Attribute("name")!].Elements(), cur, ctx); break;
                case "Generated":
                {
                    // subrecords the generated code reads: passed over here
                    var sigs = ((string)e.Attribute("sigs")!).Split(',');
                    while (cur.Pos < cur.End && sigs.Contains(cur.Subs[cur.Pos].Sig)) cur.Pos++;
                    break;
                }
                case "Note": break;
                default: throw new InvalidDataException($"unknown subrecord-level element {e.Name.LocalName}");
            }
        }
    }

    private int Find(Cursor cur, string sig)
    {
        for (int i = cur.Pos; i < cur.End; i++)
        {
            if (cur.Subs[i].Sig == sig) return i;
        }
        return -1;
    }

    private static int FindAny(Cursor cur, IReadOnlyCollection<string> sigs)
    {
        for (int i = cur.Pos; i < cur.End; i++)
        {
            if (sigs.Contains(cur.Subs[i].Sig)) return i;
        }
        return -1;
    }

    // at="cursor": the list starts where the walk stands, or has no items (not looked for further on)
    private static bool AtCursor(XElement e) => (string?)e.Attribute("at") == "cursor";

    private void RunSub(XElement e, Cursor cur, Ctx ctx)
    {
        var sig = (string)e.Attribute("sig")!;
        // C84: at="cursor": the subrecord where the walk stands, or none (not looked for further on)
        var at = AtCursor(e)
            ? (cur.Pos < cur.End && cur.Subs[cur.Pos].Sig == sig ? cur.Pos : -1)
            : Find(cur, sig);
        if (at < 0)
        {
            if ((bool?)e.Attribute("optional") != true) Fail(ctx, e, "subrecord missing");
            else if (e.Attribute("field") != null)
            {
                if (Resolve(ctx.Obj, (string)e.Attribute("field")!, out var v) && v != null && !IsEmpty(v))
                {
                    Fail(ctx, e, $"absent in the file, but Mutagen has {Show(v)}");
                }
            }
            else
            {
                // C84: without field, the lists its direct children name are empty
                foreach (var held in e.Elements().Where(c => c.Name.LocalName is "Repeat"))
                {
                    if ((string?)held.Attribute("field") is { } f && f != "." && Resolve(ctx.Obj, f, out var hv) && hv != null && !IsEmpty(hv))
                    {
                        Fail(ctx, e, $"absent in the file, but Mutagen has {f} {Show(hv)}");
                    }
                }
            }
            return;
        }
        cur.Pos = at + 1;
        var target = ctx;
        if (e.Attribute("field") != null)
        {
            var (child, ok) = Child(e, ctx);
            if (!ok) return;
            target = child;
        }
        ReadContent(e, cur.Subs[at].Data, target, at);
        Count(e);
    }

    private void RunSubRepeat(XElement e, Cursor cur, Ctx ctx)
    {
        var sig = (string)e.Attribute("sig")!;
        var list = ListOf(e, ctx, out var listPath);
        if (list == null) return;
        if (!AtCursor(e) && Find(cur, sig) is var first and >= 0) cur.Pos = first;
        int? count = e.Attribute("count") is { } c ? (int)Number(c.Value, ctx) : null;
        int i = 0;
        while (cur.Pos < cur.End && cur.Subs[cur.Pos].Sig == sig && (count == null || i < count))
        {
            var element = Element(list, i, e);
            ReadContent(e, cur.Subs[cur.Pos].Data, Indexed(ctx, element, ItemPath(list, listPath, i), e, i), cur.Pos);
            cur.Pos++;
            i++;
        }
        if (count != null && i != count) Fail(ctx, e, $"{i} subrecords, expected {count}");
        if (e.Attribute("field") != null && list.Count != i) Fail(ctx, e, $"{i} in the file, Mutagen has {list.Count}");
        Count(e);
    }

    private void RunAnyOrder(XElement e, Cursor cur, Ctx ctx)
    {
        var sigs = e.Elements("Sub").Select(s => (string)s.Attribute("sig")!)
            .Concat(e.Elements("Generated").SelectMany(g => ((string)g.Attribute("sigs")!).Split(','))).ToHashSet();
        var end = cur.Pos;
        while (end < cur.End && sigs.Contains(cur.Subs[end].Sig)) end++;
        foreach (var sub in e.Elements("Sub"))
        {
            var window = new Cursor { Subs = cur.Subs, Pos = cur.Pos, End = end };
            RunSubs([sub], window, ctx);
        }
        cur.Pos = end;
    }

    private void RunSegments(XElement e, Cursor cur, Ctx ctx)
    {
        var starts = ((string)e.Attribute("start")!).Split(',');
        var endSig = (string?)e.Attribute("end");
        var generated = (string?)e.Attribute("reader") == "generated";
        var keyType = (string?)e.Attribute("key");
        var startName = (string?)e.Attribute("startName");
        IList? list = null;
        object? dict = null;
        string listPath;
        if (keyType == null)
        {
            list = ListOf(e, ctx, out listPath);
            if (list == null) return;
        }
        else
        {
            var field = (string)e.Attribute("field")!;
            listPath = $"{ctx.Path}.{field}";
            if (!Resolve(ctx.Obj, field, out dict) || dict == null)
            {
                Fail(ctx, e, $"no dictionary {field} on {ctx.Obj?.GetType().Name ?? "null"}");
                return;
            }
        }
        if (!AtCursor(e) && FindAny(cur, starts) is var first and >= 0) cur.Pos = first;
        // reader="generated" without an end marker: an item runs to the next start or to the first subrecord it can't hold
        var holds = generated && endSig == null ? HeldSigs(e, starts) : null;
        var keys = new List<long>();
        int i = 0;
        while (cur.Pos < cur.End && starts.Contains(cur.Subs[cur.Pos].Sig))
        {
            var segStart = cur.Pos;
            var stop = segStart + 1;
            if (generated && endSig != null)
            {
                // the item runs to its end marker; a start signature inside it is its own subrecord
                while (stop < cur.End && cur.Subs[stop].Sig != endSig) stop++;
            }
            else if (holds != null)
            {
                while (stop < cur.End && !starts.Contains(cur.Subs[stop].Sig) && holds.Contains(cur.Subs[stop].Sig)) stop++;
            }
            else
            {
                while (stop < cur.End && !starts.Contains(cur.Subs[stop].Sig) && (endSig == null || cur.Subs[stop].Sig != endSig)
                       && !Stops(e, cur.Subs[stop].Sig)) stop++;
            }
            var inner = new Cursor { Subs = cur.Subs, Pos = (bool?)e.Attribute("skipStart") == true ? segStart + 1 : segStart, End = stop };
            Ctx itemCtx;
            if (keyType != null)
            {
                var key = KeyOf(cur.Subs[segStart].Data, keyType);
                keys.Add(key);
                var entry = Keyed(dict!, key);
                if (entry == null) Fail(ctx, e, $"segment {i}: no entry of {listPath} has the key {key}");
                itemCtx = Indexed(ctx, entry, $"{listPath}[key {key}]", e, i);
            }
            else
            {
                itemCtx = Indexed(ctx, Element(list!, i, e), ItemPath(list!, listPath, i), e, i);
            }
            if (startName != null) itemCtx.Texts[startName] = cur.Subs[segStart].Sig;
            RunSubs(e.Elements(), inner, itemCtx);
            if (inner.Pos < inner.End && !generated && (string?)e.Attribute("others") != "generated") Fail(ctx, e, $"segment {i}: {string.Join(",", cur.Subs.Skip(inner.Pos).Take(inner.End - inner.Pos).Select(s => s.Sig))} not described");
            cur.Pos = stop;
            if (endSig != null && cur.Pos < cur.End && cur.Subs[cur.Pos].Sig == endSig) cur.Pos++;
            i++;
        }
        // prefix: the file holds the first N elements of a longer Mutagen list (or dictionary)
        var prefix = (bool?)e.Attribute("prefix") == true;
        if (keyType == null && e.Attribute("field") != null && (prefix ? list!.Count < i : list!.Count != i)) Fail(ctx, e, $"{i} segments in the file, Mutagen has {list.Count}");
        // keyList="List" (with key): a list of the object the Segments is in, not of the item, that holds the items' keys
        // in file order (null: none)
        if (keyType != null && (string?)e.Attribute("keyList") is { } keyList)
        {
            if (!Resolve(ctx.Obj, keyList, out var held)) Fail(ctx, e, $"no list {keyList} on {ctx.Obj?.GetType().Name ?? "null"}");
            else
            {
                var mutagen = held == null ? new List<long>() : ((IEnumerable)held).Cast<object>().Select(k => Convert.ToInt64(k, CultureInfo.InvariantCulture)).ToList();
                if (!mutagen.SequenceEqual(keys)) Fail(ctx, e, $"keys in the file {string.Join(",", keys)}, Mutagen's {keyList} {string.Join(",", mutagen)}");
                CountKey($"{LayoutKey(e)}/keyList{keyList}");
            }
        }
        Count(e);
    }

    // The signatures an item of a reader="generated" Segments without an end marker can hold: its starts and every
    // signature its children describe or pass over (Sub, SubRepeat, Generated, nested Segments; through Use)
    private HashSet<string> HeldSigs(XElement e, IEnumerable<string> starts)
    {
        var ret = starts.ToHashSet();
        void Walk(XElement x)
        {
            foreach (var c in x.Elements())
            {
                if (c.Name.LocalName == "Use") Walk(_defines[(string)c.Attribute("name")!]);
                foreach (var a in new[] { "sig", "sigs", "start", "end" })
                {
                    if (c.Attribute(a) is { } v) ret.UnionWith(v.Value.Split(','));
                }
                Walk(c);
            }
        }
        Walk(e);
        return ret;
    }

    // key="I8" (U8, I16, U16, I32, U32): the number at offset 0 of the item's start subrecord
    private static long KeyOf(byte[] data, string type) => type switch
    {
        "I8" => (sbyte)data[0],
        "U8" => data[0],
        "I16" => BinaryPrimitives.ReadInt16LittleEndian(data),
        "U16" => BinaryPrimitives.ReadUInt16LittleEndian(data),
        "I32" => BinaryPrimitives.ReadInt32LittleEndian(data),
        "U32" => BinaryPrimitives.ReadUInt32LittleEndian(data),
        _ => throw new InvalidDataException($"key type {type}"),
    };

    // The value under that key (Mutagen's class: an IDictionary; an overlay: an IReadOnlyDictionary)
    private static object? Keyed(object dict, long key)
    {
        if (dict is IDictionary d)
        {
            foreach (DictionaryEntry entry in d)
            {
                if (Convert.ToInt64(entry.Key, CultureInfo.InvariantCulture) == key) return entry.Value;
            }
            return null;
        }
        foreach (var pair in (IEnumerable)dict)
        {
            var type = pair.GetType();
            if (Convert.ToInt64(type.GetProperty("Key")!.GetValue(pair), CultureInfo.InvariantCulture) == key)
            {
                return type.GetProperty("Value")!.GetValue(pair);
            }
        }
        return null;
    }

    private static bool Stops(XElement e, string sig) =>
        ((string?)e.Attribute("stopAt"))?.Split(',').Contains(sig) == true;

    // ---- content level ----

    private sealed class Reader(byte[] data, int @base = -1)
    {
        public readonly byte[] Data = data;
        // C84: where Data starts in the record's bytes (Positions.Bytes); -1: positions aren't checked
        public readonly int Base = @base;
        public int Pos;
        public int Remaining => Data.Length - Pos;
        public ReadOnlySpan<byte> Take(int n)
        {
            if (n > Remaining) throw new EndOfStreamException($"needs {n} bytes, {Remaining} left");
            var s = Data.AsSpan(Pos, n);
            Pos += n;
            return s;
        }
    }

    // subIndex: the subrecord's index in the record (-1: the record header), for value positions
    private void ReadContent(XElement e, byte[] data, Ctx ctx, int subIndex = -2)
    {
        var @base = -1;
        if (Positions != null && !_positionsBroken)
        {
            if (subIndex == -1) @base = 0;
            else if (subIndex >= 0) @base = Positions.Subrecords[subIndex].ContentStart;
        }
        var r = new Reader(data, @base);
        try
        {
            RunContent(e.Elements(), r, ctx);
        }
        catch (EndOfStreamException ex)
        {
            Fail(ctx, e, ex.Message);
            return;
        }
        if (r.Remaining > 0) Fail(ctx, e, $"{r.Remaining} of {data.Length} bytes not described");
    }

    // C84: the reader and the place the element being read started at, for the value's position
    private Reader? _valueReader;
    private int _valueStart;

    // C86: the bytes the last Bytes element read (a ReflectionFormLinks reads its FormIDs from them)
    private byte[]? _lastBytes;

    private void RunContent(IEnumerable<XElement> items, Reader r, Ctx ctx)
    {
        foreach (var e in items)
        {
            var kind = e.Name.LocalName;
            _valueReader = r;
            _valueStart = r.Pos;
            switch (kind)
            {
                case "U8": Scalar(e, r, ctx, r.Take(1)[0]); break;
                case "I8": Scalar(e, r, ctx, (sbyte)r.Take(1)[0]); break;
                case "U16": Scalar(e, r, ctx, BinaryPrimitives.ReadUInt16LittleEndian(r.Take(2))); break;
                case "I16": Scalar(e, r, ctx, BinaryPrimitives.ReadInt16LittleEndian(r.Take(2))); break;
                case "U32": Scalar(e, r, ctx, BinaryPrimitives.ReadUInt32LittleEndian(r.Take(4))); break;
                case "I32": Scalar(e, r, ctx, BinaryPrimitives.ReadInt32LittleEndian(r.Take(4))); break;
                case "F32": Scalar(e, r, ctx, BinaryPrimitives.ReadSingleLittleEndian(r.Take(4))); break;
                case "FormID":
                {
                    var bytes = r.Take(4);
                    var raw = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
                    var key = FormKeyBinaryTranslation.Instance.Parse(bytes, _masters);
                    // name: 0 for a null FormID (as Mutagen's IsNull), else the stored value
                    if (e.Attribute("name") is { } fn)
                    {
                        ctx.Vars[fn.Value] = key.IsNull ? 0 : raw;
                        KeepSpan(ctx, fn.Value);
                    }
                    Value(e, ctx, key);
                    break;
                }
                case "P3F32":
                {
                    var b = r.Take(12);
                    Value(e, ctx, new P3Float(BinaryPrimitives.ReadSingleLittleEndian(b), BinaryPrimitives.ReadSingleLittleEndian(b[4..]), BinaryPrimitives.ReadSingleLittleEndian(b[8..])));
                    break;
                }
                case "P3I16":
                {
                    var b = r.Take(6);
                    Value(e, ctx, new P3Int16(BinaryPrimitives.ReadInt16LittleEndian(b), BinaryPrimitives.ReadInt16LittleEndian(b[2..]), BinaryPrimitives.ReadInt16LittleEndian(b[4..])));
                    break;
                }
                case "F16": Value(e, ctx, BinaryPrimitives.ReadHalfLittleEndian(r.Take(2))); break;
                case "Null":
                {
                    // no bytes: Mutagen's field is null (or empty) here
                    Count(e);
                    CheckNull(e, ctx, (string)e.Attribute("field")!, "");
                    break;
                }
                case "NotLast":
                {
                    // inside a counted Repeat: the children on every item but the last; on the last their fields are null
                    if (!ctx.Vars.TryGetValue(LastVar, out var last)) throw new InvalidDataException("NotLast outside a counted Repeat");
                    if (last == 0) RunContent(e.Elements(), r, ctx);
                    else
                    {
                        Count(e);
                        foreach (var f in e.Descendants().Select(d => (string?)d.Attribute("field")).OfType<string>())
                        {
                            CheckNull(e, ctx, f, "the last item stores none, but ");
                        }
                    }
                    break;
                }
                case "Color":
                {
                    var b = r.Take(4);
                    Value(e, ctx, Color.FromArgb(b[3], b[0], b[1], b[2]));
                    break;
                }
                case "ZString" when e.Attribute("length") != null:
                {
                    var bytes = r.Take((int)Number((string)e.Attribute("length")!, ctx));
                    var zero = bytes.IndexOf((byte)0);
                    Value(e, ctx, Encoding.UTF8.GetString(zero < 0 ? bytes : bytes[..zero]));
                    break;
                }
                case "ZString":
                {
                    var rest = r.Data.AsSpan(r.Pos);
                    var len = rest.IndexOf((byte)0);
                    if (len < 0) throw new EndOfStreamException("no terminating zero");
                    var s = Encoding.UTF8.GetString(r.Take(len));
                    r.Take(1);
                    if (e.Attribute("name") is { } zn) ctx.Texts[zn.Value] = s;
                    Value(e, ctx, s);
                    break;
                }
                case "WString":
                {
                    var len = BinaryPrimitives.ReadUInt16LittleEndian(r.Take(2));
                    if (e.Attribute("lengthName") is { } ln) ctx.Vars[ln.Value] = len;
                    Value(e, ctx, Encoding.UTF8.GetString(r.Take(len)));
                    break;
                }
                case "Bytes":
                {
                    var n = e.Attribute("length") is { } l ? (int)Number(l.Value, ctx) : r.Remaining;
                    var bytes = r.Take(n).ToArray();
                    _lastBytes = bytes;
                    // encoding="length": the field is the number of bytes
                    Value(e, ctx, (string?)e.Attribute("encoding") == "length" ? (double)bytes.Length : bytes);
                    break;
                }
                case "LString":
                {
                    if (_localized)
                    {
                        var id = BinaryPrimitives.ReadUInt32LittleEndian(r.Take(4));
                        if (e.Attribute("field") is { } f && Resolve(ctx.Obj, f.Value, out var ts))
                        {
                            Count(e);
                            var key = ts is Mutagen.Bethesda.Strings.TranslatedString t ? t.StringsKey : null;
                            if (key != id) Fail(ctx, e, $"file strings ID {id:X}, Mutagen {key?.ToString("X") ?? "none"}");
                        }
                    }
                    else
                    {
                        var rest = r.Data.AsSpan(r.Pos);
                        var len = rest.IndexOf((byte)0);
                        var text = Encoding.UTF8.GetString(r.Take(len < 0 ? rest.Length : len));
                        if (len >= 0) r.Take(1);
                        Value(e, ctx, text);
                    }
                    break;
                }
                case "Const":
                {
                    // a field Mutagen derives from a value read earlier (no bytes)
                    Count(e);
                    if (!Resolve(ctx.Obj, (string)e.Attribute("field")!, out var actual)) Fail(ctx, e, "no such property");
                    else
                    {
                        var expected = (string)e.Attribute("value")!;
                        if (expected.StartsWith('$')) expected = ctx.Texts.TryGetValue(expected[1..], out var t) ? t : ctx.Vars[expected[1..]].ToString(CultureInfo.InvariantCulture);
                        if (actual?.ToString() != expected) Fail(ctx, e, $"Mutagen {Show(actual)}, expected {expected}");
                    }
                    break;
                }
                case "Repeat": RunRepeat(e, r, ctx); break;
                case "Use": RunContent(_defines[(string)e.Attribute("name")!].Elements(), r, ctx); break;
                case "IfMore":
                    // the children only when bytes are left
                    if (r.Remaining > 0) RunContent(e.Elements(), r, ctx);
                    break;
                case "Peek":
                {
                    // a value further on, read without moving (named for a Switch)
                    var at = r.Pos + (int)Number((string)e.Attribute("offset")!, ctx);
                    ctx.Vars[(string)e.Attribute("name")!] = (string)e.Attribute("type")! switch
                    {
                        // C84: a FormID, as FormID name keeps one (0 when null, else the stored value)
                        "FormID" => FormKeyBinaryTranslation.Instance.Parse(r.Data.AsSpan(at, 4), _masters).IsNull
                            ? 0
                            : BinaryPrimitives.ReadUInt32LittleEndian(r.Data.AsSpan(at)),
                        "U16" => BinaryPrimitives.ReadUInt16LittleEndian(r.Data.AsSpan(at)),
                        "U32" => BinaryPrimitives.ReadUInt32LittleEndian(r.Data.AsSpan(at)),
                        "U8" => r.Data[at],
                        var t => throw new InvalidDataException($"Peek type {t}"),
                    };
                    break;
                }
                case "TypeSwitch": RunTypeSwitch(e, ctx, children => RunContent(children, r, ctx)); break;
                case "I32String":
                {
                    // int32 length, then the text (with its zero when not empty)
                    var len = BinaryPrimitives.ReadInt32LittleEndian(r.Take(4));
                    var text = Encoding.UTF8.GetString(r.Take(len)).TrimEnd(' ');
                    if (e.Attribute("name") is { } n) ctx.Texts[n.Value] = text;
                    Value(e, ctx, text);
                    break;
                }
                case "Switch": RunSwitch(e, ctx, children => RunContent(children, r, ctx)); break;
                case "Object":
                {
                    var (child, ok) = Child(e, ctx);
                    if (ok && IsClass(e, child)) RunContent(e.Elements(), r, child);
                    else r.Pos = r.Data.Length;
                    break;
                }
                case "U64":
                {
                    // C57: a uint64, compared as one (a double can't hold every value)
                    var value = BinaryPrimitives.ReadUInt64LittleEndian(r.Take(8));
                    if (e.Attribute("name") is { } un)
                    {
                        ctx.Vars[un.Value] = value;
                        KeepSpan(ctx, un.Value);
                    }
                    Value(e, ctx, value);
                    break;
                }
                case "Guid":
                    // C57: 16 bytes, a .NET Guid in its own byte order (as Mutagen reads one)
                    Value(e, ctx, new Guid(r.Take(16)));
                    break;
                case "View":
                {
                    // C57: no bytes: the field is a value read earlier as a whole, seen as the field's type (an enum view
                    // of a flags value); optional: only some classes have the property
                    Count(e);
                    var field = (string)e.Attribute("field")!;
                    if (!Resolve(ctx.Obj, field, out var actual))
                    {
                        if ((bool?)e.Attribute("optional") != true) Fail(ctx, e, $"no property {field} on {ctx.Obj?.GetType().Name ?? "null"}");
                        break;
                    }
                    var of = (ulong)(long)ctx.Vars[(string)e.Attribute("of")!] & 0xFFFF_FFFF;
                    if (actual == null || (Bits(actual) & 0xFFFF_FFFF) != of) Fail(ctx, e, $"Mutagen {Show(actual)}, the value read {of:X}");
                    break;
                }
                case "Bit":
                {
                    var of = (long)ctx.Vars[(string)e.Attribute("of")!];
                    var index = (int)Number((string)e.Attribute("index")!, ctx);
                    var set = (of & (1L << index)) != 0;
                    if ((bool?)e.Attribute("invert") == true) set = !set;
                    Value(e, ctx, set);
                    break;
                }
                case "Versioning":
                {
                    // C84: the children only when the record's form version (its header's) is at least formVersion (the
                    // record xml's Versioning action="Add"); below it, their fields keep their defaults
                    var min = int.Parse((string)e.Attribute("formVersion")!, CultureInfo.InvariantCulture);
                    if (!ctx.Vars.TryGetValue(FormVersionVar, out var formVersion)) throw new InvalidDataException("Versioning outside a record");
                    if (formVersion >= min)
                    {
                        CountKey($"{LayoutKey(e)}/from");
                        RunContent(e.Elements(), r, ctx);
                    }
                    else
                    {
                        CountKey($"{LayoutKey(e)}/below");
                        foreach (var f in e.Descendants().Select(d => (string?)d.Attribute("field")).OfType<string>())
                        {
                            if (!Resolve(ctx.Obj, f, out var v)) Fail(ctx, e, $"no property {f} on {ctx.Obj?.GetType().Name ?? "null"}");
                            else if (!IsDefault(v)) Fail(ctx, e, $"form version {formVersion} is below {min}, but Mutagen has {f} {Show(v)}");
                        }
                    }
                    break;
                }
                case "ReflectionFormLinks":
                {
                    // C86: no bytes: the FormIDs Bethesda's reflection format stores in the bytes the Bytes element before
                    // it read, in order, through the plugin's masters (ReflectionData.FindFormIds, public)
                    Count(e);
                    var blob = _lastBytes ?? throw new InvalidDataException("ReflectionFormLinks without a Bytes before it");
                    var found = Mutagen.Bethesda.Starfield.ReflectionData.FindFormIds(blob)
                        .Select(l => FormKeyBinaryTranslation.Instance.Parse(blob.AsSpan(l.Position, 4), _masters))
                        .ToList();
                    var field = (string)e.Attribute("field")!;
                    if (!Resolve(ctx.Obj, field, out var links) || links is not IEnumerable linkList)
                    {
                        Fail(ctx, e, $"no list {field} on {ctx.Obj?.GetType().Name ?? "null"}");
                        break;
                    }
                    var mutagen = linkList.Cast<IFormLinkGetter>().Select(l => l.FormKeyNullable ?? FormKey.Null).ToList();
                    if (!mutagen.SequenceEqual(found))
                    {
                        Fail(ctx, e, $"the blob holds {string.Join(",", found)}, Mutagen's {field} {string.Join(",", mutagen)}");
                    }
                    if (!_trial && found.Count > 0) Checked[$"{LayoutKey(e)}/links"] = Checked.GetValueOrDefault($"{LayoutKey(e)}/links") + found.Count;
                    break;
                }
                case "Note": break;
                default: throw new InvalidDataException($"unknown content element {kind}");
            }
        }
    }

    // NotLast: 1 on the last item of a counted Repeat
    private const string LastVar = "\u0001last";

    private void RunRepeat(XElement e, Reader r, Ctx ctx)
    {
        var countText = (string?)e.Attribute("count") ?? "end";
        IList? list = null;
        var listPath = ctx.Path;
        if (e.Attribute("field") != null)
        {
            list = ListOf(e, ctx, out listPath);
            if (list == null)
            {
                r.Pos = r.Data.Length;
                return;
            }
        }
        var open = countText is "end" or "fit";
        var count = open ? int.MaxValue : (int)Number(countText, ctx);
        if (list != null && countText.StartsWith('$')) CheckCountPosition(e, ctx, listPath, countText[1..]);
        var zeros = e.Attribute("endsAtZeros") is { } z ? int.Parse(z.Value, CultureInfo.InvariantCulture) : 0;
        int i = 0;
        while (i < count && (!open || r.Remaining > 0))
        {
            // endsAtZeros="N": an item starting with N zero bytes isn't read; it and what follows are left
            if (zeros > 0 && r.Remaining >= zeros && !r.Data.AsSpan(r.Pos, zeros).ContainsAnyExcept((byte)0)) break;
            var element = list != null ? Element(list, i, e) : ctx.Obj;
            var itemCtx = Indexed(ctx, element, list != null ? ItemPath(list, listPath, i) : ctx.Path, e, i);
            if (!open) itemCtx.Vars[LastVar] = i == count - 1 ? 1 : 0;
            if (countText == "fit" && !Fits(e, r, itemCtx)) break;
            RunContent(e.Elements(), r, itemCtx);
            i++;
        }
        if (list != null && (bool?)e.Attribute("prefix") != true && list.Count != i) Fail(ctx, e, $"{i} in the file, Mutagen has {list.Count}");
        if (list != null && (bool?)e.Attribute("prefix") == true && list.Count < i) Fail(ctx, e, $"{i} in the file, Mutagen has only {list.Count}");
        Count(e);
    }

    // count="fit": whether a whole item fits in what is left (tried without errors or counts)
    private bool Fits(XElement e, Reader r, Ctx ctx)
    {
        var trial = new Reader(r.Data) { Pos = r.Pos };
        var was = _trial;
        _trial = true;
        try
        {
            RunContent(e.Elements(), trial, ctx.With(ctx.Obj, ctx.Path));
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
        finally
        {
            _trial = was;
        }
    }

    private void CheckNull(XElement e, Ctx ctx, string field, string prefix)
    {
        if (!Resolve(ctx.Obj, field, out var v)) Fail(ctx, e, $"no property {field} on {ctx.Obj?.GetType().Name ?? "null"}");
        else if (v != null && !IsEmpty(v)) Fail(ctx, e, $"{prefix}Mutagen has {field} {Show(v)}");
    }

    // chooses by the class Mutagen made for the current object
    private void RunTypeSwitch(XElement e, Ctx ctx, Action<IEnumerable<XElement>> run)
    {
        var actual = ctx.Obj?.GetType().Name.Replace("BinaryOverlay", "");
        var chosen = e.Elements("Case").FirstOrDefault(c => ((string)c.Attribute("type")!).Split(',').Contains(actual))
                     ?? e.Element("Default");
        if (chosen == null)
        {
            Fail(ctx, e, $"no case for {actual}");
            return;
        }
        Count(chosen);
        run(chosen.Elements());
    }

    private void RunSwitch(XElement e, Ctx ctx, Action<IEnumerable<XElement>> run)
    {
        var on = (string)e.Attribute("on")!;
        XElement? chosen;
        object value;
        if (ctx.Texts.TryGetValue(on, out var text))
        {
            value = text;
            chosen = e.Elements("Case").FirstOrDefault(c => ((string)c.Attribute("value")!).Split(',').Contains(text))
                     ?? e.Element("Default");
        }
        else
        {
            if (!ctx.Vars.TryGetValue(on, out var raw)) throw new InvalidDataException($"Switch on unknown value {on}");
            var number = (long)raw;
            if (e.Attribute("mask") is { } mask) number &= ParseLong(mask.Value);
            value = number;
            chosen = e.Elements("Case").FirstOrDefault(c => ((string)c.Attribute("value")!).Split(',')
                         .Any(v => ParseLong(v) == number))
                     ?? e.Element("Default");
        }
        if (chosen == null)
        {
            Fail(ctx, e, $"no case for {value}");
            return;
        }
        if (chosen.Attribute("type") is { } type)
        {
            var actual = ctx.Obj?.GetType().Name.Replace("BinaryOverlay", "");
            if (actual != type.Value) Fail(ctx, chosen, $"Mutagen made {actual ?? "null"}");
        }
        Count(chosen);
        run(chosen.Elements());
    }

    // ---- values ----

    private void Scalar(XElement e, Reader r, Ctx ctx, double raw)
    {
        if (e.Attribute("name") is { } name)
        {
            ctx.Vars[name.Value] = raw;
            KeepSpan(ctx, name.Value);
        }
        object value = raw;
        switch ((string?)e.Attribute("encoding"))
        {
            case null: break;
            case "bool": value = raw != 0; break;
            case "speed": value = (double)(((float)raw - 127) / 127f / 10f); break;
            case var enc when enc.StartsWith("drop:"):
                // the stored number with that bit cleared (the record xml's layoutDroppedBits: a bit another field stands for)
                value = (double)((long)raw & ~(1L << int.Parse(enc[5..], CultureInfo.InvariantCulture)));
                break;
            case var enc when enc.StartsWith("div:"):
                value = (double)(float)(raw / double.Parse(enc[4..], CultureInfo.InvariantCulture));
                break;
            default: throw new InvalidDataException($"unknown encoding {e.Attribute("encoding")}");
        }
        Value(e, ctx, value);
    }

    private void Value(XElement e, Ctx ctx, object raw)
    {
        if (e.Attribute("expect") is { } expect && !(raw is byte[] rb ? Convert.ToHexString(rb) == expect.Value.ToUpperInvariant() : Same(raw, expect.Value)))
        {
            Fail(ctx, e, $"file has {Show(raw)}, layout expects {expect.Value}");
        }
        if (e.Attribute("field") is not { } field)
        {
            if (e.Attribute("name") == null && e.Attribute("expect") == null && e.Attribute("why") == null)
            {
                Fail(ctx, e, "value with neither field, name, expect nor why");
            }
            return;
        }
        Count(e);
        if (!Resolve(ctx.Obj, field.Value, out var actual))
        {
            Fail(ctx, e, $"no property {field.Value} on {ctx.Obj?.GetType().Name ?? "null"}");
            return;
        }
        if (!Same(actual, raw)) Fail(ctx, e, $"file {Show(raw)}, Mutagen {Show(actual)}");
        if (ValueSpan() is { } span) CheckPosition(e, ctx, PositionPath(ctx.Path, field.Value), span.Start, span.Length);
    }

    // ---- value positions (C84) ----

    // The bytes of the value just read, in the record's bytes; null when positions aren't checked or it has none
    private (int Start, int Length)? ValueSpan()
    {
        if (_trial || Positions == null || _valueReader is not { Base: >= 0 } r) return null;
        var length = r.Pos - _valueStart;
        return length > 0 ? (r.Base + _valueStart, length) : null;
    }

    private void KeepSpan(Ctx ctx, string name)
    {
        if (ValueSpan() is { } span) ctx.Spans[name] = span;
        else ctx.Spans.Remove(name);
    }

    private static readonly System.Text.RegularExpressions.Regex KeyIndex = new(@"\[key ([^\]]+)\]");

    // The checker's path of a field as a value position's path: from the record (not its class name), a dictionary entry as [key]
    private static string PositionPath(string ctxPath, string field)
    {
        var path = field == "." ? ctxPath : $"{ctxPath}.{field}";
        var dot = path.IndexOf('.');
        path = dot < 0 ? "" : path[(dot + 1)..];
        if (path.StartsWith("(header).", StringComparison.Ordinal)) path = path["(header).".Length..];
        return KeyIndex.Replace(path, "[$1]");
    }

    // What holds a path: the list of an item, the object of a field; null at the record
    private static string? HolderPath(string path)
    {
        if (path.Length == 0) return null;
        var cut = path.EndsWith(']') ? path.LastIndexOf('[') : path.LastIndexOf('.');
        return cut <= 0 ? "" : path[..cut];
    }

    private static string ShowPositions(IEnumerable<ValuePosition> positions) =>
        string.Join(", ", positions.Select(p => $"{p.Kind} {p.Start}+{p.Length}"));

    // A value read at start (length bytes) is where Mutagen noted it: its own position when it has a value position, else
    // inside the nearest position of what holds it
    private void CheckPosition(XElement e, Ctx ctx, string path, int start, int length)
    {
        var key = LayoutKey(e);
        var own = Positions![path];
        if (own.Any(p => p.Kind is ValuePositionKind.Value or ValuePositionKind.Presence or ValuePositionKind.Count))
        {
            if (own.Any(p => p.Start == start && p.Length == length)) CountKey($"{key}/position");
            else Fail(ctx, e, $"read at {start}+{length}, Mutagen's value position of {path}: {ShowPositions(own)}");
            return;
        }
        for (var holder = own.Count > 0 ? path : HolderPath(path); holder != null; holder = HolderPath(holder))
        {
            var outer = Positions[holder];
            if (outer.Count == 0) continue;
            if (outer.Any(p => start >= p.Start && start + length <= p.Start + p.Length)) CountKey($"{key}/positionWithin");
            else Fail(ctx, e, $"read at {start}+{length}, outside Mutagen's positions of {(holder.Length == 0 ? "the record" : holder)}: {ShowPositions(outer)}");
            return;
        }
        CountKey($"{key}/positionNone");
    }

    // A counted list's count (a value named earlier) is where Mutagen noted the list's count
    private void CheckCountPosition(XElement e, Ctx ctx, string listPath, string countName)
    {
        if (_trial || Positions == null || !ctx.Spans.TryGetValue(countName, out var span)) return;
        var path = PositionPath(listPath, ".");
        var counts = Positions[path].Where(p => p.Kind == ValuePositionKind.Count).ToList();
        if (counts.Count == 0) return;
        if (counts.Any(p => p.Start == span.Start && p.Length == span.Length)) CountKey($"{LayoutKey(e)}/positionCount");
        else Fail(ctx, e, $"count read at {span.Start}+{span.Length}, Mutagen's count position of {path}: {ShowPositions(counts)}");
    }

    private static bool Same(object? actual, object raw)
    {
        switch (raw)
        {
            case FormKey key:
                return actual switch
                {
                    IFormLinkGetter link => (link.FormKeyNullable ?? FormKey.Null) == key || (key.IsNull && link.IsNull),
                    IFormLinkIdentifier id => id.FormKey == key,
                    FormKey fk => fk == key,
                    null => key.IsNull,
                    _ => false,
                };
            case Color c:
                return actual is Color a && a.ToArgb() == c.ToArgb();
            case string s:
                return actual?.ToString() == s;
            case byte[] bytes:
                return actual switch
                {
                    byte[] b => b.AsSpan().SequenceEqual(bytes),
                    IEnumerable<byte> b => b.SequenceEqual(bytes),
                    null => bytes.Length == 0,
                    _ => false,
                };
            case bool flag:
                return actual is bool a2 && a2 == flag;
            case ulong u:
                return actual != null && Bits(actual) == u;
            case double d:
                if (actual == null) return false;
                if (actual is float f) return f.Equals((float)d) || (float.IsNaN(f) && double.IsNaN(d));
                if (actual is double dd) return dd.Equals(d);
                if (actual is bool bb) return (bb ? 1 : 0) == d;
                if (actual is Enum en) return Convert.ToInt64(en) == (long)d || Convert.ToUInt64(en) == (ulong)(long)d;
                return Convert.ToDouble(actual, CultureInfo.InvariantCulture) == d;
            default:
                return Equals(actual, raw);
        }
    }

    private static string Show(object? v) => v switch
    {
        null => "null",
        byte[] b => Convert.ToHexString(b),
        IFormLinkGetter l => l.FormKeyNullable?.ToString() ?? "null link",
        _ => v.ToString() ?? "",
    };

    // C84: a field at its type's default (null, empty, zero, false)
    private static bool IsDefault(object? v) =>
        v == null || IsEmpty(v) || (v.GetType().IsValueType && v.Equals(Activator.CreateInstance(v.GetType())));

    private static bool IsEmpty(object v) => v switch
    {
        IFormLinkGetter l => l.IsNull,
        string s => s.Length == 0,
        ICollection c => c.Count == 0,
        IEnumerable en => !en.Cast<object>().Any(),
        _ => false,
    };

    private static long ParseLong(string s) =>
        s.StartsWith("0x") ? long.Parse(s[2..], NumberStyles.HexNumber) : long.Parse(s, CultureInfo.InvariantCulture);

    private static double Number(string text, Ctx ctx) =>
        text.StartsWith('$') ? ctx.Vars[text[1..]] : ParseLong(text);

    // An integer or enum as its bits (a negative int32 as its uint32 bits)
    private static ulong Bits(object v)
    {
        if (v is Enum en) v = Convert.ChangeType(en, Enum.GetUnderlyingType(en.GetType()), CultureInfo.InvariantCulture);
        return v switch
        {
            sbyte x => (byte)x,
            short x => (ushort)x,
            int x => (uint)x,
            long x => (ulong)x,
            _ => Convert.ToUInt64(v, CultureInfo.InvariantCulture),
        };
    }

    // C57: class="X" on an Object: the reader always makes that class there (the field's type has subclasses it never makes)
    private bool IsClass(XElement e, Ctx child)
    {
        if (e.Attribute("class") is not { } cls) return true;
        var actual = child.Obj?.GetType().Name.Replace("BinaryOverlay", "");
        if (actual == cls.Value) return true;
        Fail(child, e, $"Mutagen made {actual ?? "null"}, the layout says the reader always makes {cls.Value}");
        return false;
    }

    // ---- Mutagen side ----

    private (Ctx Child, bool Ok) Child(XElement e, Ctx ctx)
    {
        var field = (string)e.Attribute("field")!;
        if (!Resolve(ctx.Obj, field, out var value))
        {
            Fail(ctx, e, $"no property {field} on {ctx.Obj?.GetType().Name ?? "null"}");
            return (ctx, false);
        }
        if (value == null)
        {
            Fail(ctx, e, "the file has it, Mutagen's is null");
            return (ctx, false);
        }
        Count(e);
        return (ctx.With(value, $"{ctx.Path}.{field}"), true);
    }

    private IList? ListOf(XElement e, Ctx ctx, out string path)
    {
        path = ctx.Path;
        if (e.Attribute("field") is not { } field) return new ArrayList();
        // field=".": the current object is the list (a Sub field="List" holding a Repeat)
        path = field.Value == "." ? ctx.Path : $"{ctx.Path}.{field.Value}";
        if (!Resolve(ctx.Obj, field.Value, out var value))
        {
            Fail(ctx, e, $"no list {field.Value} on {ctx.Obj?.GetType().Name ?? "null"}");
            return null;
        }
        if (value == null) return new ArrayList(); // a null list: the file must have none either
        if (value is IDictionary dict)
        {
            var values = new List<object?>();
            var keys = new List<string>();
            var entry = dict.GetEnumerator();
            while (entry.MoveNext())
            {
                keys.Add(entry.Key.ToString() ?? "");
                values.Add(entry.Value);
            }
            _keys.AddOrUpdate(values, keys);
            return values;
        }
        if (value.GetType().GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)) is { } rod)
        {
            // keyed by an enum or number: element i is the value under key i
            var pairs = ((IEnumerable)value).Cast<object>().ToList();
            var ordered = pairs.OrderBy(p => Convert.ToInt64(p.GetType().GetProperty("Key")!.GetValue(p))).ToList();
            var values = ordered.Select(p => p.GetType().GetProperty("Value")!.GetValue(p)).ToList();
            _keys.AddOrUpdate(values, ordered.Select(p => p.GetType().GetProperty("Key")!.GetValue(p)?.ToString() ?? "").ToList());
            return values;
        }
        return value as IList ?? ((IEnumerable)value).Cast<object>().ToList();
    }

    // C84: the keys of the dictionaries ListOf gave as lists, so an element's path names its entry ([key K]), as value
    // positions do
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<IList, List<string>> _keys = new();

    private string ItemPath(IList list, string listPath, int i) =>
        _keys.TryGetValue(list, out var keys) && i < keys.Count ? $"{listPath}[key {keys[i]}]" : $"{listPath}[{i}]";

    private object? Element(IList list, int i, XElement e) => i < list.Count ? list[i] : null;

    private static Ctx Indexed(Ctx ctx, object? element, string path, XElement e, int i)
    {
        var child = ctx.With(element ?? ctx.Obj, path);
        if (e.Attribute("index") is { } index) child.Vars[index.Value] = i;
        return child;
    }

    // public properties, then public fields (a RecordType's TypeInt)
    private static readonly Dictionary<(Type, string), MemberInfo?> Props = new();

    public static bool Resolve(object? obj, string path, out object? value)
    {
        value = obj;
        if (path == ".") return true;
        foreach (var part in path.Split('.'))
        {
            if (value == null) return true;
            var type = value.GetType();
            MemberInfo? member;
            lock (Props)
            {
                if (!Props.TryGetValue((type, part), out member))
                {
                    member = (MemberInfo?)type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.Name == part && p.GetIndexParameters().Length == 0)
                        .OrderBy(p => p.DeclaringType == type ? 0 : 1).FirstOrDefault()
                        ?? type.GetField(part, BindingFlags.Public | BindingFlags.Instance);
                    Props[(type, part)] = member;
                }
            }
            switch (member)
            {
                case PropertyInfo prop: value = prop.GetValue(value); break;
                case FieldInfo field: value = field.GetValue(value); break;
                default: return false;
            }
        }
        return true;
    }
}
