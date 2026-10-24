using System.Drawing;
using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Assets;
using Mutagen.Bethesda.Testing;
using Noggog;
using Xunit;
using StarfieldKeyword = Mutagen.Bethesda.Starfield.Keyword;
using StarfieldRelease = Mutagen.Bethesda.Starfield.StarfieldRelease;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

/// <summary>
/// Records that are Equal must also give the same hash code
/// </summary>
public class HashCodeTests
{
    private static void ShouldHashTheSame<T>(T lhs, T rhs)
        where T : notnull
    {
        lhs.Equals(rhs).ShouldBeTrue();
        lhs.GetHashCode().ShouldBe(rhs.GetHashCode());
    }

    private Weapon GetSomeWeapon()
    {
        return new Weapon(TestConstants.Form1, SkyrimRelease.SkyrimSE)
        {
            EditorID = "Weapon",
            Keywords = new ExtendedList<IFormLinkGetter<IKeywordGetter>>()
            {
                TestConstants.Form2.ToLink<IKeywordGetter>(),
            },
            Unused = new byte[] { 1, 2, 3 },
        };
    }

    [Fact]
    public void FreshRecords()
    {
        ShouldHashTheSame(
            new Npc(TestConstants.Form1, SkyrimRelease.SkyrimSE),
            new Npc(TestConstants.Form1, SkyrimRelease.SkyrimSE));
    }

    [Fact]
    public void DeepCopy()
    {
        var weapon = GetSomeWeapon();
        var copy = weapon.DeepCopy();
        ShouldHashTheSame(weapon, copy);
        new HashSet<IWeaponGetter>() { weapon }
            .Contains(copy)
            .ShouldBeTrue();
    }

    [Fact]
    public void ListContents()
    {
        var npc1 = new Npc(TestConstants.Form1, SkyrimRelease.SkyrimSE);
        var npc2 = new Npc(TestConstants.Form1, SkyrimRelease.SkyrimSE);
        npc1.HeadParts.Add(TestConstants.Form2);
        npc2.HeadParts.Add(TestConstants.Form2);
        ShouldHashTheSame(npc1, npc2);

        var hash = npc1.GetHashCode();
        npc1.HeadParts.Add(TestConstants.Form3);
        npc1.GetHashCode().ShouldNotBe(hash);
    }

    [Fact]
    public void ByteArrayContents()
    {
        var weapon1 = GetSomeWeapon();
        var weapon2 = GetSomeWeapon();
        weapon1.Unused = new byte[] { 4, 5, 6 };
        weapon2.Unused = new byte[] { 4, 5, 6 };
        ShouldHashTheSame(weapon1, weapon2);
    }

    [Fact]
    public void DictionaryContents()
    {
        var class1 = new Class(TestConstants.Form1, SkyrimRelease.SkyrimSE);
        var class2 = new Class(TestConstants.Form1, SkyrimRelease.SkyrimSE);
        class1.SkillWeights[Skill.OneHanded] = 5;
        class2.SkillWeights[Skill.OneHanded] = 5;
        ShouldHashTheSame(class1, class2);
    }

    [Fact]
    public void ArrayContents()
    {
        var weather1 = new Weather(TestConstants.Form1, SkyrimRelease.SkyrimSE);
        var weather2 = new Weather(TestConstants.Form1, SkyrimRelease.SkyrimSE);
        weather1.CloudTextures[0] = new AssetLink<SkyrimTextureAssetType>("Sky\\Cloud.dds");
        weather2.CloudTextures[0] = new AssetLink<SkyrimTextureAssetType>("Sky\\Cloud.dds");
        ShouldHashTheSame(weather1, weather2);
    }

    [Fact]
    public void Array2dContents()
    {
        var landscape1 = new Landscape(TestConstants.Form1, SkyrimRelease.SkyrimSE)
        {
            VertexNormals = new Array2d<P3UInt8>(33, 33, new P3UInt8(1, 2, 3))
        };
        var landscape2 = new Landscape(TestConstants.Form1, SkyrimRelease.SkyrimSE)
        {
            VertexNormals = new Array2d<P3UInt8>(33, 33, new P3UInt8(1, 2, 3))
        };
        ShouldHashTheSame(landscape1, landscape2);
    }

    [Fact]
    public void ColorComparedByArgb()
    {
        var npc1 = new Npc(TestConstants.Form1, SkyrimRelease.SkyrimSE)
        {
            TextureLighting = Color.Red
        };
        var npc2 = npc1.DeepCopy();
        npc2.TextureLighting = Color.FromArgb(255, 255, 0, 0);
        ShouldHashTheSame(npc1, npc2);
    }

    [Fact]
    public void StarfieldKeywordDeepCopy()
    {
        var keyword = new StarfieldKeyword(TestConstants.Form1, StarfieldRelease.Starfield)
        {
            EditorID = "Keyword",
            Notes = "Notes",
        };
        var copy = Mutagen.Bethesda.Starfield.KeywordMixIn.DeepCopy(keyword);
        ShouldHashTheSame(keyword, copy);
        new HashSet<Mutagen.Bethesda.Starfield.IKeywordGetter>() { keyword }
            .Contains(copy)
            .ShouldBeTrue();
    }
}
