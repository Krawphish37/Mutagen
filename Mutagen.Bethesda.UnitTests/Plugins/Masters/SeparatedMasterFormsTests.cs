using System.Buffers.Binary;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.HandReadLayoutFile;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Mutagen.Bethesda.UnitTests.Plugins.Masters;

/// <summary>
/// C58/W8: the FormID forms of Starfield's separated master numbering, public on SeparatedMasterPackage (Classify,
/// IsOwnFormVariant, OwnHeaderStyle, OwnIndex, EnumerateStoredForms, FromMasterStyles), as SFSearch's FormIdResolver,
/// FormIdForms and ByteSearch.FormIdPattern use them
/// </summary>
public class SeparatedMasterFormsTests
{
    private readonly ITestOutputHelper _output;

    public SeparatedMasterFormsTests(ITestOutputHelper output) => _output = output;

    private static readonly ModKey Self = ModKey.FromFileName("Self.esm");
    private static readonly ModKey FullA = ModKey.FromFileName("FullA.esm");
    private static readonly ModKey FullB = ModKey.FromFileName("FullB.esm");
    private static readonly ModKey SmallA = ModKey.FromFileName("SmallA.esm");
    private static readonly ModKey MediumA = ModKey.FromFileName("MediumA.esm");

    private static SeparatedMasterPackage Package(MasterStyle own) => SeparatedMasterPackage.FromMasterStyles(Self, own,
    [
        new KeyedMasterStyle(FullA, MasterStyle.Full),
        new KeyedMasterStyle(SmallA, MasterStyle.Small),
        new KeyedMasterStyle(MediumA, MasterStyle.Medium),
        new KeyedMasterStyle(FullB, MasterStyle.Full),
    ]);

    [Theory]
    [InlineData(MasterStyle.Full)]
    [InlineData(MasterStyle.Small)]
    [InlineData(MasterStyle.Medium)]
    public void FromMasterStyles_NumbersEachSizeSeparately(MasterStyle own)
    {
        var package = Package(own);
        package.OwnHeaderStyle.ShouldBe(own);
        package.OwnIndex(MasterStyle.Full).ShouldBe(2u);
        package.OwnIndex(MasterStyle.Small).ShouldBe(1u);
        package.OwnIndex(MasterStyle.Medium).ShouldBe(1u);
        package.GetFormKey(new FormID(0x0100_0800), reference: true).ShouldBe(new FormKey(FullB, 0x800));
        package.GetFormKey(new FormID(0xFE00_0800), reference: true).ShouldBe(new FormKey(SmallA, 0x800));
        package.GetFormKey(new FormID(0xFD00_0800), reference: true).ShouldBe(new FormKey(MediumA, 0x800));
        package.GetFormID(new FormKey(Self, 0x801)).Raw.ShouldBe(own switch
        {
            MasterStyle.Small => 0xFE00_1801u,
            MasterStyle.Medium => 0xFD01_0801u,
            _ => 0x0200_0801u,
        });
    }

    [Fact]
    public void FromMasterStyles_DuplicateOrMissingStyleIsRefused()
    {
        // Every master's size is given here, so nothing can be missing; a master listed twice is the caller's error
        Should.Throw<ArgumentException>(() => SeparatedMasterPackage.FromMasterStyles(Self, MasterStyle.Full,
        [
            new KeyedMasterStyle(FullA, MasterStyle.Full),
            new KeyedMasterStyle(FullA, MasterStyle.Small),
        ]));
    }

