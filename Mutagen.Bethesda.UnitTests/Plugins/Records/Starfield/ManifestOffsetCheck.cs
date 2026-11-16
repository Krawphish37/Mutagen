using System.Collections;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C80/C81: holds the property manifest's offsets (Manifest\Properties.xml) to where the reader read each value, from a
// record's value positions (C60). Each value noted under a property with an offset is checked:
//   - a data subrecord's member (data="SIG"): in that subrecord, at the offset of its content;
//   - a field of an object that is a subrecord of its own (recordTypes): in that object's subrecord, at the offset;
//   - a field of an object inside other bytes (a struct): all the object's fields agree on one start, and that start
//     isn't before the object's own position;
//   - and its length, when the manifest gives one and the value is whole bytes.
// And in every object reached, every field with an offset whose bytes are there has a position (an offset no reader
// reads is a wrong one too). Derived properties (declared by hand) are left out.
internal sealed class ManifestOffsetCheck
{
    private readonly Dictionary<string, XElement> _objects;

    public Dictionary<string, long> Held { get; } = new();
    public Dictionary<string, (long Count, string Example)> Wrong { get; } = new();
    public Dictionary<string, (long Count, string Example)> Unread { get; } = new();
    public long Checked { get; private set; }

    public ManifestOffsetCheck(XElement manifest)
    {
        _objects = manifest.Elements("Object").ToDictionary(o => (string)o.Attribute("name")!);
    }

    /// <summary>Every property with a generated offset, as Object.Property</summary>
    public IEnumerable<string> OffsetProperties =>
        _objects.Values.SelectMany(o => o.Elements("Property")
            .Where(p => p.Attribute("offset") != null && p.Attribute("derived") == null)
            .Select(p => $"{o.Attribute("name")!.Value}.{p.Attribute("name")!.Value}"));

    private IEnumerable<XElement> Chain(string typeName)
    {
        for (var cur = _objects.GetValueOrDefault(typeName); cur != null;
             cur = (string?)cur.Attribute("baseClass") is { } b ? _objects.GetValueOrDefault(b) : null)
        {
            yield return cur;
        }
    }

    private (XElement Obj, XElement Prop)? Lookup(string typeName, string name)
    {
        foreach (var o in Chain(typeName))
        {
            var p = o.Elements("Property").FirstOrDefault(e => (string)e.Attribute("name")! == name);
            if (p != null) return (o, p);
        }
        return null;
    }

    private static void Note(Dictionary<string, (long Count, string Example)> d, string key, string example) =>
        d[key] = d.TryGetValue(key, out var v) ? (v.Count + 1, v.Example) : (1, example);

    private sealed record Read(ValuePosition P, string Key, int Offset, int? Length, string? Data);

    public void Check(RecordValuePositions rvp, string where)
    {
        var root = rvp.Record;
        var byParent = new Dictionary<string, (string TypeName, List<Read> Reads)>();
        foreach (var p in rvp.Positions)
        {
            if (p.Kind is ValuePositionKind.HandRead or ValuePositionKind.Count or ValuePositionKind.Presence) continue;
            if (p.Path.EndsWith(']')) continue; // a list item: no property of its own
            var dot = p.Path.LastIndexOf('.');
            var parentPath = dot < 0 ? "" : p.Path[..dot];
            var name = p.Path[(dot + 1)..];
            var parent = parentPath.Length == 0 ? root : Resolve(root, parentPath);
            if (parent == null) continue;
            var typeName = TypeName(parent);
            if (Lookup(typeName, name) is not { } found) continue;
            var (obj, prop) = found;
            if (prop.Attribute("offset") is not { } offset || prop.Attribute("derived") != null) continue;
            if (!byParent.TryGetValue(parentPath, out var group)) byParent[parentPath] = group = (typeName, new List<Read>());
            group.Reads.Add(new Read(p, $"{obj.Attribute("name")!.Value}.{name}", int.Parse(offset.Value),
                (int?)prop.Attribute("length"), (string?)prop.Attribute("data")));
        }
        foreach (var (parentPath, (typeName, reads)) in byParent)
        {
            var recordTypes = Chain(typeName).Select(o => (string?)o.Attribute("recordTypes")).FirstOrDefault(r => r != null)?.Split(',');
            var structs = reads.Where(r => r.Data == null).ToList();
            var anchor = structs.OrderBy(r => r.Offset).ThenBy(r => r.P.Start).FirstOrDefault();
            var probe = (anchor ?? reads[0]).P;
            var objectAt = rvp[parentPath]
                .Where(q => q.Kind is ValuePositionKind.Object or ValuePositionKind.Value
                            && q.Start <= probe.Start && probe.Start <= q.Start + q.Length)
                .OrderByDescending(q => q.Start).FirstOrDefault();
            var start = anchor == null ? 0 : anchor.P.Start - anchor.Offset;
            foreach (var r in reads)
            {
                Checked++;
                var p = r.P;
                string? problem = null;
                if (r.Data != null)
                {
                    if (p.Subrecord?.ToString() != r.Data) problem = $"in {p.Subrecord}, not {r.Data}";
                    else if (p.Offset != r.Offset) problem = $"at {p.Offset} of {r.Data}, the manifest says {r.Offset}";
                }
                else if (recordTypes != null)
                {
                    // In the object's own subrecord: one of its types, or the one its position starts in (a holder may
                    // name another type for it)
                    if (!recordTypes.Contains(p.Subrecord?.ToString()) && p.SubrecordIndex != objectAt?.SubrecordIndex)
                    {
                        problem = $"in {p.Subrecord}, not its object's subrecord";
                    }
                    else if (p.Offset != r.Offset) problem = $"at {p.Offset} of {p.Subrecord}, the manifest says {r.Offset}";
                }
                else if (p.Start - start != r.Offset)
                {
                    problem = $"at {p.Start - start} from {anchor!.Key} (offset {anchor.Offset}), the manifest says {r.Offset}";
                }
                else if (objectAt != null && start < objectAt.Start)
                {
                    problem = $"its object would start {objectAt.Start - start} bytes before where it was read";
                }
                if (problem == null && r.Length is { } length && p.BitMask == 0
                    && p.Kind is ValuePositionKind.Value or ValuePositionKind.Object && p.Length != length)
                {
                    problem = $"{p.Length} bytes, the manifest says {length}";
                }
                if (problem == null) Held[r.Key] = Held.GetValueOrDefault(r.Key) + 1;
                else Note(Wrong, r.Key, $"{where} {p.Path}: {problem}");
            }
        }

        // Every object reached: its fields with an offset, whose bytes are there, were read
        var reached = rvp.Positions.Where(p => p.Kind is ValuePositionKind.Object or ValuePositionKind.Value)
            .Select(p => p.Path).Distinct().Prepend("");
        foreach (var path in reached)
        {
            var obj = path.Length == 0 ? root : Resolve(root, path);
            if (obj == null || obj is string || obj is Enum || obj.GetType().IsPrimitive) continue;
            var objectAt = path.Length == 0
                ? null
                : rvp[path].FirstOrDefault(q => q.Kind is ValuePositionKind.Object or ValuePositionKind.Value);
            foreach (var o in Chain(TypeName(obj)))
            {
                foreach (var prop in o.Elements("Property").Where(e => e.Attribute("offset") != null && e.Attribute("derived") == null))
                {
                    var name = (string)prop.Attribute("name")!;
                    var offset = int.Parse((string)prop.Attribute("offset")!);
                    var length = (int?)prop.Attribute("length") ?? 1;
                    if ((string?)prop.Attribute("data") is { } data)
                    {
                        var sub = rvp.Subrecords.FirstOrDefault(s => s.Type.ToString() == data);
                        if (sub == null || sub.ContentLength < offset + length) continue;
                    }
                    else if (objectAt != null && objectAt.Length < offset + length)
                    {
                        continue;
                    }
                    var full = path.Length == 0 ? name : $"{path}.{name}";
                    if (rvp[full].Count == 0) Note(Unread, $"{o.Attribute("name")!.Value}.{name}", $"{where} {full}");
                }
            }
        }
    }

