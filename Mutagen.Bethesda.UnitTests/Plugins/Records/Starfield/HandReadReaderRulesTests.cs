using System.Xml.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.HandReadLayoutFile;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C57: rules of hand-written readers that Mutagen's shipped files now carry (the rest are in HandReadLayoutAttributesTests,
// HandReadSelectorAttributesTests and the layout file's NPC_ ONA2 entry, checked by HandReadLayoutSyntheticTests and the
// real-data run):
//   - Layouts/HandReadLayouts.xml's class= on a dialogue response's script fragments: the reader always makes a plain
//     ScriptFragments, though SceneScriptFragments derives from it;
//   - a gendered value with genderEnumRecord (ArmorAddon.BoneDataModifiers after BSMP), as docs/Contributing/development
//     /Code-Generation.md describes it ("Gendered values in the binary").
public class HandReadReaderRulesTests
{
    private static XElement LayoutRoot() => XDocument.Load(HandReadLayoutFileTests.LayoutPath()).Root!;

    private static DialogResponses Info(ScriptFragments fragments) => new(new FormKey(TestModKey, 0x812), StarfieldRelease.Starfield)
    {
        VirtualMachineAdapter = new DialogResponsesAdapter { ObjectFormat = 2, ScriptFragments = fragments },
    };

    private static ScriptFragments Fragments(ScriptFragments f)
    {
        f.Script = new ScriptEntry { Name = "LayoutScript" };
        f.OnBegin = new ScriptFragment { ScriptName = "LayoutScript", FragmentName = "Fragment_Begin" };
        return f;
    }

    [Fact]
    public void DialogResponsesScriptFragments_ClassIsTheOneTheReaderMakes()
    {
        var vmad = LayoutRoot().Elements("AnyRecord").Single(a => (string?)a.Attribute("sig") == "VMAD");
        var object_ = vmad.Descendants("Case").Single(c => (string?)c.Attribute("type") == "DialogResponsesAdapter")
            .Descendants("Object").Single(o => (string?)o.Attribute("field") == "ScriptFragments");
        ((string?)object_.Attribute("class")).ShouldBe(nameof(ScriptFragments));
        typeof(SceneScriptFragments).IsSubclassOf(typeof(ScriptFragments)).ShouldBeTrue();

        // what the reader makes: a plain ScriptFragments, with both readers
        IDialogResponsesGetter plain = Info(Fragments(new ScriptFragments()));
        var bytes = Write(w => plain.WriteToBinary(w));
        foreach (var info in ReadBoth<IDialogResponsesGetter>(bytes, f => DialogResponses.CreateFromBinary(f), (s, p) => DialogResponsesBinaryOverlay.DialogResponsesFactory(s, p)))
        {
            info.VirtualMachineAdapter!.ScriptFragments!.GetType().Name.Replace("BinaryOverlay", "").ShouldBe(nameof(ScriptFragments));
        }

        // the checker holds Mutagen's object to it: the class read back passes, another class fails
        var subs = HandReadLayoutFileTests.Subrecords(bytes[24..]);
        var read = DialogResponses.CreateFromBinary(new Mutagen.Bethesda.Plugins.Binary.Streams.MutagenFrame(
            new Mutagen.Bethesda.Plugins.Binary.Streams.MutagenMemoryReadStream(bytes, Meta())));
        var good = new LayoutChecker(Meta().MasterReferences, localized: false);
        good.CheckRecord(vmad, subs, read);
        good.Errors.ShouldBeEmpty();
        var other = new LayoutChecker(Meta().MasterReferences, localized: false);
        other.CheckRecord(vmad, subs, Info(Fragments(new SceneScriptFragments())));
        other.Errors.ShouldHaveSingleItem().ShouldContain("Mutagen made SceneScriptFragments");
    }

    // ArmorAddon.BoneDataModifiers: genderEnumRecord="BSMP", items BNAM
    private const string Bsmp0 = "42534D50040000000000", Bsmp1 = "42534D50040001000000";
    private static string Bnam(uint id) => "424E414D0400" + Convert.ToHexString(BitConverter.GetBytes(id));

    private static IEnumerable<IArmorAddonGetter> ReadArma(string subrecordsHex) =>
        ReadBoth<IArmorAddonGetter>(Record("ARMA", Hex(subrecordsHex)), f => ArmorAddon.CreateFromBinary(f), (s, p) => ArmorAddonBinaryOverlay.ArmorAddonFactory(s, p));

    private static uint[] Ids(IReadOnlyList<IFormLinkGetter<IBoneModifierGetter>> list) => list.Select(l => l.FormKey.ID).ToArray();

    [Fact]
    public void GenderEnumRecord_HalvesInAnyOrder_EachFollowedByItsItems()
    {
        var arma = XDocument.Load(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(HandReadLayoutFileTests.LayoutPath()))!, "Records", "Major Records", "ArmorAddon.xml"));
        arma.Descendants().Single(e => e.Name.LocalName == "Gendered" && (string?)e.Attribute("name") == "BoneDataModifiers")
            .Attribute("genderEnumRecord")!.Value.ShouldBe("BSMP");

        // female first, then male: each BSMP's int32 says which half the BNAM run after it is
        foreach (var a in ReadArma(Bsmp1 + Bnam(0x801) + Bsmp0 + Bnam(0x802) + Bnam(0x803)))
        {
            Ids(a.BoneDataModifiers.Female).ShouldBe([0x801u]);
            Ids(a.BoneDataModifiers.Male).ShouldBe([0x802u, 0x803u]);
            // written male first, both halves, each after its BSMP
            var written = GameConstants.Starfield.MajorRecord(Write(w => a.WriteToBinary(w)));
            var sigs = written.Select(s => s.RecordType.Type).Where(s => s is "BSMP" or "BNAM").ToArray();
            sigs.ShouldBe(["BSMP", "BNAM", "BNAM", "BSMP", "BNAM"]);
        }
        // one half only (kgcdoom.esm's 00B9A5 ends with a female BSMP and no items): the other half is empty
        foreach (var a in ReadArma(Bsmp1))
        {
            a.BoneDataModifiers.Male.ShouldBeEmpty();
            a.BoneDataModifiers.Female.ShouldBeEmpty();
        }
        foreach (var a in ReadArma(Bsmp0 + Bnam(0x804)))
        {
            Ids(a.BoneDataModifiers.Male).ShouldBe([0x804u]);
            a.BoneDataModifiers.Female.ShouldBeEmpty();
        }
    }
}