    [Theory]
    [InlineData(MasterStyle.Full, 0x0200_0801u, StoredFormIdForm.Full, true)]
    [InlineData(MasterStyle.Full, 0x0300_0801u, StoredFormIdForm.PastTheList, true)]
    [InlineData(MasterStyle.Full, 0x0100_0801u, StoredFormIdForm.Full, false)]
    [InlineData(MasterStyle.Full, 0xFE00_1801u, StoredFormIdForm.PastTheList, true)]
    [InlineData(MasterStyle.Small, 0xFE00_1801u, StoredFormIdForm.Small, true)]
    [InlineData(MasterStyle.Small, 0xFE00_2801u, StoredFormIdForm.PastTheList, true)]
    [InlineData(MasterStyle.Small, 0xFE00_0801u, StoredFormIdForm.Small, false)]
    [InlineData(MasterStyle.Small, 0xFD01_0801u, StoredFormIdForm.PastTheList, true)]
    [InlineData(MasterStyle.Medium, 0xFD01_0801u, StoredFormIdForm.Medium, true)]
    [InlineData(MasterStyle.Medium, 0xFD00_0801u, StoredFormIdForm.Medium, false)]
    public void Classify(MasterStyle own, uint raw, StoredFormIdForm form, bool isOwn)
    {
        Package(own).Classify(raw, out var gotOwn).ShouldBe(form);
        gotOwn.ShouldBe(isOwn);
    }

    public static TheoryData<MasterStyle, uint> OwnIds()
    {
        var ret = new TheoryData<MasterStyle, uint>();
        foreach (var own in new[] { MasterStyle.Full, MasterStyle.Small, MasterStyle.Medium })
        {
            foreach (var id in new uint[] { 0x1, 0x800, 0xFFF, 0x1000, 0xFFFF, 0x1_0000, 0xFF_FFFF })
            {
                ret.Add(own, id);
            }
        }
        return ret;
    }

    /// <summary>
    /// EnumerateStoredForms gives exactly the stored values that read back as the FormKey: checked against GetFormKey
    /// over every value with that local ID in each numbering
    /// </summary>
    [Theory]
    [MemberData(nameof(OwnIds))]
    public void EnumerateStoredForms_IsExactlyWhatReadsAsTheKey(MasterStyle own, uint id)
    {
        var package = Package(own);
        foreach (var key in new[] { new FormKey(Self, id), new FormKey(FullB, id), new FormKey(SmallA, id), new FormKey(MediumA, id) })
        {
            var all = package.EnumerateStoredForms(key, includePastList: true).Select(f => f.Raw).ToList();
            all.Distinct().Count().ShouldBe(all.Count);
            var expected = Candidates(id).Where(raw => package.GetFormKey(new FormID(raw), reference: true) == key).ToHashSet();
            all.ToHashSet().SetEquals(expected).ShouldBeTrue($"{own} {key}: {all.Count} given, {expected.Count} read as it");

            // Without the past-the-list forms: those Classify doesn't call past the list
            var places = package.EnumerateStoredForms(key).Select(f => f.Raw).ToHashSet();
            places.SetEquals(all.Where(raw => package.Classify(raw, out _) != StoredFormIdForm.PastTheList)).ShouldBeTrue();

            if (key.ModKey == Self)
            {
                // The written form first; every other form of its own is a variant
                var written = package.GetFormID(key).Raw;
                all[0].ShouldBe(written);
                foreach (var raw in all)
                {
                    package.Classify(raw, out var isOwn);
                    isOwn.ShouldBeTrue();
                    package.IsOwnFormVariant(raw, includePastList: true).ShouldBe(raw != written);
                }
            }
            else
            {
                all.Count.ShouldBeLessThanOrEqualTo(1);
            }
        }
    }

    [Fact]
    public void EnumerateStoredForms_NullAndForeignKeys()
    {
        var package = Package(MasterStyle.Small);
        package.EnumerateStoredForms(FormKey.Null).ShouldBe([FormID.Null]);
        package.EnumerateStoredForms(new FormKey(ModKey.FromFileName("NotAMaster.esm"), 0x800)).ShouldBeEmpty();
        // A Small master's record whose ID is past its numbering's 3 hex digits can't be stored
        package.EnumerateStoredForms(new FormKey(SmallA, 0x1000)).ShouldBeEmpty();
    }

