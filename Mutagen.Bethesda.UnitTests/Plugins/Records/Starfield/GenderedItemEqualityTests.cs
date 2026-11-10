using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// M5 (overlay vs full import over all of Starfield.esm) found every ARMO (1,017), ARMA (1,106) and RACE (31) record
// unequal between the two readers although every field matched: the generated record Equals compares gendered fields
// with object.Equals, and GenderedItem / GenderedItemBinaryOverlay had no Equals, so two reads of the same record (or a
// record and its copy) were equal only when their gendered fields were the same object. Gendered items now compare by
// their male and female items, across implementations.
public class GenderedItemEqualityTests
{
    // AA_EmptyArmorAddon (1FFA12:Starfield.esm)
    private const string EmptyAddon = "45444944130041415F456D70747941726D6F724164646F6E00424F363408000010000000000000524E414D040029560000444E414D0D00000000000000000000000000004D4F44321800456666656374735C6678656D7074796E6F64652E6E696600464C4C44040001000000464C4C44040001000000464C4C44040001000000464C4C44040001000000464C4C44040001000000464C4C440400010000004D4F444C0400F03001004D4E414D040000000000544E414D010000534E414D010000564E414D01000042534D5004000000000042534D50040001000000";

    [Fact]
    public void GenderedItems_EqualByContent()
    {
        new GenderedItem<int>(1, 2).Equals(new GenderedItem<int>(1, 2)).ShouldBeTrue();
        new GenderedItem<int>(1, 2).GetHashCode().ShouldBe(new GenderedItem<int>(1, 2).GetHashCode());
        new GenderedItem<int>(1, 2).Equals(new GenderedItem<int>(2, 1)).ShouldBeFalse();
        new GenderedItem<string?>("a", null).Equals(new GenderedItem<string?>("a", null)).ShouldBeTrue();
    }

    [Fact]
    public void ArmorAddon_FullImportAndOverlayEqual()
    {
        var read = ReadBoth<IArmorAddonGetter>(Record("ARMA", Hex(EmptyAddon)),
            f => ArmorAddon.CreateFromBinary(f), (s, p) => ArmorAddonBinaryOverlay.ArmorAddonFactory(s, p)).ToArray();
        var full = read[0];
        var overlay = read[1];
        full.WorldModel.ShouldNotBeNull();
        string D(string n, object? a, object? b) => $"{n}: {Equals(a, b)} full={a?.GetType().Name}:{(a is System.Collections.IEnumerable ea ? string.Join(",", ea.Cast<object?>().Select(x => x?.GetType().Name ?? "null")) : "")} ov={b?.GetType().Name}:{(b is System.Collections.IEnumerable eb ? string.Join(",", eb.Cast<object?>().Select(x => x?.GetType().Name ?? "null")) : "")}";
        full.Equals(overlay).ShouldBeTrue(string.Join(" | ", D("WorldModel", full.WorldModel, overlay.WorldModel), D("FirstPersonModel", full.FirstPersonModel, overlay.FirstPersonModel), D("AltSkeleton", full.AltSkeleton, overlay.AltSkeleton), D("SkinTexture", full.SkinTexture, overlay.SkinTexture), D("Morphs", full.Morphs, overlay.Morphs), D("BoneDataModifiers", full.BoneDataModifiers, overlay.BoneDataModifiers)));
        overlay.Equals(full).ShouldBeTrue();
        // A gendered list (bone data modifiers) compares by its items
        Equals(full.BoneDataModifiers, overlay.BoneDataModifiers).ShouldBeTrue();
        var again = ReadBoth<IArmorAddonGetter>(Record("ARMA", Hex(EmptyAddon)),
            f => ArmorAddon.CreateFromBinary(f), (s, p) => ArmorAddonBinaryOverlay.ArmorAddonFactory(s, p)).First();
        full.Equals(again).ShouldBeTrue();
        ((ArmorAddon)full).DeepCopy().Equals(full).ShouldBeTrue();
    }
}
