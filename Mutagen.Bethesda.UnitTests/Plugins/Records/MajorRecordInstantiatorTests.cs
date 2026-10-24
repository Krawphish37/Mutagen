using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Utility;
using Xunit;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Testing;
using Sf = Mutagen.Bethesda.Starfield;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

public class MajorRecordInstantiatorTests
{
    [Fact]
    public void DirectGeneric()
    {
        Warmup.Init();
        var form = new FormKey(Mutagen.Bethesda.Oblivion.Constants.Oblivion, 0x123456);
        var ret = MajorRecordInstantiator<Ammunition>.Activator(form, GameRelease.Oblivion);
        Assert.IsType<Ammunition>(ret);
        Assert.Equal(form, ret.FormKey);
    }

    [Fact]
    public void SetterGeneric()
    {
        Warmup.Init();
        var form = new FormKey(Mutagen.Bethesda.Oblivion.Constants.Oblivion, 0x123456);
        var ret = MajorRecordInstantiator<IAmmunition>.Activator(form, GameRelease.Oblivion);
        Assert.IsType<Ammunition>(ret);
        Assert.Equal(form, ret.FormKey);
    }

    [Fact]
    public void GetterGeneric()
    {
        Warmup.Init();
        var form = new FormKey(Mutagen.Bethesda.Oblivion.Constants.Oblivion, 0x123456);
        var ret = MajorRecordInstantiator<IAmmunitionGetter>.Activator(form, GameRelease.Oblivion);
        Assert.IsType<Ammunition>(ret);
        Assert.Equal(form, ret.FormKey);
    }
        
    [Fact]
    public void Direct()
    {
        Warmup.Init();
        var form = new FormKey(Mutagen.Bethesda.Oblivion.Constants.Oblivion, 0x123456);
        var ret = MajorRecordInstantiator.Activator(form, GameRelease.Oblivion, typeof(Ammunition));
        Assert.IsType<Ammunition>(ret);
        Assert.Equal(form, ret.FormKey);
    }

    [Fact]
    public void Setter()
    {
        Warmup.Init();
        var form = new FormKey(Mutagen.Bethesda.Oblivion.Constants.Oblivion, 0x123456);
        var ret = MajorRecordInstantiator.Activator(form, GameRelease.Oblivion, typeof(IAmmunition));
        Assert.IsType<Ammunition>(ret);
        Assert.Equal(form, ret.FormKey);
    }

    [Fact]
    public void Getter()
    {
        Warmup.Init();
        var form = new FormKey(Mutagen.Bethesda.Oblivion.Constants.Oblivion, 0x123456);
        var ret = MajorRecordInstantiator.Activator(form, GameRelease.Oblivion, typeof(IAmmunitionGetter));
        Assert.IsType<Ammunition>(ret);
        Assert.Equal(form, ret.FormKey);
    }

    [Fact]
    public void AbstractGeneric()
    {
        Warmup.Init();
        var form = new FormKey(Mutagen.Bethesda.Oblivion.Constants.Oblivion, 0x123456);
        var ex = Assert.Throws<InvalidOperationException>(() => MajorRecordInstantiator<GameSetting>.Activator(form, GameRelease.Oblivion));
        Assert.Contains(nameof(GameSetting), ex.Message);
        Assert.Contains(nameof(GameSettingFloat), ex.Message);
        Assert.Contains(nameof(GameSettingInt), ex.Message);
        Assert.Contains(nameof(GameSettingString), ex.Message);
        // Type initializer is not poisoned, so a second call gets the same error
        Assert.Throws<InvalidOperationException>(() => MajorRecordInstantiator<IGameSettingGetter>.Activator(form, GameRelease.Oblivion));
        Assert.Throws<InvalidOperationException>(() => MajorRecordInstantiator<GameSetting>.Activator(form, GameRelease.Oblivion));
    }

    [Fact]
    public void Abstract()
    {
        Warmup.Init();
        var form = new FormKey(Mutagen.Bethesda.Oblivion.Constants.Oblivion, 0x123456);
        var ex = Assert.Throws<InvalidOperationException>(() => MajorRecordInstantiator.Activator(form, GameRelease.Oblivion, typeof(IGameSetting)));
        Assert.Contains(nameof(GameSetting), ex.Message);
        Assert.Contains(nameof(GameSettingFloat), ex.Message);
    }

    [Fact]
    public void AddNewAbstractGroup()
    {
        var mod = new Sf.StarfieldMod(TestConstants.PluginModKey, Sf.StarfieldRelease.Starfield);
        var nextFormId = mod.ModHeader.Stats.NextFormID;

        var ex = Assert.Throws<InvalidOperationException>(() => mod.ObjectModifications.AddNew());
        Assert.Contains(nameof(Sf.AObjectModification), ex.Message);
        Assert.Contains(nameof(Sf.WeaponModification), ex.Message);
        Assert.Throws<InvalidOperationException>(() => mod.ObjectModifications.AddNew("EditorID"));
        Assert.Throws<InvalidOperationException>(() => ((IGroup)mod.ObjectModifications).AddNew());
        Assert.Throws<InvalidOperationException>(() => mod.GameSettings.AddNew());
        Assert.Empty(mod.ObjectModifications);
        Assert.Equal(nextFormId, mod.ModHeader.Stats.NextFormID);

        // Other groups are unaffected
        var weapon = mod.Weapons.AddNew("Weapon");
        Assert.Equal(nextFormId, weapon.FormKey.ID);
        mod.ObjectModifications.Add(new Sf.WeaponModification(mod));
        Assert.Single(mod.ObjectModifications);
    }
}