    private static IEnumerable<uint> Candidates(uint id)
    {
        if (id <= 0xFF_FFFF)
        {
            for (uint top = 0; top < 0xFD; top++) yield return top << 24 | id;
        }
        if (id <= 0xFFFF)
        {
            for (uint index = 0; index <= 0xFF; index++) yield return 0xFD00_0000 | index << 16 | id;
        }
        if (id <= 0xFFF)
        {
            for (uint index = 0; index <= 0xFFF; index++) yield return 0xFE00_0000 | index << 12 | id;
        }
    }

    /// <summary>
    /// Every record header of the official plugins, as SFSearch's header pass resolves it: its owner (a master or the
    /// plugin), whether it is the plugin's own and in which form; each read the same through the public API as Mutagen
    /// reads the record, and each stored value is one of the forms EnumerateStoredForms gives for its FormKey
    /// </summary>
    [Fact]
    [Trait("Category", "RealData")]
    public void RealData_OfficialPlugins_EveryHeaderFormId()
    {
        var data = Environment.GetEnvironmentVariable("MUTAGEN_STARFIELD_DATA");
        if (string.IsNullOrEmpty(data)) return; // needs the game's Data folder
        var constants = GameConstants.Starfield;
        var styles = new Dictionary<ModKey, MasterStyle>();
        MasterStyle StyleOf(ModKey mod)
        {
            if (styles.TryGetValue(mod, out var s)) return s;
            return styles[mod] = ModHeaderFrame.FromPath(Path.Combine(data, mod.FileName), GameRelease.Starfield).MasterStyle;
        }
        int records = 0, own = 0, variants = 0, pastTheList = 0;
        foreach (var name in HandReadLayoutFileTests.OfficialPlugins)
        {
            var path = Path.Combine(data, name);
            var modKey = ModKey.FromFileName(name);
            var header = ModHeaderFrame.FromPath(path, GameRelease.Starfield);
            var masters = MasterReferenceCollection.FromModHeader(modKey, header).Masters
                .Select(m => new KeyedMasterStyle(m.Master, StyleOf(m.Master))).ToList();
            var package = SeparatedMasterPackage.FromMasterStyles(modKey, header.MasterStyle, masters);
            var bytes = File.ReadAllBytes(path);
            foreach (var raw in HeaderFormIds(bytes, constants))
            {
                records++;
                var key = package.GetFormKey(new FormID(raw), reference: true);
                var form = package.Classify(raw, out var isOwn);
                isOwn.ShouldBe(key.ModKey == modKey, $"{name} {raw:X8}");
                package.EnumerateStoredForms(key, includePastList: true).Select(f => f.Raw).ShouldContain(raw, $"{name} {raw:X8}");
                if (isOwn) own++;
                if (form == StoredFormIdForm.PastTheList) pastTheList++;
                if (package.IsOwnFormVariant(raw)) variants++;
            }
        }
        _output.WriteLine($"{records} record headers: {own} own, {variants} own in a form other than the one written, {pastTheList} past the list");
        records.ShouldBeGreaterThan(0);
        own.ShouldBeGreaterThan(0);
    }

    // The FormID of every record header in a plugin's bytes (groups walked, compressed records not opened)
    private static IEnumerable<uint> HeaderFormIds(byte[] bytes, GameConstants constants)
    {
        var pos = constants.MajorConstants.HeaderLength + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
        var stack = new Stack<int>();
        stack.Push(bytes.Length);
        while (pos < bytes.Length)
        {
            while (pos >= stack.Peek()) stack.Pop();
            var size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(pos + 4));
            if (bytes.AsSpan(pos, 4).SequenceEqual("GRUP"u8))
            {
                stack.Push(pos + size);
                pos += constants.GroupConstants.HeaderLength;
                continue;
            }
            yield return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 12));
            pos += constants.MajorConstants.HeaderLength + size;
        }
    }
}
