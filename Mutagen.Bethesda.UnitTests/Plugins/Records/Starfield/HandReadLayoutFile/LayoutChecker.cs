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

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.HandReadLayoutFile;

// C186 step 2: reads a record's subrecords by Mutagen.Bethesda.Starfield/Layouts/HandReadLayouts.xml and compares every
// value that names a field with what Mutagen parsed. Every byte of a described subrecord has to be described (a
// leftover is an error), so the file can't silently skip a span. The language is documented at the top of the file.
public sealed class LayoutChecker
{
    public sealed record Sub(string Sig, byte[] Data);

    private readonly IReadOnlySeparatedMasterPackage _masters;
    public List<string> Errors { get; } = new();
    public Dictionary<string, long> Checked { get; } = new();
    public string Where { get; set; } = "";

    private readonly bool _localized;

    private Dictionary<string, XElement> _defines = new();

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
        public Ctx With(object? obj, string path) => new() { Obj = obj, Path = path, Vars = new(Vars), Texts = new(Texts) };
    }

    private sealed class Cursor
    {
        public required List<Sub> Subs;
        public int Pos;
        public int End;
    }

    private void Fail(Ctx ctx, XElement e, string message)
    {
        if (Errors.Count < 2000) Errors.Add($"{Where} {ctx.Path} <{e.Name.LocalName} {Describe(e)}>: {message}");
        else if (Errors.Count == 2000) Errors.Add("... more errors");
    }

    private static string Describe(XElement e) =>
        string.Join(" ", e.Attributes().Select(a => $"{a.Name}=\"{a.Value}\""));

    private void Count(XElement e)
    {
        var key = LayoutKey(e);
        Checked[key] = Checked.GetValueOrDefault(key) + 1;
    }

    public static string LayoutKey(XElement e)
    {
        var parts = e.AncestorsAndSelf().Reverse().Skip(1)
            .Select(x => x.Name.LocalName + ((string?)x.Attribute("sig") ?? (string?)x.Attribute("field") ?? (string?)x.Attribute("value") ?? ""));
        return string.Join("/", parts);
    }

    public void CheckRecord(XElement recordLayout, List<Sub> subs, object record)
    {
        if (_defines.Count == 0)
        {
            _defines = recordLayout.Document!.Root!.Elements("Define").ToDictionary(d => (string)d.Attribute("name")!);
        }
        var cursor = new Cursor { Subs = subs, Pos = 0, End = subs.Count };
        RunSubs(recordLayout.Elements(), cursor, new Ctx { Obj = record, Path = record.GetType().Name });
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
                case "Object":
                {
                    if ((bool?)e.Attribute("optional") == true
                        && Resolve(ctx.Obj, (string)e.Attribute("field")!, out var maybe) && maybe == null)
                    {
                        foreach (var sig in e.Descendants().Select(d => (string?)d.Attribute("sig")).OfType<string>().Distinct())
                        {
                            if (Find(cur, sig) >= 0) Fail(ctx, e, $"Mutagen's is null, but {sig} follows");
                        }
                        break;
                    }
                    var (child, ok) = Child(e, ctx);
                    if (ok) RunSubs(e.Elements(), cur, child);
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

    private void RunSub(XElement e, Cursor cur, Ctx ctx)
    {
        var sig = (string)e.Attribute("sig")!;
        var at = Find(cur, sig);
        if (at < 0)
        {
            if ((bool?)e.Attribute("optional") != true) Fail(ctx, e, "subrecord missing");
            else if (e.Attribute("field") != null && Resolve(ctx.Obj, (string)e.Attribute("field")!, out var v) && v != null && !IsEmpty(v))
            {
                Fail(ctx, e, $"absent in the file, but Mutagen has {Show(v)}");
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
        ReadContent(e, cur.Subs[at].Data, target);
        Count(e);
    }

    private void RunSubRepeat(XElement e, Cursor cur, Ctx ctx)
    {
        var sig = (string)e.Attribute("sig")!;
        var list = ListOf(e, ctx, out var listPath);
        if (list == null) return;
        if (Find(cur, sig) is var first and >= 0) cur.Pos = first;
        int? count = e.Attribute("count") is { } c ? (int)Number(c.Value, ctx) : null;
        int i = 0;
        while (cur.Pos < cur.End && cur.Subs[cur.Pos].Sig == sig && (count == null || i < count))
        {
            var element = Element(list, i, e);
            ReadContent(e, cur.Subs[cur.Pos].Data, Indexed(ctx, element, $"{listPath}[{i}]", e, i));
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
        var start = (string)e.Attribute("start")!;
        var endSig = (string?)e.Attribute("end");
        var list = ListOf(e, ctx, out var listPath);
        if (list == null) return;
        if (Find(cur, start) is var first and >= 0) cur.Pos = first;
        int i = 0;
        while (cur.Pos < cur.End && cur.Subs[cur.Pos].Sig == start)
        {
            var segStart = cur.Pos;
            var stop = segStart + 1;
            while (stop < cur.End && cur.Subs[stop].Sig != start && (endSig == null || cur.Subs[stop].Sig != endSig)
                   && !Stops(e, cur.Subs[stop].Sig)) stop++;
            var inner = new Cursor { Subs = cur.Subs, Pos = (bool?)e.Attribute("skipStart") == true ? segStart + 1 : segStart, End = stop };
            var element = Element(list, i, e);
            RunSubs(e.Elements(), inner, Indexed(ctx, element, $"{listPath}[{i}]", e, i));
            if (inner.Pos < inner.End && (string?)e.Attribute("others") != "generated") Fail(ctx, e, $"segment {i}: {string.Join(",", cur.Subs.Skip(inner.Pos).Take(inner.End - inner.Pos).Select(s => s.Sig))} not described");
            cur.Pos = stop;
            if (endSig != null && cur.Pos < cur.End && cur.Subs[cur.Pos].Sig == endSig) cur.Pos++;
            i++;
        }
        if (e.Attribute("field") != null && list.Count != i) Fail(ctx, e, $"{i} segments in the file, Mutagen has {list.Count}");
        Count(e);
    }

    private static bool Stops(XElement e, string sig) =>
        ((string?)e.Attribute("stopAt"))?.Split(',').Contains(sig) == true;

    // ---- content level ----

    private sealed class Reader(byte[] data)
    {
        public readonly byte[] Data = data;
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

    private void ReadContent(XElement e, byte[] data, Ctx ctx)
    {
        var r = new Reader(data);
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

    private void RunContent(IEnumerable<XElement> items, Reader r, Ctx ctx)
    {
        foreach (var e in items)
        {
            var kind = e.Name.LocalName;
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
                    var key = FormKeyBinaryTranslation.Instance.Parse(r.Take(4), _masters);
                    Value(e, ctx, key);
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
                    Value(e, ctx, r.Take(n).ToArray());
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
                    if (ok) RunContent(e.Elements(), r, child);
                    else r.Pos = r.Data.Length;
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
                case "Note": break;
                default: throw new InvalidDataException($"unknown content element {kind}");
            }
        }
    }

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
        int i = 0;
        var count = countText == "end" ? int.MaxValue : (int)Number(countText, ctx);
        while (i < count && (countText != "end" || r.Remaining > 0))
        {
            var element = list != null ? Element(list, i, e) : ctx.Obj;
            RunContent(e.Elements(), r, Indexed(ctx, element, list != null ? $"{listPath}[{i}]" : ctx.Path, e, i));
            i++;
        }
        if (list != null && (bool?)e.Attribute("prefix") != true && list.Count != i) Fail(ctx, e, $"{i} in the file, Mutagen has {list.Count}");
        if (list != null && (bool?)e.Attribute("prefix") == true && list.Count < i) Fail(ctx, e, $"{i} in the file, Mutagen has only {list.Count}");
        Count(e);
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
        if (e.Attribute("name") is { } name) ctx.Vars[name.Value] = raw;
        object value = raw;
        switch ((string?)e.Attribute("encoding"))
        {
            case null: break;
            case "bool": value = raw != 0; break;
            case "speed": value = (double)(((float)raw - 127) / 127f / 10f); break;
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
        path = $"{ctx.Path}.{field.Value}";
        if (!Resolve(ctx.Obj, field.Value, out var value))
        {
            Fail(ctx, e, $"no list {field.Value} on {ctx.Obj?.GetType().Name ?? "null"}");
            return null;
        }
        if (value == null) return new ArrayList(); // a null list: the file must have none either
        if (value is IDictionary dict) return dict.Values.Cast<object>().ToList();
        if (value.GetType().GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)) is { } rod)
        {
            // keyed by an enum or number: element i is the value under key i
            var pairs = ((IEnumerable)value).Cast<object>().ToList();
            var ordered = pairs.OrderBy(p => Convert.ToInt64(p.GetType().GetProperty("Key")!.GetValue(p))).ToList();
            return ordered.Select(p => p.GetType().GetProperty("Value")!.GetValue(p)).ToList();
        }
        return value as IList ?? ((IEnumerable)value).Cast<object>().ToList();
    }

    private object? Element(IList list, int i, XElement e) => i < list.Count ? list[i] : null;

    private static Ctx Indexed(Ctx ctx, object? element, string path, XElement e, int i)
    {
        var child = ctx.With(element ?? ctx.Obj, path);
        if (e.Attribute("index") is { } index) child.Vars[index.Value] = i;
        return child;
    }

    private static readonly Dictionary<(Type, string), PropertyInfo?> Props = new();

    public static bool Resolve(object? obj, string path, out object? value)
    {
        value = obj;
        if (path == ".") return true;
        foreach (var part in path.Split('.'))
        {
            if (value == null) return true;
            var type = value.GetType();
            PropertyInfo? prop;
            lock (Props)
            {
                if (!Props.TryGetValue((type, part), out prop))
                {
                    prop = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(p => p.Name == part && p.GetIndexParameters().Length == 0)
                        .OrderBy(p => p.DeclaringType == type ? 0 : 1).FirstOrDefault();
                    Props[(type, part)] = prop;
                }
            }
            if (prop == null) return false;
            value = prop.GetValue(value);
        }
        return true;
    }
}
