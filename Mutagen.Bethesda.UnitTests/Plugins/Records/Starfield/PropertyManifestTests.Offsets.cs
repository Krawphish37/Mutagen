using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Mapping;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using TempFile = Noggog.IO.TempFile;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C80 (S171) and C81 (W1-M6): the manifest's offsets are where the reader reads each value.
// - Every game's manifest: an object's offsets in one block run in field order without overlapping.
// - Written records and, as RealData, Starfield.esm: every offset held to the value positions (ManifestOffsetCheck).
// - Objects hand-written readers make and fill (from the compiled readers, ReaderReach) say so with enteredBy=, and the
//   manifest gives their fields filledBy= and no generated-order offset.
public partial class PropertyManifestTests
{
    // Another game's manifest (beside Starfield's project)
    private static XElement Manifest(string game) =>
        XDocument.Load(Path.Combine(ProjectDir(), "..", $"Mutagen.Bethesda.{game}", "Manifest", "Properties.xml")).Root!;

    // C80: before, every Loqui field of one type in a block had the last one's offset (Loqui's LoquiType equals any other
    // of the same target object, and the offsets were kept in a dictionary comparing by value), and a record's sound
    // subrecords (PickupSound) took a data subrecord member's offset
    [Theory]
    [InlineData("Starfield")]
    [InlineData("Skyrim")]
    [InlineData("Fallout4")]
    [InlineData("Fallout3")]
    [InlineData("Oblivion")]
    public void Offsets_RunInFieldOrderWithoutOverlapping(string game)
    {
        var problems = new List<string>();
        var count = 0;
        foreach (var obj in Manifest(game).Elements("Object"))
        {
            var name = (string)obj.Attribute("name")!;
            var record = (string?)obj.Attribute("objType") == "Record";
            var ends = new Dictionary<string, (int End, string Field)>();
            foreach (var e in obj.Elements().Where(e => e.Attribute("offset") != null && e.Attribute("derived") == null))
            {
                count++;
                var field = $"{name}.{(string?)e.Attribute("name") ?? e.Name.LocalName}";
                var block = (string?)e.Attribute("data") ?? "";
                if (record && block.Length == 0)
                {
                    problems.Add($"{field}: a record's field outside a data subrecord has offset {e.Attribute("offset")!.Value}");
                    continue;
                }
                var offset = (int)e.Attribute("offset")!;
                if (ends.TryGetValue(block, out var before) && offset < before.End)
                {
                    problems.Add($"{field}: offset {offset} in {(block.Length == 0 ? "its object" : block)}, before the end of {before.Field} ({before.End})");
                }
                ends[block] = (offset + ((int?)e.Attribute("length") ?? 0), field);
            }
        }
        count.ShouldBeGreaterThan(400);
        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems.Take(40)));
    }

    private static List<RecordValuePositions> WrittenPositions(StarfieldMod mod)
    {
        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var path = new ModPath(mod.ModKey, tmp.File.Path);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield);
        return overlay.EnumerateMajorRecords().Select(r => r.GetValuePositions()).ToList();
    }

    // C80: written records holding S171's objects (Weapon WAUD's sounds, MovementData's speeds, CombatStyle's space
    // tables, Explosion's sounds), every manifest offset in them held to where the reader read the value
    [Fact]
    public void LoquiFieldsOfOneType_AreAtTheOffsetsTheReaderReads()
    {
        var mod = new StarfieldMod(ModKey.FromNameAndExtension("ManifestOffsets.esp"), StarfieldRelease.Starfield);
        mod.Weapons.Add(new Weapon(mod));
        mod.CombatStyles.Add(new CombatStyle(mod));
        mod.MovementTypes.Add(new MovementType(mod) { MovementData = new MovementData() });
        mod.Explosions.Add(new Explosion(mod));
        var check = new ManifestOffsetCheck(Manifest());
        foreach (var positions in WrittenPositions(mod))
        {
            check.Check(positions, positions.Record.GetType().Name);
        }
        check.Wrong.ShouldBeEmpty(check.Report());
        check.Unread.ShouldBeEmpty(check.Report());
        foreach (var held in new[]
                 {
                     "Weapon.MeleeOrCreature", "Weapon.Equip", "Weapon.FastEquip", "MovementData.MaxSpeedForward",
                     "MovementData.MaxSpeedLeft", "MovementData.MaxRotationSpeedYaw", "MovementSpeeds.Run",
                     "CombatStyle.SpaceGeneralPowerWeighting", "Explosion.Sound1", "Explosion.Sound2",
                 })
        {
            check.Held.ShouldContainKey(held, check.Report());
        }
    }

    // RealData (C80, C81): every manifest offset met in Starfield.esm is where the reader reads the value, and every one
    // whose bytes a record has is read (up to MUTAGEN_OFFSET_PER_TYPE records of each type, 300 by default, for at most
    // 30 s a type)
    [Fact]
    [Trait("Category", "RealData")]
    public void RealData_EveryManifestOffset_IsWhereTheReaderReads()
    {
        var data = Environment.GetEnvironmentVariable("MUTAGEN_STARFIELD_DATA");
        if (string.IsNullOrEmpty(data)) return; // needs the game's Data folder
        var perType = int.TryParse(Environment.GetEnvironmentVariable("MUTAGEN_OFFSET_PER_TYPE"), out var n) ? n : 300;
        using var mod = StarfieldMod.CreateFromBinaryOverlay(Path.Combine(data, "Starfield.esm"), StarfieldRelease.Starfield);
        var check = new ManifestOffsetCheck(Manifest());
        var groups = MajorRecordTypeEnumerator.GetMajorRecordTypesFor(GameCategory.Starfield)
            .Select(t => (Sig: t.ClassType.GetProperty("StaticRegistration", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                    ?.GetType().GetField("TriggeringRecordType", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)?.ToString(),
                t.GetterType))
            .Where(t => t.Sig != null)
            .GroupBy(t => t.Sig!);
        long records = 0;
        foreach (var group in groups)
        {
            var clock = Stopwatch.StartNew();
            var seen = new HashSet<FormKey>();
            foreach (var (_, getter) in group)
            {
                foreach (var rec in mod.EnumerateMajorRecords(getter))
                {
                    if (seen.Count >= perType || clock.Elapsed.TotalSeconds > 30) break;
                    if (!seen.Add(rec.FormKey)) continue;
                    check.Check(rec.GetValuePositions(), $"{group.Key} {rec.FormKey}");
                    records++;
                }
            }
        }
        check.Wrong.ShouldBeEmpty(check.Report());
        check.Unread.ShouldBeEmpty(check.Report());
        records.ShouldBeGreaterThan(20_000);
        check.Held.Count.ShouldBeGreaterThan(1_900);
    }

    // Each manifest object that is, or derives from, one with enteredBy= (C81) or handRead= (C73): the value
    private static Dictionary<string, string> MarkedByHand(XElement manifest)
    {
        var objects = manifest.Elements("Object").ToDictionary(o => (string)o.Attribute("name")!);
        var ret = new Dictionary<string, string>();
        foreach (var (name, obj) in objects)
        {
            for (var cur = obj; cur != null; cur = (string?)cur.Attribute("baseClass") is { } b ? objects.GetValueOrDefault(b) : null)
            {
                if (((string?)cur.Attribute("enteredBy") ?? (string?)cur.Attribute("handRead")) is { } by)
                {
                    ret[name] = by;
                    break;
                }
            }
        }
        return ret;
    }

    // C81: an object hand-written readers make and fill, rather than its generated reader from its start, says so in the
    // record xml (enteredBy=, or C73's handRead=): its fields' generated-order offsets aren't their places (PTDA holds a
    // package target's kind at 0, its link at 4 and CountOrDistance at 8; the generated order put the link at 12). From the
    // compiled readers: every object whose generated field reader no read reaches, and every object hand-written reading
    // code makes whose generated reader nothing enters from its start, is marked.
    [Fact]
    public void EveryObjectEnteredByHand_IsMarked()
    {
        var reach = ReaderReach.Starfield.Value;
        var manifest = Manifest();
        var marked = MarkedByHand(manifest);
        var required = new SortedDictionary<string, string>();
        foreach (var obj in manifest.Elements("Object").Where(o => (string?)o.Attribute("objType") != "Record"))
        {
            var name = (string)obj.Attribute("name")!;
            if (reach.WithFieldReaders.Contains(name) && !reach.FieldReadersReached.Contains(name))
            {
                required[name] = "no read reaches its generated field reader";
            }
            else if (reach.MadeByHand.TryGetValue(name, out var by) && !reach.EnteredFromStart.Contains(name))
            {
                required[name] = $"made by {string.Join(", ", by.Order())}, and nothing enters its generated reader";
            }
        }
        // The known cases are found; an object the generated reader also reads from its start (Weather's own colors; its
        // clouds' colors are filled by Weather's CloudColors step) keeps its offsets
        foreach (var known in new[] { "APackageTarget", "PackageTargetSpecificReference", "ObjectModFloatProperty", "ScriptIntProperty", "TopicReference", "PerkQuestEffect" })
        {
            required.Keys.ShouldContain(known);
        }
        required.Keys.ShouldNotContain("WeatherColor");
        required.Keys.ShouldNotContain("SoundReference");
        required.Keys.ShouldNotContain("ModStats");
        var missing = required.Where(r => !marked.ContainsKey(r.Key)).Select(r => $"{r.Key}: {r.Value}").ToList();
        missing.ShouldBeEmpty(string.Join(Environment.NewLine, missing));
    }

    // C81: in the manifest, an object entered by hand has enteredBy (its own or its base class's), no offset on a field or
    // step, and each field says who fills it (the readers, its own step, or its own Custom reader)
    [Fact]
    public void EnteredObjects_HaveNoOffsets_AndTheirFieldsSayWhoFillsThem()
    {
        var manifest = Manifest();
        var entered = 0;
        foreach (var (name, by) in MarkedByHand(manifest))
        {
            var obj = manifest.Elements("Object").Single(o => (string)o.Attribute("name")! == name);
            if (obj.Attribute("handRead") != null) continue; // C73's, tested on its own
            entered++;
            ((string?)obj.Attribute("enteredBy")).ShouldBe(by, name);
            foreach (var e in obj.Elements().Where(e => e.Attribute("derived") == null))
            {
                e.Attribute("offset").ShouldBeNull($"{name}.{e.Attribute("name")}");
                if (e.Name.LocalName == "Property" && e.Attribute("reader") == null)
                {
                    e.Attribute("filledBy").ShouldNotBeNull($"{name}.{e.Attribute("name")}");
                }
            }
        }
        entered.ShouldBeGreaterThan(80);
        var target = manifest.Elements("Object").Single(o => (string)o.Attribute("name")! == "PackageTargetSpecificReference");
        ((string?)target.Attribute("enteredBy")).ShouldBe("APackageTarget.CreateFromBinary");
        var reference = target.Elements("Property").Single(p => (string)p.Attribute("name")! == "Reference");
        reference.Attribute("offset").ShouldBeNull();
        ((string?)reference.Attribute("filledBy")).ShouldBe("APackageTarget.CreateFromBinary");
        // TraversalReference.Traversal's own reader starts at Flags (40); the link is at 44 (the xml's layoutOffset), so
        // the reader's start isn't given as its offset
        var traversal = manifest.Elements("Object").Single(o => (string)o.Attribute("name")! == "TraversalReference")
            .Elements("Property").Single(p => (string)p.Attribute("name")! == "Traversal");
        traversal.Attribute("offset").ShouldBeNull();
    }

    // C81: each reader enteredBy names is compiled (Owner.Name: Owner's FillBinary<Name>Custom for a step or Custom field
    // of Owner, or a static method Name of Owner's class or binary create translation) and makes or fills the object (its
    // code names the object's class or a subclass's: newobj, or a member it sets)
    [Fact]
    public void EnteredBy_NamesReadersThatMakeOrFillTheObject()
    {
        var types = StarfieldAssembly.GetTypes();
        var objects = Manifest().Elements("Object").ToDictionary(o => (string)o.Attribute("name")!);
        IEnumerable<string> WithSubclasses(string name) =>
            objects.Values.Where(o => (string?)o.Attribute("baseClass") == name)
                .SelectMany(o => WithSubclasses((string)o.Attribute("name")!)).Prepend(name);
        var named = 0;
        foreach (var obj in objects.Values)
        {
            if ((string?)obj.Attribute("enteredBy") is not { } by) continue;
            // Said where it is set: not again on a subclass that inherits it
            if ((string?)obj.Attribute("baseClass") is { } b && (string?)objects[b].Attribute("enteredBy") == by) continue;
            var name = (string)obj.Attribute("name")!;
            var family = WithSubclasses(name).ToHashSet();
            foreach (var reader in by.Split(','))
            {
                named++;
                var dot = reader.LastIndexOf('.');
                var owner = reader[..dot];
                var method = reader[(dot + 1)..];
                var methods = types
                    .Where(t => t.Namespace == "Mutagen.Bethesda.Starfield"
                                && ReaderReach.Plain(t) is var n && (n == owner || n == owner + "BinaryCreateTranslation"))
                    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    .Where(m => m.Name == method || m.Name == $"FillBinary{method}Custom")
                    .ToList();
                methods.ShouldNotBeEmpty($"{name}: enteredBy {reader} names no compiled reader");
                var touched = methods.SelectMany(TypesNamed).Select(ReaderReach.Plain).ToHashSet();
                touched.Overlaps(family).ShouldBeTrue($"{name}: {reader} neither makes nor fills {string.Join("/", family)}");
            }
        }
        named.ShouldBeGreaterThan(30);
    }

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    // The classes whose methods a method's code names (call, callvirt, newobj, ldftn), following the methods of its own
    // class and of the classes nested in it (lambdas, local functions)
    private static IEnumerable<Type> TypesNamed(MethodBase root)
    {
        static Type Def(Type t) => t.IsGenericType ? t.GetGenericTypeDefinition() : t;
        var home = Def(root.DeclaringType!);
        bool AtHome(Type t)
        {
            for (var cur = t; cur != null; cur = cur.DeclaringType)
            {
                if (Def(cur) == home) return true;
            }
            return false;
        }
        var seen = new HashSet<MethodBase>();
        var queue = new Queue<MethodBase>([root]);
        var ret = new HashSet<Type>();
        while (queue.Count > 0)
        {
            var m = queue.Dequeue();
            if (!seen.Add(m)) continue;
            var il = m.GetMethodBody()?.GetILAsByteArray();
            if (il == null) continue;
            var typeArgs = m.DeclaringType!.IsGenericType ? m.DeclaringType.GetGenericArguments() : null;
            var methodArgs = m.IsGenericMethod ? m.GetGenericArguments() : null;
            int pos = 0;
            while (pos < il.Length)
            {
                OpCode op;
                if (il[pos] == 0xFE)
                {
                    op = OpCodesByValue[unchecked((short)(0xFE00 | il[pos + 1]))];
                    pos += 2;
                }
                else
                {
                    op = OpCodesByValue[il[pos]];
                    pos += 1;
                }
                switch (op.OperandType)
                {
                    case OperandType.InlineMethod:
                    {
                        MethodBase? callee = null;
                        try
                        {
                            callee = m.Module.ResolveMethod(BitConverter.ToInt32(il, pos), typeArgs, methodArgs);
                        }
                        catch (ArgumentException)
                        {
                            // a member the generic context can't name
                        }
                        if (callee?.DeclaringType is { } declaring)
                        {
                            ret.Add(declaring);
                            if (AtHome(declaring)) queue.Enqueue(callee);
                            // An iterator's or async method's body is in its nested state machine's MoveNext
                            if (AtHome(declaring) && Def(declaring) != home)
                            {
                                foreach (var nested in declaring.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                                {
                                    queue.Enqueue(nested);
                                }
                            }
                        }
                        pos += 4;
                        break;
                    }
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        pos += 1;
                        break;
                    case OperandType.InlineVar:
                        pos += 2;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        pos += 8;
                        break;
                    case OperandType.InlineSwitch:
                        pos += 4 + 4 * BitConverter.ToInt32(il, pos);
                        break;
                    default:
                        pos += 4;
                        break;
                }
            }
        }
        return ret;
    }
}
