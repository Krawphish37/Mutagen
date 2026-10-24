using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

public class GenderedTranslationMaskTests
{
    private static ArmorAddon GetArmorAddon(string prefix, byte malePriority, byte femalePriority)
    {
        return new ArmorAddon(TestConstants.Form1, SkyrimRelease.SkyrimSE)
        {
            Priority = new GenderedItem<byte>(malePriority, femalePriority),
            WorldModel = new GenderedItem<Model?>(
                new Model() { File = $"{prefix}Male.nif" },
                new Model() { File = $"{prefix}Female.nif" }),
        };
    }

    private static Model.TranslationMask ModelOff() => new(defaultOn: true, onOverall: false);

    [Fact]
    public void CrystalNullFieldFollowsDefaultOn()
    {
        new ArmorAddon.TranslationMask(defaultOn: true).GetCrystal()
            .GetShouldTranslate((int)ArmorAddon_FieldIndex.WorldModel).ShouldBeTrue();
        new ArmorAddon.TranslationMask(defaultOn: false).GetCrystal()
            .GetShouldTranslate((int)ArmorAddon_FieldIndex.WorldModel).ShouldBeFalse();
    }

    [Fact]
    public void CrystalCanSwitchOffGenderedField()
    {
        var crystal = new ArmorAddon.TranslationMask(defaultOn: true)
        {
            Priority = new GenderedItem<bool>(false, false),
            WorldModel = new GenderedItem<Model.TranslationMask>(ModelOff(), ModelOff()),
        }.GetCrystal();
        crystal.GetShouldTranslate((int)ArmorAddon_FieldIndex.Priority).ShouldBeFalse();
        crystal.GetShouldTranslate((int)ArmorAddon_FieldIndex.WorldModel).ShouldBeFalse();
    }

    [Fact]
    public void CrystalKeepsGenderedSubMasks()
    {
        var crystal = new ArmorAddon.TranslationMask(defaultOn: false)
        {
            WorldModel = new GenderedItem<Model.TranslationMask>(
                ModelOff(),
                new Model.TranslationMask(defaultOn: false) { File = true }),
        }.GetCrystal();
        crystal.GetShouldTranslate((int)ArmorAddon_FieldIndex.WorldModel).ShouldBeTrue();
        var sub = crystal.GetSubCrystal((int)ArmorAddon_FieldIndex.WorldModel);
        sub.ShouldNotBeNull();
        sub.GetShouldTranslate(0).ShouldBeFalse();
        sub.GetShouldTranslate(1).ShouldBeTrue();
        var female = sub.GetSubCrystal(1);
        female.ShouldNotBeNull();
        female.GetShouldTranslate((int)Model_FieldIndex.File).ShouldBeTrue();
        female.GetShouldTranslate((int)Model_FieldIndex.AlternateTextures).ShouldBeFalse();
    }

    [Fact]
    public void DeepCopyExcludesGenderedFieldSwitchedOff()
    {
        var rec = GetArmorAddon("Src", 1, 2);
        var copy = rec.DeepCopy(new ArmorAddon.TranslationMask(defaultOn: true)
        {
            Priority = new GenderedItem<bool>(false, false),
            WorldModel = new GenderedItem<Model.TranslationMask>(ModelOff(), ModelOff()),
        });
        copy.WorldModel.ShouldBeNull();
        copy.Priority.Male.ShouldBe((byte)0);
        copy.Priority.Female.ShouldBe((byte)0);
    }

    [Fact]
    public void DeepCopyDefaultOffExcludesGenderedFields()
    {
        var rec = GetArmorAddon("Src", 1, 2);
        var copy = rec.DeepCopy(new ArmorAddon.TranslationMask(defaultOn: false)
        {
            EditorID = true
        });
        copy.WorldModel.ShouldBeNull();
        copy.Priority.Male.ShouldBe((byte)0);
        copy.Priority.Female.ShouldBe((byte)0);
    }

