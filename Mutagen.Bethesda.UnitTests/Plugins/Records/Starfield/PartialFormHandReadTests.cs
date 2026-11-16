using System.Buffers.Binary;
using System.Text;
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
using Noggog;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

/// <summary>
/// C75 (SFSearch S169): the fields hand-written readers fill count as present (TryGetPresentFields), so MergeOnto takes
/// them from the version under a partial form, as the game does. Before, a quest's UnusedConditions and NextAliasID and a
/// cell's NumTraversalFluffBytes weren't reported, so a merge left them at their defaults.
/// </summary>
public class PartialFormHandReadTests
{
    private readonly ITestOutputHelper _output;

    public PartialFormHandReadTests(ITestOutputHelper output) => _output = output;

    private static byte[] Text(string s) => Encoding.ASCII.GetBytes(s + "\0");

    private static byte[] Ctda()
    {
        var b = new byte[32];
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(4), 1f);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(28), -1);
        return b;
    }

    private static string[] Present(IMajorRecordGetter record)
    {
        record.TryGetPresentFields(out var held).ShouldBeTrue();
        var fieldIndex = record.Registration.ClassType.Assembly.GetType($"{record.Registration.ClassType.Namespace}.{record.Registration.ClassType.Name}_FieldIndex")!;
        return held!.Select(i => Enum.GetName(fieldIndex, i)!).ToArray();
    }

    // A quest's dialogue conditions (a Custom field), its NEXT and unused conditions (a step) and its next alias ID and
    // aliases (the alias step)
    [Fact]
    public void Quest_HandReadFieldsArePresent()
    {
        var bytes = Record("QUST",
            Subrecord("EDID", Text("Q")),
            Subrecord("CTDA", Ctda()),
            Subrecord("NEXT", Array.Empty<byte>()),
            Subrecord("CTDA", Ctda()),
            Subrecord("ANAM", BitConverter.GetBytes(5u)));
        foreach (var quest in ReadBoth<IQuestGetter>(bytes, f => Quest.CreateFromBinary(f), (s, p) => QuestBinaryOverlay.QuestFactory(s, p)))
        {
            quest.NextAliasID.ShouldBe(5u);
            quest.UnusedConditions!.Count.ShouldBe(1);
            Present(quest).ShouldBe(new[] { "EditorID", "DialogConditions", "UnusedConditions", "NextAliasID", "Aliases" }, ignoreOrder: true);
        }
        var bare = Record("QUST", Subrecord("EDID", Text("Q")));
        foreach (var quest in ReadBoth<IQuestGetter>(bare, f => Quest.CreateFromBinary(f), (s, p) => QuestBinaryOverlay.QuestFactory(s, p)))
        {
            Present(quest).ShouldBe(new[] { "EditorID" });
        }
    }

    // A worldspace's object bounds (Custom fields inside their NAM0 / NAM9 data)
    [Fact]
    public void Worldspace_HandReadFieldsArePresent()
    {
        var bytes = Record("WRLD",
            Subrecord("EDID", Text("W")),
            Subrecord("NAM0", new byte[8]),
            Subrecord("NAM9", new byte[8]));
        foreach (var world in ReadBoth<IWorldspaceGetter>(bytes, f => Worldspace.CreateFromBinary(f), (s, p) => WorldspaceBinaryOverlay.WorldspaceFactory(s, p)))
        {
            Present(world).ShouldBe(new[] { "EditorID", "ObjectBoundsMin", "ObjectBoundsMax" }, ignoreOrder: true);
        }
    }

    // The game's merge over a version that holds those fields: they come from it
    [Fact]
    public void Quest_MergeTakesTheHandReadFieldsFromTheBase()
    {
        var full = Record("QUST",
            Subrecord("EDID", Text("Q")),
            Subrecord("CTDA", Ctda()),
            Subrecord("NEXT", Array.Empty<byte>()),
            Subrecord("CTDA", Ctda()),
            Subrecord("ANAM", BitConverter.GetBytes(5u)));
        var partial = Record("QUST", Subrecord("EDID", Text("Q")));
        BinaryPrimitives.WriteInt32LittleEndian(partial.AsSpan(8), PartialFormMixIn.PartialFormFlag);
        var bases = ReadBoth<IQuestGetter>(full, f => Quest.CreateFromBinary(f), (s, p) => QuestBinaryOverlay.QuestFactory(s, p)).ToArray();
        var partials = ReadBoth<IQuestGetter>(partial, f => Quest.CreateFromBinary(f), (s, p) => QuestBinaryOverlay.QuestFactory(s, p)).ToArray();
        foreach (var p in partials)
        {
            foreach (var b in bases)
            {
                var merged = p.MergeOnto(b).ShouldBeOfType<Quest>();
                merged.NextAliasID.ShouldBe(5u);
                merged.UnusedConditions!.Count.ShouldBe(1);
                merged.DialogConditions.Count.ShouldBe(1);
                ExceptHeader(merged, b).ShouldBeTrue();
            }
        }
    }

    // The fields two records differ in, by name
    private static IEnumerable<string> Differing(IMajorRecordGetter lhs, IMajorRecordGetter rhs)
    {
        var fieldIndex = lhs.Registration.ClassType.Assembly.GetType($"{lhs.Registration.ClassType.Namespace}.{lhs.Registration.ClassType.Name}_FieldIndex")!;
        for (int i = 0; i < lhs.Registration.FieldCount; i++)
        {
            var mask = new (bool On, TranslationCrystal? SubCrystal)[lhs.Registration.FieldCount];
            mask[i] = (true, null);
            if (!((MajorRecordCommon)lhs.CommonInstance()!).Equals(lhs, rhs, new TranslationCrystal(mask))) yield return Enum.GetName(fieldIndex, i) ?? i.ToString();
        }
    }

    // Equal in every field but the record header's (flags and their enum view, version control, form version, version 2)
    private static bool ExceptHeader(IMajorRecordGetter lhs, IMajorRecordGetter rhs)
    {
        var mask = new (bool On, TranslationCrystal? SubCrystal)[lhs.Registration.FieldCount];
        for (int i = 0; i < mask.Length; i++) mask[i] = (true, null);
        var fieldIndex = lhs.Registration.ClassType.Assembly.GetType($"{lhs.Registration.ClassType.Namespace}.{lhs.Registration.ClassType.Name}_FieldIndex")!;
        foreach (var header in new[] { "MajorRecordFlagsRaw", "StarfieldMajorRecordFlags", "VersionControl", "FormVersion", "Version2" })
        {
            mask[Convert.ToInt32(Enum.Parse(fieldIndex, header))] = (false, null);
        }
        return ((MajorRecordCommon)lhs.CommonInstance()!).Equals(lhs, rhs, new TranslationCrystal(mask));
    }

    /// <summary>
    /// Aurie_UpgradedCompanions.esm's 13 quest partial forms (each holds only its EDID) and SFBGS00D.esm's
    /// CityNewAtlantisLodgeInt (06BB29:Starfield.esm, its EDID only) merge to the version under them: identical to it apart
    /// from the record header (SFSearch's own merge finds the quests ITMs). The cell's hand-read NumTraversalFluffBytes is
    /// present in its base.
    /// </summary>
    [Fact]
    [Trait("Category", "RealData")]
    public void RealData_EdidOnlyPartialForms_MergeToTheirBase()
    {
        var data = Environment.GetEnvironmentVariable("MUTAGEN_STARFIELD_DATA");
        if (string.IsNullOrEmpty(data)) return; // needs the game's Data folder
        var styles = new Cache<IModMasterStyledGetter, ModKey>(x => x.ModKey);
        foreach (var f in Directory.EnumerateFiles(data, "*.esm"))
        {
            try
            {
                styles.Set(new KeyedMasterStyle(ModKey.FromFileName(Path.GetFileName(f)), ModHeaderFrame.FromPath(f, GameRelease.Starfield).MasterStyle));
            }
            catch (Exception e) when (e is IOException or ArgumentException or InvalidDataException)
            {
                // not a plugin Mutagen reads a header from: not a master of these
            }
        }
        var param = new BinaryReadParameters { MasterFlagsLookup = styles };
        var readers = new Dictionary<ModKey, MajorRecordReader>();
        var locators = new Dictionary<ModKey, RecordLocatorResults>();
        MajorRecordReader ReaderOf(ModKey mod) => readers.TryGetValue(mod, out var r) ? r
            : readers[mod] = MajorRecordReader.FromPath(new ModPath(mod, Path.Combine(data, mod.FileName)), GameRelease.Starfield, param);
        RecordLocatorResults LocatorOf(ModKey mod)
        {
            if (locators.TryGetValue(mod, out var l)) return l;
            var path = new ModPath(mod, Path.Combine(data, mod.FileName));
            var header = ModHeaderFrame.FromPath(path, GameRelease.Starfield);
            var masters = MasterReferenceCollection.FromModHeader(mod, header).Masters
                .Select(m => new KeyedMasterStyle(m.Master, styles[m.Master].MasterStyle)).ToList();
            return locators[mod] = RecordLocator.GetLocations(path, GameConstants.Starfield, SeparatedMasterPackage.FromMasterStyles(mod, header.MasterStyle, masters));
        }
        IMajorRecordGetter BaseOf(FormKey formKey, ModKey plugin)
        {
            var header = ModHeaderFrame.FromPath(Path.Combine(data, plugin.FileName), GameRelease.Starfield);
            var masters = MasterReferenceCollection.FromModHeader(plugin, header).Masters.Select(m => m.Master).ToList();
            for (int i = masters.Count - 1; i >= 0; i--)
            {
                if (LocatorOf(masters[i]).TryGetRecord(formKey, out var marker)) return ReaderOf(masters[i]).ReadAt(marker.Location.Min);
            }
            throw new InvalidOperationException($"{formKey} is in none of {plugin}'s masters");
        }

        try
        {
            var quests = 0;
            var aurie = ModKey.FromFileName("Aurie_UpgradedCompanions.esm");
            foreach (var marker in LocatorOf(aurie).ListedRecords.Values.Where(m => m.Record.Type == "QUST"))
            {
                var partial = ReaderOf(aurie).ReadAt(marker.Location.Min);
                if (!partial.IsPartialForm()) continue;
                quests++;
                var under = BaseOf(partial.FormKey, aurie);
                var merged = partial.MergeOnto(under);
                ExceptHeader(merged, under).ShouldBeTrue($"{partial.FormKey} {partial.EditorID}: {string.Join(", ", Differing(merged, under))}");
            }
            quests.ShouldBe(13);

            var sfbgs00d = ModKey.FromFileName("SFBGS00D.esm");
            var lodge = FormKey.Factory("06BB29:Starfield.esm");
            LocatorOf(sfbgs00d).TryGetRecord(lodge, out var lodgeAt).ShouldBeTrue();
            var cellPartial = ReaderOf(sfbgs00d).ReadAt(lodgeAt!.Location.Min);
            cellPartial.IsPartialForm().ShouldBeTrue();
            var cellBase = BaseOf(lodge, sfbgs00d);
            Present(cellBase).ShouldContain("NumTraversalFluffBytes");
            Present(cellBase).ShouldContain("Traversals");
            var cellMerged = cellPartial.MergeOnto(cellBase).ShouldBeOfType<Cell>();
            ExceptHeader(cellMerged, cellBase).ShouldBeTrue(string.Join(", ", Differing(cellMerged, cellBase)));
            cellMerged.NumTraversalFluffBytes.ShouldBe(((ICellGetter)cellBase).NumTraversalFluffBytes);
            _output.WriteLine($"{quests} quests merged to their base; the lodge cell too (NumTraversalFluffBytes {cellMerged.NumTraversalFluffBytes}, {cellMerged.Traversals?.Count} traversals)");
        }
        finally
        {
            foreach (var r in readers.Values) r.Dispose();
        }
    }
}
