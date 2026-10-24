using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing;
using Xunit;
using FO4 = Mutagen.Bethesda.Fallout4;
using SF = Mutagen.Bethesda.Starfield;
using SSE = Mutagen.Bethesda.Skyrim;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

/// <summary>
/// Removal by a type that inherits from a group's record type, such as IWeaponModification
/// within the AObjectModification group
/// </summary>
public class RemoveInheritingTypeTests
{
    private static (StarfieldMod Mod, SF.WeaponModification Weapon, SF.ArmorModification Armor) StarfieldObjectModifications()
    {
        var mod = new StarfieldMod(TestConstants.PluginModKey, StarfieldRelease.Starfield);
        var weapon = new SF.WeaponModification(mod);
        var armor = new SF.ArmorModification(mod);
        mod.ObjectModifications.Add(weapon);
        mod.ObjectModifications.Add(armor);
        return (mod, weapon, armor);
    }

    [Fact]
    public void Generic()
    {
        var (mod, weapon, armor) = StarfieldObjectModifications();
        mod.Remove<SF.IWeaponModification>(weapon.FormKey);
        Assert.Same(armor, Assert.Single(mod.ObjectModifications));

        (mod, weapon, armor) = StarfieldObjectModifications();
        mod.Remove<SF.IWeaponModificationGetter>(weapon.FormKey);
        Assert.Same(armor, Assert.Single(mod.ObjectModifications));

        (mod, weapon, armor) = StarfieldObjectModifications();
        mod.Remove(weapon);
        Assert.Same(armor, Assert.Single(mod.ObjectModifications));

        (mod, weapon, armor) = StarfieldObjectModifications();
        mod.Remove<SF.IWeaponModification>(new[] { weapon.FormKey, armor.FormKey });
        Assert.Same(armor, Assert.Single(mod.ObjectModifications));
    }

    [Fact]
    public void ByType()
    {
        var (mod, weapon, armor) = StarfieldObjectModifications();
        mod.Remove(weapon.FormKey, typeof(SF.IWeaponModification));
        Assert.Same(armor, Assert.Single(mod.ObjectModifications));

        (mod, weapon, armor) = StarfieldObjectModifications();
        mod.Remove(new[] { weapon.FormKey, armor.FormKey }, typeof(SF.IWeaponModificationGetter));
        Assert.Same(armor, Assert.Single(mod.ObjectModifications));

        (mod, weapon, armor) = StarfieldObjectModifications();
        mod.Remove(new HashSet<FormKey> { weapon.FormKey, armor.FormKey }, typeof(SF.ArmorModification));
        Assert.Same(weapon, Assert.Single(mod.ObjectModifications));
    }

    [Fact]
    public void SkipsSiblings()
    {
        var (mod, weapon, armor) = StarfieldObjectModifications();
        mod.Remove<SF.IWeaponModification>(armor.FormKey);
        mod.Remove(armor.FormKey, typeof(SF.IWeaponModificationGetter));
        mod.Remove<SF.IArmorModification>(weapon.FormKey);
        Assert.Equal(2, mod.ObjectModifications.Count);
    }

    [Fact]
    public void Group()
    {
        var (mod, weapon, armor) = StarfieldObjectModifications();
        mod.ObjectModifications.Remove<SF.AObjectModification, SF.IWeaponModification>(armor.FormKey);
        Assert.Equal(2, mod.ObjectModifications.Count);
        mod.ObjectModifications.Remove<SF.AObjectModification, SF.IWeaponModification>(weapon.FormKey);
        Assert.Same(armor, Assert.Single(mod.ObjectModifications));
    }

    [Fact]
    public void BaseTypeRemovesAnySubclass()
    {
        var (mod, weapon, armor) = StarfieldObjectModifications();
        mod.Remove<SF.IAObjectModification>(weapon.FormKey);
        Assert.Same(armor, Assert.Single(mod.ObjectModifications));
        mod.Remove(armor.FormKey, typeof(SF.IAObjectModificationGetter));
        Assert.Empty(mod.ObjectModifications);
    }

    [Fact]
    public void Fallout4ObjectModifications()
    {
        var mod = new Fallout4Mod(TestConstants.PluginModKey, Fallout4Release.Fallout4);
        var weapon = new FO4.WeaponModification(mod);
        var armor = new FO4.ArmorModification(mod);
        mod.ObjectModifications.Add(weapon);
        mod.ObjectModifications.Add(armor);
        mod.Remove<FO4.IWeaponModification>(armor.FormKey);
        Assert.Equal(2, mod.ObjectModifications.Count);
        mod.Remove<FO4.IWeaponModification>(weapon.FormKey);
        Assert.Same(armor, Assert.Single(mod.ObjectModifications));
    }

    [Fact]
    public void GlobalsAndGameSettings()
    {
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var globalInt = mod.Globals.AddNewInt();
        var globalFloat = mod.Globals.AddNewFloat();
        var gameSettingInt = mod.GameSettings.AddNewInt();
        var gameSettingFloat = mod.GameSettings.AddNewFloat();
        mod.Remove<SSE.IGlobalInt>(globalFloat.FormKey);
        mod.Remove<SSE.IGameSettingIntGetter>(gameSettingFloat.FormKey);
        Assert.Equal(2, mod.Globals.Count);
        Assert.Equal(2, mod.GameSettings.Count);
        mod.Remove<SSE.IGlobalInt>(globalInt.FormKey);
        mod.Remove<SSE.IGameSettingIntGetter>(gameSettingInt.FormKey);
        Assert.Same(globalFloat, Assert.Single(mod.Globals));
        Assert.Same(gameSettingFloat, Assert.Single(mod.GameSettings));
    }
}