    [Fact]
    public void DeepCopyInLeavesGenderedFieldSwitchedOff()
    {
        var rec = GetArmorAddon("Src", 1, 2);
        var target = GetArmorAddon("Target", 3, 4);
        target.DeepCopyIn(rec, new ArmorAddon.TranslationMask(defaultOn: true)
        {
            Priority = new GenderedItem<bool>(false, false),
            WorldModel = new GenderedItem<Model.TranslationMask>(ModelOff(), ModelOff()),
        });
        target.WorldModel!.Male!.File.GivenPath.ShouldBe("TargetMale.nif");
        target.WorldModel!.Female!.File.GivenPath.ShouldBe("TargetFemale.nif");
        target.Priority.Male.ShouldBe((byte)3);
        target.Priority.Female.ShouldBe((byte)4);
    }

    [Fact]
    public void DeepCopyInCopiesOnlyGenderSwitchedOn()
    {
        var rec = GetArmorAddon("Src", 1, 2);
        var target = GetArmorAddon("Target", 3, 4);
        target.DeepCopyIn(rec, new ArmorAddon.TranslationMask(defaultOn: false)
        {
            Priority = new GenderedItem<bool>(true, false),
            WorldModel = new GenderedItem<Model.TranslationMask>(ModelOff(), new Model.TranslationMask(defaultOn: true)),
        });
        target.Priority.Male.ShouldBe((byte)1);
        target.Priority.Female.ShouldBe((byte)4);
        target.WorldModel!.Male!.File.GivenPath.ShouldBe("TargetMale.nif");
        target.WorldModel!.Female!.File.GivenPath.ShouldBe("SrcFemale.nif");
    }

    [Fact]
    public void DeepCopyPassesGenderedSubMask()
    {
        var rec = GetArmorAddon("Src", 1, 2);
        rec.WorldModel!.Male!.AlternateTextures = new();
        var copy = rec.DeepCopy(new ArmorAddon.TranslationMask(defaultOn: false)
        {
            WorldModel = new GenderedItem<Model.TranslationMask>(
                new Model.TranslationMask(defaultOn: false) { File = true },
                new Model.TranslationMask(defaultOn: false) { File = true }),
        });
        copy.WorldModel!.Male!.File.GivenPath.ShouldBe("SrcMale.nif");
        copy.WorldModel!.Male!.AlternateTextures.ShouldBeNull();
        copy.WorldModel!.Female!.File.GivenPath.ShouldBe("SrcFemale.nif");
    }

    [Fact]
    public void DeepCopyWithoutMaskCopiesGenderedFields()
    {
        var rec = GetArmorAddon("Src", 1, 2);
        var copy = rec.DeepCopy();
        copy.WorldModel!.Male!.File.GivenPath.ShouldBe("SrcMale.nif");
        copy.WorldModel!.Male.ShouldNotBeSameAs(rec.WorldModel!.Male);
        copy.WorldModel!.Female!.File.GivenPath.ShouldBe("SrcFemale.nif");
        copy.Priority.Male.ShouldBe((byte)1);
        copy.Priority.Female.ShouldBe((byte)2);

        var target = GetArmorAddon("Target", 3, 4);
        target.DeepCopyIn(rec);
        target.WorldModel!.Male!.File.GivenPath.ShouldBe("SrcMale.nif");
        target.Priority.Female.ShouldBe((byte)2);
    }

    [Fact]
    public void EqualsIgnoresGenderedFieldSwitchedOff()
    {
        var lhs = GetArmorAddon("Lhs", 1, 2);
        var rhs = GetArmorAddon("Rhs", 1, 2);
        // Same instance, so only WorldModel differs
        rhs.Priority = lhs.Priority;
        lhs.Equals(rhs).ShouldBeFalse();
        lhs.Equals(rhs, new ArmorAddon.TranslationMask(defaultOn: true)
        {
            // A separate gendered item per record, which compares by reference
            WeightSliderEnabled = new GenderedItem<bool>(false, false),
            WorldModel = new GenderedItem<Model.TranslationMask>(ModelOff(), ModelOff()),
        }).ShouldBeTrue();
    }
}
