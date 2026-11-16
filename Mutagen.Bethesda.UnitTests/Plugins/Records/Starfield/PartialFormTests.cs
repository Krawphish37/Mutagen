using System.Buffers.Binary;
using System.IO.Abstractions;
using Loqui;
using Loqui.Internal;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Analysis;
using Mutagen.Bethesda.Plugins.Binary;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.HandReadLayoutFile;
using Noggog;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

/// <summary>
/// C58/W7: partial forms (header flag 0x4000) on every type that can be one (xEdit's list: Cell, DialogTopic, Location,
/// Quest, Worldspace), which fields a record's bytes held, and the merge the game makes of a partial form over the
/// version before it (SFSearch's PartialForms.cs)
/// </summary>
public class PartialFormTests
{
    private readonly ITestOutputHelper _output;

    public PartialFormTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TheTypesThatCanBePartialForms()
    {
        var types = typeof(StarfieldMod).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(IStarfieldMajorRecord).IsAssignableFrom(t) && t.IsClass && !t.Name.EndsWith("BinaryOverlay"))
            .Where(PartialFormMixIn.CanBePartialForm)
            .Select(t => t.Name)
            .OrderBy(x => x)
            .ToArray();
        // xEdit's definitions (wbDefinitionsSF1.pas): the record defs whose flags list 14, 'Partial Form'
        types.ShouldBe(new[] { "Cell", "DialogTopic", "Location", "Quest", "Worldspace" });
        PartialFormMixIn.CanBePartialForm(typeof(ILocationGetter)).ShouldBeTrue();
        PartialFormMixIn.CanBePartialForm(typeof(IWeaponGetter)).ShouldBeFalse();
    }

    /// <summary>
    /// A subrecord read into several fields of the record itself (a DataType marker) is reported as one field: no type
    /// that can be a partial form has one, so its present fields are exact
    /// </summary>
    [Fact]
    public void PartialFormTypesHaveNoFlattenedStructs()
    {
        foreach (var type in new[] { typeof(Cell), typeof(DialogTopic), typeof(Location), typeof(Quest), typeof(Worldspace) })
        {
            foreach (var name in Enum.GetNames(FieldIndexEnum(type)))
            {
                name.ShouldNotEndWith("DataTypeState", customMessage: type.Name);
            }
        }
    }

    [Fact]
    public void FlagOnAnotherType_IsNotAPartialForm()
    {
        var mod = new StarfieldMod(ModKey.FromFileName("Test.esm"), StarfieldRelease.Starfield);
        var weapon = mod.Weapons.AddNew("W");
        weapon.MajorRecordFlagsRaw = PartialFormMixIn.PartialFormFlag;
        weapon.IsPartialForm().ShouldBeFalse();
        var location = mod.Locations.AddNew("L");
        location.MajorRecordFlagsRaw = PartialFormMixIn.PartialFormFlag;
        location.IsPartialForm().ShouldBeTrue();
        // Made in code: which fields it holds isn't known, so it can't be merged
        location.TryGetPresentFields(out _).ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => location.MergeOnto(new Location(location.FormKey, StarfieldRelease.Starfield)));
        Should.Throw<ArgumentException>(() => location.MergeOnto(mod.Locations.AddNew("Other")));
    }

    [Theory, MutagenAutoData]
    public async Task Merge_LaysThePartialFormsFieldsOverTheBase(IFileSystem fileSystem, DirectoryPath existingTempDir)
    {
        var baseMod = new StarfieldMod(ModKey.FromFileName("Base.esm"), StarfieldRelease.Starfield);
        var keyword = baseMod.Keywords.AddNew("Kw");
        var location = baseMod.Locations.AddNew("BaseLocation");
        location.Name = "Base name";
        location.Keywords = new() { keyword.ToLink() };
        location.WorldLocationRadius = 5;
        var basePath = new ModPath(baseMod.ModKey, Path.Combine(existingTempDir, baseMod.ModKey.FileName));
        await baseMod.BeginWrite.ToPath(basePath).WithNoLoadOrder().NoModKeySync().WithFileSystem(fileSystem).WriteAsync();

        // The partial form: its EDID and a new name only
        var partialMod = new StarfieldMod(ModKey.FromFileName("Partial.esm"), StarfieldRelease.Starfield);
        var partial = new Location(location.FormKey, StarfieldRelease.Starfield)
        {
            EditorID = "PartialLocation",
            Name = "Partial name",
            MajorRecordFlagsRaw = PartialFormMixIn.PartialFormFlag,
        };
        partialMod.Locations.Add(partial);
        var partialPath = new ModPath(partialMod.ModKey, Path.Combine(existingTempDir, partialMod.ModKey.FileName));
        await partialMod.BeginWrite.ToPath(partialPath).WithLoadOrder(baseMod).NoModKeySync().WithFileSystem(fileSystem).WriteAsync();

        var known = new KeyedMasterStyle(baseMod.ModKey, MasterStyle.Full);
        var baseFull = StarfieldMod.Create(StarfieldRelease.Starfield).FromPath(basePath).WithNoLoadOrder().WithFileSystem(fileSystem).Construct();
        var partialFull = StarfieldMod.Create(StarfieldRelease.Starfield).FromPath(partialPath).WithKnownMasters(known).WithFileSystem(fileSystem).Construct();
        using var baseOverlay = StarfieldMod.CreateFromBinaryOverlay(basePath, StarfieldRelease.Starfield, new BinaryReadParameters { FileSystem = fileSystem });
        using var partialOverlay = StarfieldMod.CreateFromBinaryOverlay(partialPath, StarfieldRelease.Starfield,
            new BinaryReadParameters { FileSystem = fileSystem, MasterFlagsLookup = Lookup(known) });

        var partials = new ILocationGetter[] { partialFull.Locations.Single(), partialOverlay.Locations.Single() };
        var bases = new ILocationGetter[] { baseFull.Locations.Single(), baseOverlay.Locations.Single() };
        foreach (var p in partials)
        {
            p.IsPartialForm().ShouldBeTrue();
            p.TryGetPresentFields(out var held).ShouldBeTrue();
            held.ShouldContain((int)Location_FieldIndex.EditorID);
            held.ShouldContain((int)Location_FieldIndex.Name);
            held.ShouldNotContain((int)Location_FieldIndex.Keywords);
            held.ShouldNotContain((int)Location_FieldIndex.WorldLocationRadius);
            foreach (var b in bases)
            {
                b.IsPartialForm().ShouldBeFalse();
                b.TryGetPresentFields(out var baseHeld).ShouldBeTrue();
                baseHeld.ShouldContain((int)Location_FieldIndex.Keywords);
                var merged = p.MergeOnto(b).ShouldBeOfType<Location>();
                merged.EditorID.ShouldBe("PartialLocation");
                merged.Name!.String.ShouldBe("Partial name");
                merged.Keywords!.Select(k => k.FormKey).ShouldBe(new[] { keyword.FormKey });
                merged.WorldLocationRadius.ShouldBe(5);
                merged.MajorRecordFlagsRaw.ShouldBe(0);
                merged.IsPartialForm().ShouldBeFalse();
                merged.TryGetPresentFields(out var mergedHeld).ShouldBeTrue();
                mergedHeld.ShouldBe(held.Union(baseHeld).OrderBy(x => x));
                // The partial form and its base are left as they were
                p.EditorID.ShouldBe("PartialLocation");
                b.Name!.String.ShouldBe("Base name");
            }
        }

        // A record that isn't a partial form merges to a copy of itself
        var whole = bases[0].MergeOnto(bases[1]);
        whole.Equals(bases[0]).ShouldBeTrue();
    }

    private static Cache<IModMasterStyledGetter, ModKey> Lookup(params KeyedMasterStyle[] masters)
    {
        var ret = new Cache<IModMasterStyledGetter, ModKey>(x => x.ModKey);
        foreach (var m in masters) ret.Set(m);
        return ret;
    }

    /// <summary>
    /// Every partial form in the official plugins (header flag 0x4000), merged over the version before it in its plugin's
    /// masters: its type is one that can be a partial form, and the merged record has each field the partial form holds
    /// from it and each other field its base holds from the base. Locations and worldspaces are also read through the
    /// overlay, which gives the same present fields.
    /// </summary>
    [Fact]
    [Trait("Category", "RealData")]
    public void RealData_OfficialPlugins_EveryPartialFormMerges()
    {
        var data = Environment.GetEnvironmentVariable("MUTAGEN_STARFIELD_DATA");
        if (string.IsNullOrEmpty(data)) return; // needs the game's Data folder
        var constants = GameConstants.Starfield;
        var styles = new Cache<IModMasterStyledGetter, ModKey>(x => x.ModKey);
        var headers = new Dictionary<ModKey, ModHeaderFrame>();
        foreach (var name in HandReadLayoutFileTests.OfficialPlugins)
        {
            var modKey = ModKey.FromFileName(name);
            var header = ModHeaderFrame.FromPath(Path.Combine(data, name), GameRelease.Starfield);
            headers[modKey] = header;
            styles.Set(new KeyedMasterStyle(modKey, header.MasterStyle));
        }
        var param = new BinaryReadParameters { MasterFlagsLookup = styles };
        var readers = new Dictionary<ModKey, MajorRecordReader>();
        var locators = new Dictionary<ModKey, RecordLocatorResults>();
        MajorRecordReader ReaderOf(ModKey mod) => readers.TryGetValue(mod, out var r) ? r
            : readers[mod] = MajorRecordReader.FromPath(new ModPath(mod, Path.Combine(data, mod.FileName)), GameRelease.Starfield, param);
        RecordLocatorResults LocatorOf(ModKey mod)
        {
            if (locators.TryGetValue(mod, out var l)) return l;
            var masters = MasterReferenceCollection.FromModHeader(mod, headers[mod]).Masters
                .Select(m => new KeyedMasterStyle(m.Master, styles[m.Master].MasterStyle)).ToList();
            var package = SeparatedMasterPackage.FromMasterStyles(mod, headers[mod].MasterStyle, masters);
            return locators[mod] = RecordLocator.GetLocations(new ModPath(mod, Path.Combine(data, mod.FileName)), constants, package);
        }

        var census = new Dictionary<string, int>();
        int merged = 0, overlayChecked = 0;
        try
        {
            foreach (var name in HandReadLayoutFileTests.OfficialPlugins)
            {
                var modKey = ModKey.FromFileName(name);
                var bytes = File.ReadAllBytes(Path.Combine(data, name));
                IStarfieldModDisposableGetter? overlay = null;
                try
                {
                    foreach (var (position, signature, flags) in Records(bytes, constants))
                    {
                        if ((flags & PartialFormMixIn.PartialFormFlag) == 0) continue;
                        census[signature] = census.GetValueOrDefault(signature) + 1;
                        var partial = ReaderOf(modKey).ReadAt(position);
                        partial.IsPartialForm().ShouldBeTrue($"{name} {signature} {partial.FormKey}");
                        partial.TryGetPresentFields(out var held).ShouldBeTrue();
                        var baseVersion = Under(partial.FormKey, modKey, 0);
                        if (baseVersion == null) continue;
                        baseVersion.TryGetPresentFields(out var baseHeld).ShouldBeTrue();
                        var result = partial.MergeOnto(baseVersion);
                        var count = partial.Registration.FieldCount;
                        foreach (var index in held)
                        {
                            FieldEquals(result, partial, index, count).ShouldBeTrue($"{name} {partial.FormKey} field {FieldName(partial, index)} from the partial form");
                        }
                        foreach (var index in baseHeld.Except(held))
                        {
                            FieldEquals(result, baseVersion, index, count).ShouldBeTrue($"{name} {partial.FormKey} field {FieldName(partial, index)} from the base");
                        }
                        (result.MajorRecordFlagsRaw & PartialFormMixIn.PartialFormFlag).ShouldBe(0);
                        merged++;

                        // The overlay gives the same present fields
                        if (signature is "LCTN" or "WRLD")
                        {
                            overlay ??= StarfieldMod.CreateFromBinaryOverlay(new ModPath(modKey, Path.Combine(data, name)), StarfieldRelease.Starfield, param);
                            IMajorRecordGetter fromOverlay = signature == "LCTN" ? overlay.Locations[partial.FormKey] : overlay.Worldspaces[partial.FormKey];
                            fromOverlay.TryGetPresentFields(out var overlayHeld).ShouldBeTrue();
                            overlayHeld.ShouldBe(held, $"{name} {partial.FormKey}");
                            overlayChecked++;
                        }
                    }
                }
                finally
                {
                    overlay?.Dispose();
                }
            }
        }
        finally
        {
            foreach (var r in readers.Values) r.Dispose();
        }
        _output.WriteLine($"partial forms: {string.Join(", ", census.OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Value}"))}; {merged} merged, {overlayChecked} through the overlay too");
        merged.ShouldBeGreaterThan(0);
        foreach (var signature in census.Keys)
        {
            signature.ShouldBeOneOf("CELL", "DIAL", "LCTN", "QUST", "WRLD");
        }

        // The version a plugin's record sits on, as the game has it there: the last of its masters that holds it, merged
        // over its own base when it is a partial form itself
        IMajorRecordGetter? Under(FormKey formKey, ModKey plugin, int depth)
        {
            var masters = MasterReferenceCollection.FromModHeader(plugin, headers[plugin]).Masters.Select(m => m.Master).ToList();
            for (int i = masters.Count - 1; i >= 0; i--)
            {
                if (!headers.ContainsKey(masters[i])) continue;
                if (!LocatorOf(masters[i]).TryGetRecord(formKey, out var marker)) continue;
                var found = ReaderOf(masters[i]).ReadAt(marker.Location.Min);
                found.FormKey.ShouldBe(formKey);
                if (found.IsPartialForm() && depth < 16 && Under(formKey, masters[i], depth + 1) is { } deeper)
                {
                    return found.MergeOnto(deeper);
                }
                return found;
            }
            return null;
        }
    }

    // A record type's FieldIndex enum (Location_FieldIndex, ...): its field names (the registration has none)
    private static Type FieldIndexEnum(Type recordClass) =>
        recordClass.Assembly.GetType($"{recordClass.Namespace}.{recordClass.Name}_FieldIndex")!;

    private static string FieldName(IMajorRecordGetter record, int index) =>
        Enum.GetName(FieldIndexEnum(record.Registration.ClassType), index) ?? index.ToString();

    private static bool FieldEquals(IMajorRecordGetter lhs, IMajorRecordGetter rhs, int index, int count)
    {
        var mask = new (bool On, TranslationCrystal? SubCrystal)[count];
        mask[index] = (true, null);
        return ((MajorRecordCommon)lhs.CommonInstance()!).Equals(lhs, rhs, new TranslationCrystal(mask));
    }

    // Every record of a plugin: its position, signature and header flags (groups walked)
    private static IEnumerable<(long Position, string Signature, int Flags)> Records(byte[] bytes, GameConstants constants)
    {
        var pos = constants.MajorConstants.HeaderLength + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
        while (pos < bytes.Length)
        {
            var size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(pos + 4));
            if (bytes.AsSpan(pos, 4).SequenceEqual("GRUP"u8))
            {
                pos += constants.GroupConstants.HeaderLength;
                continue;
            }
            yield return (pos, System.Text.Encoding.ASCII.GetString(bytes, pos, 4), BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(pos + 8)));
            pos += constants.MajorConstants.HeaderLength + size;
        }
    }
}
