using System.Buffers.Binary;
using System.Reflection;
using System.Xml.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Noggog;
using Shouldly;
using Xunit;
using TempFile = Noggog.IO.TempFile;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C186 group 5: simple offsets and encodings of hand-read fields, described in attributes the generator ignores:
//   layoutSubrecord   the subrecord holding the bytes, when the field's own xml doesn't say
//   layoutOffset      byte offset in that subrecord (or in the object's bytes), or after:<Field>
//   layoutEncoding    how the stored value maps to the field (uint8*10min, float/100, uint32!=0, ...)
//   layoutDroppedBits bits stored on disk but not kept in the field (another field carries them)
//   layoutPresentWhen when the bytes are there at all
// Each test writes records, checks the bytes where the attributes say they are, and reads them back (both readers
// where the object can be reached through a mod).
public class HandReadLayoutEncodingTests
{
    private static readonly XNamespace Loqui = "http://tempuri.org/LoquiSource.xsd";
    private static readonly ModKey TestModKey = ModKey.FromNameAndExtension("HandReadLayout.esp");
    private static readonly string[] GroupFiveAttributes =
        ["layoutSubrecord", "layoutEncoding", "layoutDroppedBits", "layoutPresentWhen", "layoutOffset"];

    private static string RecordsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Mutagen.Bethesda.Starfield", "Records");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Mutagen.Bethesda.Starfield/Records not found above the test output");
    }

    // Object.Field -> field element, for every field with a group 5 attribute (the CTDA files belong to group 1)
    private static readonly Lazy<Dictionary<string, XElement>> AnnotatedFields = new(() =>
    {
        var ret = new Dictionary<string, XElement>();
        foreach (var file in Directory.EnumerateFiles(RecordsDir(), "*.xml", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name is "Condition.xml" or "ConditionDatas.xml") continue;
            foreach (var obj in XDocument.Load(file).Root!.Elements(Loqui + "Object"))
            {
                foreach (var field in obj.Descendants())
                {
                    if (field.Attribute("name") is not { } fieldName) continue;
                    if (!GroupFiveAttributes.Any(a => field.Attribute(a) != null)) continue;
                    // group 4 (HandReadGroupLayoutTests): group structure and the traversal fluff bytes
                    if (field.Attribute("layoutGroup") != null
                        || (string?)field.Attribute("layoutEncoding") == "trailingZeroCount") continue;
                    ret.Add($"{obj.Attribute("name")!.Value}.{fieldName.Value}", field);
                }
            }
        }
        return ret;
    });

    private static string? Attr(string key, string attr) =>
        AnnotatedFields.Value.TryGetValue(key, out var e) ? (string?)e.Attribute(attr) : throw new KeyNotFoundException(key);

    private static int Offset(string key) => int.Parse(Attr(key, "layoutOffset")!);

    private static readonly string[] Checked =
    [
        "Climate.SunriseBegin", "Climate.SunriseEnd", "Climate.SunsetBegin", "Climate.SunsetEnd",
        "MagicEffect.Archetype", "MagicEffectArchetype.Type",
        "PlacedObject.Position", "PlacedObject.Rotation", "PlacedObject.HasPlacementData",
        "PlacedNpc.Position", "PlacedNpc.Rotation", "PlacedNpc.HasPlacementData",
        "PlacedPrimitive.Bounds", "Worldspace.ObjectBoundsMin", "Worldspace.ObjectBoundsMax",
        "RegionData.Flags", "RegionData.Priority", "GameSettingBool.Data",
        "LeveledItem.ChanceNone", "LeveledNpc.ChanceNone", "LeveledPackIn.ChanceNone", "LeveledSpaceCell.ChanceNone",
        "FindMatchingRefNearAlias.AliasID", "Subgraph.Role", "Subgraph.Perspective",
        "Package.PackageTemplate", "Package.DataInputVersion", "ColorRecord.Flags",
        "Npc.Flags", "Npc.Level", "PcLevelMult.LevelMult",
        "TraversalReference.Flags", "TraversalReference.Traversal", "TraversalReference.Unknown",
        "NavmeshPathingCell.ParentCell", "NavmeshPathingCell.Grid",
    ];

    private static IEnumerable<string> AssociationKeys() =>
        AnnotatedFields.Value.Keys.Where(k => k.EndsWith(".Association"));

    [Fact]
    public void EveryAnnotatedFieldHasACheck()
    {
        AnnotatedFields.Value.Keys.Except(AssociationKeys()).Order().ShouldBe(Checked.Order());
        AssociationKeys().Count().ShouldBe(13);
        AnnotatedFields.Value.Count.ShouldBe(Checked.Length + 13);
    }

    // ---- writing and finding bytes ----

    private sealed class Written : IDisposable
    {
        private readonly TempFile _tmp;
        public byte[] Bytes { get; }
        public StarfieldMod Full { get; }
        public IStarfieldModDisposableGetter Overlay { get; }

        public Written(StarfieldMod mod)
        {
            _tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
            var path = new ModPath(mod.ModKey, _tmp.File.Path);
            mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();
            Bytes = File.ReadAllBytes(_tmp.File.Path);
            Full = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield);
            Overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield);
        }

        public IEnumerable<T> Both<T>(FormKey formKey) where T : IMajorRecordGetter =>
            new IStarfieldModGetter[] { Full, Overlay }
                .Select(m => (T)m.EnumerateMajorRecords().Single(r => r.FormKey == formKey));

        public void Dispose()
        {
            Overlay.Dispose();
            _tmp.Dispose();
        }
    }

    private static StarfieldMod NewMod() => new(TestModKey, StarfieldRelease.Starfield);

    // The content of the record with this FormID (own records carry mod index 0: the test mod has no masters)
    private static byte[] RecordContent(byte[] file, FormKey formKey)
    {
        var pos = RecordStart(file, formKey);
        return file.AsSpan(pos + 24, BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(pos + 4))).ToArray();
    }

    private static List<(string Sig, byte[] Data)> Subrecords(byte[] content)
    {
        var ret = new List<(string, byte[])>();
        var pos = 0;
        while (pos < content.Length)
        {
            var sig = System.Text.Encoding.ASCII.GetString(content, pos, 4);
            var len = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(pos + 4));
            ret.Add((sig, content.AsSpan(pos + 6, len).ToArray()));
            pos += 6 + len;
        }
        return ret;
    }

    private static byte[] Sub(byte[] file, FormKey formKey, string sig) =>
        Subrecords(RecordContent(file, formKey)).Single(s => s.Sig == sig).Data;

    private static bool HasSub(byte[] file, FormKey formKey, string sig) =>
        Subrecords(RecordContent(file, formKey)).Any(s => s.Sig == sig);

    private static float F(byte[] data, int offset) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset));
    private static int I32(byte[] data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
    private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
    private static ushort U16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
    private static short I16(byte[] data, int offset) => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset));

    // Standalone objects (no major record to reach them through a mod): written and read with the class reader
    private static byte[] WriteObject(Action<MutagenWriter> write)
    {
        var masters = new MasterReferenceCollection(TestModKey);
        var bundle = new WritingBundle(GameConstants.Starfield)
        {
            MasterReferences = masters,
            SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(masters),
        };
        var mem = new MemoryStream();
        using (var writer = new MutagenWriter(mem, bundle, dispose: false))
        {
            write(writer);
        }
        return mem.ToArray();
    }

    private static MutagenFrame Frame(byte[] bytes) =>
        new(new MutagenMemoryReadStream(bytes,
            new ParsingMeta(GameConstants.Starfield, TestModKey,
                SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey)))));

    private static int IndexOf(byte[] haystack, uint value)
    {
        var needle = BitConverter.GetBytes(value);
        for (int i = 0; i + 4 <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, 4).SequenceEqual(needle)) return i;
        }
        throw new KeyNotFoundException(value.ToString("X8"));
    }

    // ---- the checks ----

    [Fact]
    public void ClimateTimesAreTenMinuteSteps()
    {
        string[] fields = ["SunriseBegin", "SunriseEnd", "SunsetBegin", "SunsetEnd"];
        TimeOnly[] times = [new(5, 30), new(6, 0), new(18, 10), new(23, 50)];
        var mod = NewMod();
        var climate = new Climate(mod)
        {
            SunriseBegin = times[0], SunriseEnd = times[1], SunsetBegin = times[2], SunsetEnd = times[3],
            Volatility = 7,
        };
        mod.Climates.Add(climate);
        using var written = new Written(mod);
        var tnam = Sub(written.Bytes, climate.FormKey, "TNAM");
        for (int i = 0; i < fields.Length; i++)
        {
            Attr($"Climate.{fields[i]}", "layoutEncoding").ShouldBe("uint8*10min");
            tnam[Offset($"Climate.{fields[i]}")].ShouldBe((byte)((times[i].Hour * 60 + times[i].Minute) / 10));
        }
        foreach (var read in written.Both<IClimateGetter>(climate.FormKey))
        {
            for (int i = 0; i < fields.Length; i++)
            {
                typeof(IClimateGetter).GetProperty(fields[i])!.GetValue(read).ShouldBe(times[i]);
            }
        }
    }

    [Fact]
    public void MagicEffectAssociationIsDataOffsetZeroAndArchetypeTypeIsOffsetEighty()
    {
        Offset("MagicEffect.Archetype").ShouldBe(80);
        Attr("MagicEffectArchetype.Type", "layoutSubrecord").ShouldBe("DATA");
        Offset("MagicEffectArchetype.Type").ShouldBe(80);

        var mod = NewMod();
        var target = mod.GetNextFormKey();
        var cases = new List<(string Name, MagicEffect Effect, MagicEffectArchetype.TypeEnum Type, bool Selected)>();
        foreach (var key in AssociationKeys())
        {
            var name = key.Split('.')[0];
            Attr(key, "layoutSubrecord").ShouldBe("DATA");
            Offset(key).ShouldBe(0);
            var type = typeof(MagicEffect).Assembly.GetType($"Mutagen.Bethesda.Starfield.{name}")!;
            var archetype = name == nameof(MagicEffectArchetype)
                ? new MagicEffectArchetype(MagicEffectArchetype.TypeEnum.Calm)
                : (AMagicEffectArchetype)System.Activator.CreateInstance(type, nonPublic: true)!;
            var link = type.GetProperty("Association")!.GetValue(archetype)!;
            link.GetType().GetMethod("SetTo", [typeof(FormKey?)])!.Invoke(link, [target]);
            var typeValue = (MagicEffectArchetype.TypeEnum)type.GetProperty("Type", BindingFlags.Public | BindingFlags.Instance)!
                .GetValue(archetype)!;
            var effect = new MagicEffect(mod) { Archetype = archetype };
            mod.MagicEffects.Add(effect);
            var selected = AnnotatedFields.Value[key].Parent!.Parent!.Attribute("selectorValue") != null;
            cases.Add((name, effect, typeValue, selected));
        }
        using var written = new Written(mod);
        foreach (var (name, effect, typeValue, selected) in cases)
        {
            var data = Sub(written.Bytes, effect.FormKey, "DATA");
            U32(data, 0).ShouldBe(target.ID, name);
            I32(data, 80).ShouldBe((int)typeValue, name);
            foreach (var read in written.Both<IMagicEffectGetter>(effect.FormKey))
            {
                read.Archetype.AssociationKey.FormKey.ShouldBe(target, name);
                if (selected)
                {
                    read.Archetype.GetType().Name.Replace("BinaryOverlay", "").ShouldBe(name);
                }
            }
        }
    }

    [Fact]
    public void PlacementDataIsPositionThenRotationAndItsPresenceIsTheFlag()
    {
        foreach (var obj in new[] { "PlacedObject", "PlacedNpc" })
        {
            Attr($"{obj}.Position", "layoutSubrecord").ShouldBe("DATA");
            Attr($"{obj}.Rotation", "layoutSubrecord").ShouldBe("DATA");
            Attr($"{obj}.HasPlacementData", "layoutPresentWhen").ShouldBe("subrecord:DATA");
        }
        Attr("PlacedPrimitive.Bounds", "layoutEncoding").ShouldBe("float*2");

        var mod = NewMod();
        var placed = new PlacedObject(mod)
        {
            Position = new P3Float(1, 2, 3),
            Rotation = new P3Float(4, 5, 6),
            Primitive = new PlacedPrimitive { Bounds = new P3Float(2, 4, 6) },
        };
        // DATA is left out only for a deleted reference without placement
        var bare = new PlacedObject(mod) { EditorID = "Bare", IsDeleted = true };
        var npc = new PlacedNpc(mod) { Position = new P3Float(7, 8, 9), Rotation = new P3Float(10, 11, 12) };
        var cell = new Cell(mod) { EditorID = "TestCell" };
        cell.Temporary.Add(placed);
        cell.Temporary.Add(bare);
        cell.Temporary.Add(npc);
        mod.Cells.Add(new CellBlock
        {
            BlockNumber = 0,
            GroupType = GroupTypeEnum.InteriorCellBlock,
            SubBlocks = { new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = { cell } } },
        });
        using var written = new Written(mod);

        foreach (var (obj, record, first) in new (string, IMajorRecordGetter, float)[] { ("PlacedObject", placed, 1f), ("PlacedNpc", npc, 7f) })
        {
            var data = Sub(written.Bytes, record.FormKey, "DATA");
            var pos = Offset($"{obj}.Position");
            var rot = Offset($"{obj}.Rotation");
            new P3Float(F(data, pos), F(data, pos + 4), F(data, pos + 8)).ShouldBe(new P3Float(first, first + 1, first + 2));
            new P3Float(F(data, rot), F(data, rot + 4), F(data, rot + 8)).ShouldBe(new P3Float(first + 3, first + 4, first + 5));
        }
        HasSub(written.Bytes, bare.FormKey, "DATA").ShouldBeFalse();
        var xprm = Sub(written.Bytes, placed.FormKey, "XPRM");
        new P3Float(F(xprm, 0), F(xprm, 4), F(xprm, 8)).ShouldBe(new P3Float(1, 2, 3));

        foreach (var read in written.Both<IPlacedObjectGetter>(placed.FormKey))
        {
            read.HasPlacementData.ShouldBeTrue();
            read.Position.ShouldBe(placed.Position);
            read.Rotation.ShouldBe(placed.Rotation);
            read.Primitive!.Bounds.ShouldBe(new P3Float(2, 4, 6));
        }
        foreach (var read in written.Both<IPlacedObjectGetter>(bare.FormKey))
        {
            read.HasPlacementData.ShouldBeFalse();
        }
        foreach (var read in written.Both<IPlacedNpcGetter>(npc.FormKey))
        {
            read.HasPlacementData.ShouldBeTrue();
            read.Position.ShouldBe(npc.Position);
            read.Rotation.ShouldBe(npc.Rotation);
        }
    }

    [Fact]
    public void WorldspaceBoundsAreStoredTimesOneHundred()
    {
        Attr("Worldspace.ObjectBoundsMin", "layoutEncoding").ShouldBe("float/100");
        Attr("Worldspace.ObjectBoundsMax", "layoutEncoding").ShouldBe("float/100");
        var mod = NewMod();
        var world = new Worldspace(mod)
        {
            ObjectBoundsMin = new P2Float(1.5f, -2f),
            ObjectBoundsMax = new P2Float(3f, 4.25f),
        };
        mod.Worldspaces.Add(world);
        using var written = new Written(mod);
        var nam0 = Sub(written.Bytes, world.FormKey, "NAM0");
        var nam9 = Sub(written.Bytes, world.FormKey, "NAM9");
        (F(nam0, 0), F(nam0, 4)).ShouldBe((150f, -200f));
        (F(nam9, 0), F(nam9, 4)).ShouldBe((300f, 425f));
        foreach (var read in written.Both<IWorldspaceGetter>(world.FormKey))
        {
            read.ObjectBoundsMin.ShouldBe(world.ObjectBoundsMin);
            read.ObjectBoundsMax.ShouldBe(world.ObjectBoundsMax);
        }
    }

    [Fact]
    public void RegionDataHeaderHoldsFlagsAndPriority()
    {
        Attr("RegionData.Flags", "layoutSubrecord").ShouldBe("RDAT");
        Attr("RegionData.Priority", "layoutSubrecord").ShouldBe("RDAT");
        var mod = NewMod();
        var region = new Region(mod)
        {
            Weather = new RegionWeather { Flags = RegionData.RegionDataFlag.Override, Priority = 7 },
        };
        mod.Regions.Add(region);
        using var written = new Written(mod);
        var rdat = Sub(written.Bytes, region.FormKey, "RDAT");
        I32(rdat, 0).ShouldBe((int)RegionData.RegionDataType.Weather);
        rdat[Offset("RegionData.Flags")].ShouldBe((byte)RegionData.RegionDataFlag.Override);
        rdat[Offset("RegionData.Priority")].ShouldBe((byte)7);
        foreach (var read in written.Both<IRegionGetter>(region.FormKey))
        {
            read.Weather!.Flags.ShouldBe(RegionData.RegionDataFlag.Override);
            read.Weather.Priority.ShouldBe((byte)7);
        }
    }

    [Fact]
    public void GameSettingBoolIsAUInt32ReadAsNotZero()
    {
        Attr("GameSettingBool.Data", "layoutEncoding").ShouldBe("uint32!=0");
        var mod = NewMod();
        var gmst = new GameSettingBool(mod) { EditorID = "bHandReadLayout", Data = true };
        mod.GameSettings.Add(gmst);
        using var written = new Written(mod);
        var data = Sub(written.Bytes, gmst.FormKey, "DATA");
        data.Length.ShouldBe(4);
        U32(data, 0).ShouldBe(1u);

        // a stored 2 also reads as true
        var bytes = written.Bytes.ToArray();
        var start = RecordStart(bytes, gmst.FormKey);
        var record = bytes.AsSpan(start, 24 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(start + 4))).ToArray();
        var dataAt = 24 + IndexOfSequence(record.AsSpan(24).ToArray(), "DATA"u8.ToArray()) + 6;
        record[dataAt] = 2;
        GameSettingBool.CreateFromBinary(Frame(record)).Data.ShouldBe(true);
    }

    private static int RecordStart(byte[] file, FormKey formKey)
    {
        var pos = 0;
        while (pos < file.Length)
        {
            var sig = System.Text.Encoding.ASCII.GetString(file, pos, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(pos + 4));
            if (sig == "GRUP")
            {
                pos += 24;
                continue;
            }
            if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(pos + 12)) == formKey.ID) return pos;
            pos += 24 + size;
        }
        throw new KeyNotFoundException(formKey.ToString());
    }

    private static int IndexOfSequence(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        }
        throw new KeyNotFoundException();
    }

    [Fact]
    public void LeveledChanceNoneIsAByteBeforeFormVersion510AndAFloatAfter()
    {
        string[] objects = ["LeveledItem", "LeveledNpc", "LeveledPackIn", "LeveledSpaceCell"];
        foreach (var obj in objects)
        {
            Attr($"{obj}.ChanceNone", "layoutEncoding").ShouldBe("formVersion<510:uint8/255;float");
        }
        var mod = NewMod();
        var records = new List<IStarfieldMajorRecord>();
        foreach (ushort version in new ushort[] { 509, 510 })
        {
            var item = new LeveledItem(mod) { ChanceNone = 0.5f, FormVersion = version };
            var npc = new LeveledNpc(mod) { ChanceNone = 0.5f, FormVersion = version };
            var packIn = new LeveledPackIn(mod) { ChanceNone = 0.5f, FormVersion = version };
            var spaceCell = new LeveledSpaceCell(mod) { ChanceNone = 0.5f, FormVersion = version };
            mod.LeveledItems.Add(item);
            mod.LeveledNpcs.Add(npc);
            mod.LeveledPackIns.Add(packIn);
            mod.LeveledSpaceCells.Add(spaceCell);
            records.AddRange([item, npc, packIn, spaceCell]);
        }
        using var written = new Written(mod);
        foreach (var record in records)
        {
            var lvld = Sub(written.Bytes, record.FormKey, "LVLD");
            var old = record.FormVersion < 510;
            if (old)
            {
                lvld.ShouldBe(new byte[] { 128 });
            }
            else
            {
                lvld.Length.ShouldBe(4);
                F(lvld, 0).ShouldBe(0.5f);
            }
            foreach (var read in written.Both<IMajorRecordGetter>(record.FormKey))
            {
                var chance = (float)read.GetType().GetProperty("ChanceNone")!.GetValue(read)!;
                chance.ShouldBe(old ? 128 / 255f : 0.5f);
            }
        }
    }

    [Fact]
    public void FindMatchingRefNearAliasIdIsAnInt32()
    {
        Attr("FindMatchingRefNearAlias.AliasID", "layoutEncoding").ShouldBe("int32");
        var alias = new FindMatchingRefNearAlias { AliasID = -3 };
        var bytes = WriteObject(w => alias.WriteToBinary(w));
        var alna = Subrecords(bytes).Single(s => s.Sig == "ALNA").Data;
        alna.Length.ShouldBe(4);
        I32(alna, 0).ShouldBe(-3);
        FindMatchingRefNearAlias.CreateFromBinary(Frame(bytes)).AliasID.ShouldBe((short)-3);
    }

    [Fact]
    public void SubgraphRoleAndPerspectiveAreUInt16s()
    {
        Attr("Subgraph.Role", "layoutEncoding").ShouldBe("uint16");
        Attr("Subgraph.Perspective", "layoutSubrecord").ShouldBe("SRAF");
        var subgraph = new Subgraph { Role = Subgraph.SubgraphRole.Furniture, Perspective = Perspective.First };
        var bytes = WriteObject(w => subgraph.WriteToBinary(w));
        var sraf = Subrecords(bytes).Single(s => s.Sig == "SRAF").Data;
        sraf.Length.ShouldBe(4);
        U16(sraf, Offset("Subgraph.Role")).ShouldBe((ushort)Subgraph.SubgraphRole.Furniture);
        U16(sraf, Offset("Subgraph.Perspective")).ShouldBe((ushort)Perspective.First);
        var read = Subgraph.CreateFromBinary(Frame(bytes));
        read.Role.ShouldBe(Subgraph.SubgraphRole.Furniture);
        read.Perspective.ShouldBe(Perspective.First);
    }

    [Fact]
    public void PackageTemplateAndDataInputVersionShareThePkcu()
    {
        Attr("Package.DataInputVersion", "layoutSubrecord").ShouldBe("PKCU");
        var mod = NewMod();
        var template = mod.GetNextFormKey();
        var package = new Package(mod) { DataInputVersion = 3 };
        package.PackageTemplate.SetTo(template);
        mod.Packages.Add(package);
        using var written = new Written(mod);
        var pkcu = Sub(written.Bytes, package.FormKey, "PKCU");
        pkcu.Length.ShouldBe(12);
        U32(pkcu, 0).ShouldBe(0u); // the Data count
        U32(pkcu, Offset("Package.PackageTemplate")).ShouldBe(template.ID);
        I32(pkcu, Offset("Package.DataInputVersion")).ShouldBe(3);
        foreach (var read in written.Both<IPackageGetter>(package.FormKey))
        {
            read.PackageTemplate.FormKey.ShouldBe(template);
            read.DataInputVersion.ShouldBe(3);
        }
    }

    [Fact]
    public void DroppedFlagBitsAreStoredButNotKept()
    {
        Attr("ColorRecord.Flags", "layoutDroppedBits").ShouldBe("1");
        Attr("Npc.Flags", "layoutDroppedBits").ShouldBe("7");
        Attr("PcLevelMult.LevelMult", "layoutEncoding").ShouldBe("uint16/1000");
        Offset("Npc.Level").ShouldBe(6);

        var mod = NewMod();
        var color = new ColorRecord(mod)
        {
            Data = new ColorRemappingIndex(),
            Flags = ColorRecord.Flag.Playable | ColorRecord.Flag.ExtendedLut,
        };
        var npc = new Npc(mod) { Flags = Npc.Flag.Female, Level = new PcLevelMult { LevelMult = 1.5f } };
        mod.Colors.Add(color);
        mod.Npcs.Add(npc);
        using var written = new Written(mod);

        U32(Sub(written.Bytes, color.FormKey, "FNAM"), 0).ShouldBe(0x1u | 0x2u | 0x4u);
        var acbs = Sub(written.Bytes, npc.FormKey, "ACBS");
        U32(acbs, 0).ShouldBe((uint)Npc.Flag.Female | (1u << 7));
        U16(acbs, Offset("Npc.Level")).ShouldBe((ushort)1500);

        foreach (var read in written.Both<IColorRecordGetter>(color.FormKey))
        {
            read.Flags.ShouldBe(ColorRecord.Flag.Playable | ColorRecord.Flag.ExtendedLut);
            read.Data.ShouldBeAssignableTo<IColorRemappingIndexGetter>();
        }
        foreach (var read in written.Both<INpcGetter>(npc.FormKey))
        {
            read.Flags.ShouldBe(Npc.Flag.Female);
            read.Level.ShouldBeAssignableTo<IPcLevelMultGetter>()!.LevelMult.ShouldBe(1.5f);
        }
    }

    [Fact]
    public void TraversalFormIdIsThereOnlyWhenFlagBitTwoIsClear()
    {
        Offset("TraversalReference.Flags").ShouldBe(40);
        Attr("TraversalReference.Flags", "layoutDroppedBits").ShouldBe("2");
        Offset("TraversalReference.Traversal").ShouldBe(44);
        Attr("TraversalReference.Traversal", "layoutPresentWhen").ShouldBe("field:Flags:bit=2:clear");
        Attr("TraversalReference.Unknown", "layoutOffset").ShouldBe("after:Traversal");

        var target = new FormKey(TestModKey, 0x900);
        byte[] unknown = [1, 2, 3, 4, 5, 6, 7, 8];
        foreach (var hasTraversal in new[] { true, false })
        {
            var item = new TraversalReference
            {
                Unknown1 = 1,
                Flags = TraversalReference.Flag.Unknown0,
                Unknown = unknown,
            };
            if (hasTraversal) item.Traversal.SetTo(target);
            else item.Traversal.SetToNull();
            var bytes = WriteObject(w => item.WriteToBinary(w));
            bytes.Length.ShouldBe(hasTraversal ? 56 : 52);
            U32(bytes, 40).ShouldBe(hasTraversal ? 1u : 1u | 4u);
            if (hasTraversal) U32(bytes, 44).ShouldBe(target.ID);
            bytes.AsSpan(hasTraversal ? 48 : 44, 8).ToArray().ShouldBe(unknown);
            var read = TraversalReference.CreateFromBinary(Frame(bytes));
            read.Flags.ShouldBe(TraversalReference.Flag.Unknown0);
            read.Traversal.FormKeyNullable.ShouldBe(hasTraversal ? target : null);
            read.Unknown.ToArray().ShouldBe(unknown);
        }
    }

    [Fact]
    public void PathingCellIsTheParentCellOrTheGridStoredYThenX()
    {
        Attr("NavmeshPathingCell.ParentCell", "layoutPresentWhen").ShouldBe("field:ParentWorld:null");
        Attr("NavmeshPathingCell.Grid", "layoutPresentWhen").ShouldBe("field:ParentWorld:notnull");
        Attr("NavmeshPathingCell.Grid", "layoutEncoding").ShouldBe("int16:Y,X");
        Offset("NavmeshPathingCell.ParentCell").ShouldBe(8);
        Offset("NavmeshPathingCell.Grid").ShouldBe(8);

        const uint crc = 0xDEADBEEF;
        var world = new FormKey(TestModKey, 0x901);
        var parentCell = new FormKey(TestModKey, 0x902);
        foreach (var inWorld in new[] { false, true })
        {
            var cell = new NavmeshPathingCell { Crc = crc };
            if (inWorld)
            {
                cell.ParentWorld.SetTo(world);
                cell.Grid = new P2Int16(3, -4);
            }
            else
            {
                cell.ParentCell.SetTo(parentCell);
            }
            var info = new NavigationMapInfo { PathingCell = cell };
            var bytes = WriteObject(w => info.WriteToBinary(w));
            var at = IndexOf(bytes, crc);
            if (inWorld)
            {
                U32(bytes, at + 4).ShouldBe(world.ID);
                I16(bytes, at + Offset("NavmeshPathingCell.Grid")).ShouldBe((short)-4);
                I16(bytes, at + Offset("NavmeshPathingCell.Grid") + 2).ShouldBe((short)3);
            }
            else
            {
                U32(bytes, at + 4).ShouldBe(0u);
                U32(bytes, at + Offset("NavmeshPathingCell.ParentCell")).ShouldBe(parentCell.ID);
            }
            var read = NavigationMapInfo.CreateFromBinary(Frame(bytes)).PathingCell;
            read.Crc.ShouldBe(crc);
            if (inWorld)
            {
                read.ParentWorld.FormKey.ShouldBe(world);
                read.Grid.ShouldBe(new P2Int16(3, -4));
            }
            else
            {
                read.ParentWorld.IsNull.ShouldBeTrue();
                read.ParentCell.FormKey.ShouldBe(parentCell);
            }
        }
    }
}
