using System.Xml.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C57 (W15): the engine's own names for what a Mutagen object, field or enum is, in the record xml, so a tool can key
// the engine's reflection data (Starfield.exe 1.16.244's BSReflection, C30; CreationKit.exe 1.16.244's registry, C219)
// without a table of its own:
//   engineClass="BGSAtmosphere" on an object: the engine's reflected class it is, or (where it holds a Reflection) the
//               root class of that reflection's value;
//   engineField="pConditionForm" on a field: its name in its object's engineClass;
//   engineEnum="BGSBodyPartDefs::LIMB_ENUM" on an Enum field: the engine's enum whose values its enumName has.
// The names are the engine's, as the dumps have them. Held here against the xml and the code; the root classes against
// Starfield.esm (RealData).
public class EngineReflectionNameTests
{
    private static readonly XNamespace Loqui = "http://tempuri.org/LoquiSource.xsd";

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

    private static List<XElement> AllObjects() =>
        Directory.EnumerateFiles(RecordsDir(), "*.xml", SearchOption.AllDirectories)
            .SelectMany(f => XDocument.Load(f).Root!.Elements(Loqui + "Object")).ToList();

    private static readonly Dictionary<string, string> Classes = new()
    {
        ["Atmosphere"] = "BGSAtmosphere",
        ["Challenge"] = "BGSChallengeForm",
        ["Clouds"] = "BGSCloudForm",
        ["FogVolume"] = "BGSFogVolumeForm",
        ["ForceData"] = "BGSForceData",
        ["FormFolderKeywordList"] = "BGSFormFolderKeywordList",
        ["ImageSpace"] = "TESImageSpace",
        ["ImageSpaceAdapter"] = "TESImageSpaceModifier",
        ["LayeredMaterialSwap"] = "BGSLayeredMaterialSwap",
        ["MaterialPath"] = "BGSMaterialPathForm",
        ["ParticleSystemDefineCollection"] = "BGSParticleSystemDefineCollection",
        ["SunPreset"] = "BSGalaxy::BGSSunPresetForm",
        ["WeatherSetting"] = "BGSWeatherSettingsForm",
        ["VolumetricLighting"] = "BGSVolumetricLighting", // the reflection root (C57: not its Settings field's class, BGSVolumetricLightingSettings)
        ["EffectSequenceComponent"] = "BGSEffectSequenceFormComponent",
        ["LodOwnerComponent"] = "BGSLodOwnerComponent",
        ["MaterialPropertyComponent"] = "BGSMaterialPropertyComponent",
        // laid out as the engine's struct (no reflection of their own)
        ["SoundReference"] = "BGSAudio::WwiseSoundHook",
        ["PlacedObjectSpline"] = "ExtraBendableSplineParams::ParamData",
    };

    private static readonly Dictionary<string, string> Fields = new()
    {
        ["SoundReference.StartEvent"] = "StartEvent",
        ["SoundReference.StopEvent"] = "StopEvent",
        ["SoundReference.ConditionForm"] = "pConditionForm",
        ["SoundReference.EventForm"] = "pEventForm",
        ["PlacedObjectSpline.SplineRadius"] = "fThickness",
    };

    // enumName -> engineEnum, on every Enum field with that enumName
    private static readonly Dictionary<string, string> Enums = new()
    {
        ["DamageCauseType"] = "DamageCauseType",
        ["BodyPart.PartType"] = "BGSBodyPartDefs::LIMB_ENUM",
    };

    [Fact]
    public void Xml_GivesTheEngineNames()
    {
        var objects = AllObjects();
        objects.Where(o => o.Attribute("engineClass") != null)
            .ToDictionary(o => (string)o.Attribute("name")!, o => (string)o.Attribute("engineClass")!)
            .ShouldBe(Classes, ignoreOrder: true);

        var fields = new Dictionary<string, string>();
        var enums = new List<(string EnumName, string? Engine)>();
        foreach (var o in objects)
        {
            foreach (var f in o.Element(Loqui + "Fields")?.Descendants() ?? Enumerable.Empty<XElement>())
            {
                if (f.Attribute("engineField") is { } ef)
                {
                    // the object names the engine's class the field belongs to
                    o.Attribute("engineClass").ShouldNotBeNull($"{o.Attribute("name")}.{f.Attribute("name")}: engineField without the object's engineClass");
                    fields.Add($"{o.Attribute("name")!.Value}.{f.Attribute("name")!.Value}", ef.Value);
                }
                if (f.Attribute("enumName") is { } en && Enums.ContainsKey(en.Value)) enums.Add((en.Value, (string?)f.Attribute("engineEnum")));
                else f.Attribute("engineEnum").ShouldBeNull($"{o.Attribute("name")}.{f.Attribute("name")}: engineEnum on an enum this test doesn't know");
            }
        }
        fields.ShouldBe(Fields, ignoreOrder: true);
        enums.Select(e => e.EnumName).Distinct().ShouldBe(Enums.Keys, ignoreOrder: true);
        foreach (var (enumName, engine) in enums) engine.ShouldBe(Enums[enumName], enumName);
    }

