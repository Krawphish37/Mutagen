using System.Collections;
using Shouldly;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing;
using Noggog;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

public class GroupCommonTests
{
    [Fact]
    public void AddNew()
    {
        var mod = new OblivionMod(TestConstants.PluginModKey,
            OblivionRelease.Oblivion);
        var rec = mod.Npcs.AddNew();
        Assert.Equal(1, mod.Npcs.Count);
        Assert.Same(mod.Npcs.Records.First(), rec);
    }

    [Fact]
    public void AddNew_DifferentForSecond()
    {
        var mod = new OblivionMod(TestConstants.PluginModKey,
            OblivionRelease.Oblivion);
        var rec = mod.Npcs.AddNew();
        var rec2 = mod.Npcs.AddNew();
        Assert.Equal(2, mod.Npcs.Count);
        Assert.NotSame(rec, rec2);
    }

    [Fact]
    public void AddWithFormKey()
    {
        var mod = new OblivionMod(TestConstants.PluginModKey,
            OblivionRelease.Oblivion);
        var rec = mod.Npcs.AddNew(TestConstants.Form1);
        Assert.Equal(1, mod.Npcs.Count);
        Assert.Same(mod.Npcs.Records.First(), rec);
        rec.FormKey.ShouldBe(TestConstants.Form1);
    }

    [Fact]
    public void NonGenericEnumeratorYieldsRecords()
    {
        var mod = new StarfieldMod(TestConstants.PluginModKey,
            StarfieldRelease.Starfield);
        var weap = mod.ObjectModifications.AddReturn(new WeaponModification(mod));
        var armo = mod.ObjectModifications.AddReturn(new ArmorModification(mod));
        EnumerateNonGeneric(mod.ObjectModifications)
            .ShouldBe(new object[] { weap, armo }, ignoreOrder: true);
        mod.ObjectModifications.OfType<IWeaponModificationGetter>()
            .ShouldBe(new[] { (IWeaponModificationGetter)weap });
    }

    [Fact]
    public void OverlayNonGenericEnumeratorYieldsRecords()
    {
        var mod = new StarfieldMod(TestConstants.PluginModKey,
            StarfieldRelease.Starfield);
        var weap = mod.ObjectModifications.AddReturn(new WeaponModification(mod));
        mod.ObjectModifications.AddReturn(new ArmorModification(mod));
        var stream = new MemoryTributary();
        mod.WriteToBinary(stream);
        stream.Position = 0;
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(stream, StarfieldRelease.Starfield, mod.ModKey);
        EnumerateNonGeneric(overlay.ObjectModifications)
            .Select(x => x.ShouldBeAssignableTo<IAObjectModificationGetter>()!.FormKey)
            .ShouldBe(mod.ObjectModifications.FormKeys, ignoreOrder: true);
        overlay.ObjectModifications.OfType<IWeaponModificationGetter>()
            .Select(x => x.FormKey)
            .ShouldBe(new[] { weap.FormKey });
    }

    private static List<object?> EnumerateNonGeneric(IEnumerable enumerable)
    {
        var ret = new List<object?>();
        foreach (var item in enumerable)
        {
            ret.Add(item);
        }
        return ret;
    }
}