    public string Report() =>
        string.Join(Environment.NewLine,
            Wrong.OrderBy(k => k.Key).Select(k => $"wrong {k.Key} x{k.Value.Count}: {k.Value.Example}")
                .Concat(Unread.OrderBy(k => k.Key).Select(k => $"not read {k.Key} x{k.Value.Count}: {k.Value.Example}")));

    private static string TypeName(object o) => o.GetType().Name.Split('`')[0];

    // A path's object: property names joined by '.', [i] a list item, [Key] a dictionary entry, [x,y] a 2D array cell
    private static object? Resolve(object? root, string path)
    {
        var cur = root;
        foreach (var token in Tokens(path))
        {
            if (cur == null) return null;
            if (token.StartsWith('['))
            {
                var key = token[1..^1];
                if (key.Contains(','))
                {
                    var xy = key.Split(',').Select(int.Parse).ToArray();
                    var indexer = cur.GetType().GetProperties().FirstOrDefault(p => p.GetIndexParameters().Length == 2);
                    if (indexer == null) return null;
                    cur = indexer.GetValue(cur, [xy[0], xy[1]]);
                    continue;
                }
                if (cur is IDictionary dict)
                {
                    cur = dict.Keys.Cast<object>().Where(k => k.ToString() == key).Select(k => dict[k]).FirstOrDefault();
                    continue;
                }
                if (cur.GetType().GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)))
                {
                    object? found = null;
                    foreach (var kv in (IEnumerable)cur)
                    {
                        if (kv.GetType().GetProperty("Key")!.GetValue(kv)?.ToString() != key) continue;
                        found = kv.GetType().GetProperty("Value")!.GetValue(kv);
                        break;
                    }
                    cur = found;
                    continue;
                }
                if (!int.TryParse(key, out var i)) return null;
                cur = cur switch
                {
                    IList list => i < list.Count ? list[i] : null,
                    IEnumerable e => e.Cast<object?>().ElementAtOrDefault(i),
                    _ => null,
                };
                continue;
            }
            var prop = FindProperty(cur.GetType(), token);
            if (prop == null) return null;
            cur = prop.GetValue(cur);
        }
        return cur;
    }

    private static IEnumerable<string> Tokens(string path)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < path.Length; i++)
        {
            var c = path[i];
            if (c == '.')
            {
                if (sb.Length > 0) yield return sb.ToString();
                sb.Clear();
            }
            else if (c == '[')
            {
                if (sb.Length > 0) yield return sb.ToString();
                sb.Clear();
                var end = path.IndexOf(']', i);
                yield return path[i..(end + 1)];
                i = end;
            }
            else sb.Append(c);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private static PropertyInfo? FindProperty(Type t, string name)
    {
        for (var cur = t; cur != null; cur = cur.BaseType)
        {
            var p = cur.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (p != null && p.GetIndexParameters().Length == 0) return p;
        }
        foreach (var i in t.GetInterfaces())
        {
            var p = i.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (p != null && p.GetIndexParameters().Length == 0) return p;
        }
        return null;
    }
}