    // The engine's values (C218, C30): DamageCauseType Vehicle_Impact 12, Other 13; LIMB_ENUM LIMB_TORSO 0, LIMB_HEAD_1 1,
    // LIMB_EYE_1 2, LIMB_LOOKAT_1 3, LIMB_FLY_GRAB 4 (its LIMB_NONE, -1, has no member here)
    [Fact]
    public void EngineEnums_HaveTheEngineValues()
    {
        ((int)DamageCauseType.VehicleImpact).ShouldBe(12);
        ((int)DamageCauseType.Other).ShouldBe(13);
        Enum.GetValues<BodyPart.PartType>().Select(v => (int)v).Take(5).ShouldBe([0, 1, 2, 3, 4]);
        Enum.GetValues<BodyPart.PartType>().ShouldNotContain(v => (int)v < 0);
    }

    // RealData: the root class of each reflection the objects with an engineClass hold, in Starfield.esm, is that class
    [Fact]
    [Trait("Category", "RealData")]
    public void RealData_ReflectionRootClass_IsTheEngineClass()
    {
        var data = Environment.GetEnvironmentVariable("MUTAGEN_STARFIELD_DATA");
        if (string.IsNullOrEmpty(data)) return; // needs the game's Data folder
        var types = Environment.GetEnvironmentVariable("MUTAGEN_ENGINE_TYPES") is { Length: > 0 } list
            ? list.Split(',')
            : ["ATMO", "CHAL", "CLDF", "FOGV", "FORC", "FFKW", "IMGS", "IMAD", "LMSW", "MTPT", "PSDC", "SUNP", "WTHS", "VOLI", "IPCT", "ACTI", "STAT", "MSTT"];
        using var mod = StarfieldMod.CreateFromBinaryOverlay(Path.Combine(data, "Starfield.esm"), StarfieldRelease.Starfield);
        var registrations = typeof(StarfieldMod).Assembly.GetTypes().Where(t => t.Name.EndsWith("_Registration"))
            .Select(t => (Sig: t.GetField("TriggeringRecordType")?.GetValue(null) as RecordType?, Name: t.Name[..^"_Registration".Length]))
            .Where(x => x.Sig != null).ToList();
        var met = new Dictionary<string, long>();
        var wrong = new List<string>();
        void Check(object holder, IReflectionGetter? reflection, string where)
        {
            if (reflection == null) return;
            var name = holder.GetType().Name.Replace("BinaryOverlay", "");
            if (!Classes.TryGetValue(name, out var engine)) return;
            var root = reflection.Parse()?.Objects.OfType<ReflectionObject>().FirstOrDefault()?.Class.Name ?? "(not decoded)";
            met[name] = met.GetValueOrDefault(name) + 1;
            if (root != engine && wrong.Count < 40) wrong.Add($"{where} {name}: root class {root}, the xml says {engine}");
        }
        foreach (var sig in types)
        {
            var (_, name) = registrations.First(r => r.Sig!.Value.Type == sig && typeof(IStarfieldMajorRecordGetter).IsAssignableFrom(typeof(StarfieldMod).Assembly.GetType($"Mutagen.Bethesda.Starfield.{r.Name}")));
            var getter = typeof(StarfieldMod).Assembly.GetType($"Mutagen.Bethesda.Starfield.I{name}Getter")!;
            foreach (var rec in mod.EnumerateMajorRecords(getter).Cast<IStarfieldMajorRecordGetter>())
            {
                var where = $"{sig} {rec.FormKey}";
                foreach (var prop in new[] { "Reflection", "ReflectionDiff" })
                {
                    if (rec.GetType().GetProperty(prop)?.GetValue(rec) is IReflectionGetter r) Check(rec, r, where);
                }
                if (rec.GetType().GetProperty("Components")?.GetValue(rec) is IEnumerable<IAComponentGetter> components)
                {
                    foreach (var c in components)
                    {
                        Check(c, c.GetType().GetProperty("Reflection")?.GetValue(c) as IReflectionGetter, where);
                    }
                }
            }
        }
        wrong.ShouldBeEmpty(string.Join(Environment.NewLine, wrong));
        // every engineClass that names a reflection's root is met at least once
        var holders = Classes.Keys.Where(k => k is not ("SoundReference" or "PlacedObjectSpline" or "Challenge")).ToList();
        holders.Where(h => !met.ContainsKey(h)).ShouldBeEmpty($"met: {string.Join(", ", met.Select(m => $"{m.Key} {m.Value}"))}");
    }
}
