using Shouldly;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Cache.Linking;

public class MutableLoadOrderLinkCacheAddTests
{
    private record Setup(
        SkyrimMod Master,
        INpc MasterNpc,
        INpc MasterOnlyNpc,
        SkyrimMod Plugin,
        INpc PluginOverride,
        INpc PluginNewNpc);

    /// <summary>
    /// A master with two NPCs, and a plugin that overrides one of them and adds a new one.
    /// </summary>
    private static Setup BuildMods()
    {
        var master = new SkyrimMod(TestConstants.MasterModKey, SkyrimRelease.SkyrimSE);
        var masterNpc = master.Npcs.AddNew();
        masterNpc.EditorID = "SharedNpc";
        var masterOnlyNpc = master.Npcs.AddNew();
        masterOnlyNpc.EditorID = "MasterOnlyNpc";

        var plugin = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var pluginOverride = plugin.Npcs.GetOrAddAsOverride(masterNpc);
        pluginOverride.EditorID = "SharedNpcOverridden";
        var pluginNewNpc = plugin.Npcs.AddNew();
        pluginNewNpc.EditorID = "PluginNewNpc";

        return new Setup(master, masterNpc, masterOnlyNpc, plugin, pluginOverride, pluginNewNpc);
    }

    private static void ShouldHaveWinners(IEnumerable<INpcGetter> winners, Setup setup)
    {
        var list = winners.ToList();
        list.Count.ShouldBe(3);
        list.ShouldContain(setup.PluginOverride);
        list.ShouldContain(setup.PluginNewNpc);
        list.ShouldContain(setup.MasterOnlyNpc);
        list.ShouldNotContain(setup.MasterNpc);
    }

    [Fact]
    public void Typed_AddAfterImmutableBase_UpdatesOrders()
    {
        var setup = BuildMods();
        var immutableBase = new LoadOrder<ISkyrimModGetter>() { setup.Master }
            .ToImmutableLinkCache<ISkyrimMod, ISkyrimModGetter>();
        var cache = new MutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter>(immutableBase);

        cache.Add(setup.Plugin);

        cache.ListedOrder.Select(x => x.ModKey)
            .ShouldBe(new[] { TestConstants.MasterModKey, TestConstants.PluginModKey });
        cache.PriorityOrder.Select(x => x.ModKey)
            .ShouldBe(new[] { TestConstants.PluginModKey, TestConstants.MasterModKey });
    }

    [Fact]
    public void Typed_AddAfterImmutableBase_WinningOverridesIncludeAddedMod()
    {
        var setup = BuildMods();
        var immutableBase = new LoadOrder<ISkyrimModGetter>() { setup.Master }
            .ToImmutableLinkCache<ISkyrimMod, ISkyrimModGetter>();
        var cache = new MutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter>(immutableBase);

        cache.Add(setup.Plugin);

        ShouldHaveWinners(cache.WinningOverrides<INpcGetter>(), setup);
        ShouldHaveWinners(cache.WinningOverrides(typeof(INpcGetter)).Cast<INpcGetter>(), setup);

        var contexts = cache.WinningContextOverrides<INpc, INpcGetter>(cache).ToList();
        contexts.Count.ShouldBe(3);
        contexts.Single(x => x.Record.FormKey == setup.MasterNpc.FormKey).ModKey
            .ShouldBe(TestConstants.PluginModKey);
        contexts.Single(x => x.Record.FormKey == setup.PluginNewNpc.FormKey).ModKey
            .ShouldBe(TestConstants.PluginModKey);
        contexts.Single(x => x.Record.FormKey == setup.MasterOnlyNpc.FormKey).ModKey
            .ShouldBe(TestConstants.MasterModKey);

        cache.WinningContextOverrides(cache, typeof(INpcGetter))
            .Single(x => x.Record.FormKey == setup.MasterNpc.FormKey).ModKey
            .ShouldBe(TestConstants.PluginModKey);
    }

    [Fact]
    public void Typed_AddAfterMutableMods_AppendsToEnd()
    {
        var setup = BuildMods();
        var cache = new MutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter>(setup.Master);

        cache.Add(setup.Plugin);

        cache.ListedOrder.Select(x => x.ModKey)
            .ShouldBe(new[] { TestConstants.MasterModKey, TestConstants.PluginModKey });
        cache.PriorityOrder.Select(x => x.ModKey)
            .ShouldBe(new[] { TestConstants.PluginModKey, TestConstants.MasterModKey });
        ShouldHaveWinners(cache.WinningOverrides<INpcGetter>(), setup);
    }

    [Fact]
    public void Typed_AddMatchesConstructorOrder()
    {
        var setup = BuildMods();
        var patch = new SkyrimMod(TestConstants.PluginModKey2, SkyrimRelease.SkyrimSE);
        var immutableBase = new LoadOrder<ISkyrimModGetter>() { setup.Master }
            .ToImmutableLinkCache<ISkyrimMod, ISkyrimModGetter>();

        var viaCtor = new MutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter>(immutableBase, patch, setup.Plugin);
        var viaAdd = new MutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter>(immutableBase, patch);
        viaAdd.Add(setup.Plugin);

        viaAdd.ListedOrder.ShouldBe(viaCtor.ListedOrder);
        viaAdd.PriorityOrder.ShouldBe(viaCtor.PriorityOrder);
    }

    [Fact]
    public void Untyped_Add_UpdatesOrdersAndWinningOverrides()
    {
        var setup = BuildMods();
        var cache = new MutableLoadOrderLinkCache(setup.Master);

        cache.Add(setup.Plugin);

        cache.ListedOrder.Select(x => x.ModKey)
            .ShouldBe(new[] { TestConstants.MasterModKey, TestConstants.PluginModKey });
        cache.PriorityOrder.Select(x => x.ModKey)
            .ShouldBe(new[] { TestConstants.PluginModKey, TestConstants.MasterModKey });
        ShouldHaveWinners(cache.WinningOverrides<INpcGetter>(), setup);
        ShouldHaveWinners(cache.WinningOverrides(typeof(INpcGetter)).Cast<INpcGetter>(), setup);
    }

    [Fact]
    public void Untyped_AddAfterImmutableBase_UpdatesOrders()
    {
        var setup = BuildMods();
        var immutableBase = new ImmutableLoadOrderLinkCache(
            new IModGetter[] { setup.Master },
            GameCategory.Skyrim,
            LinkCachePreferences.Default);
        var cache = new MutableLoadOrderLinkCache(immutableBase);

        cache.Add(setup.Plugin);

        cache.ListedOrder.Select(x => x.ModKey)
            .ShouldBe(new[] { TestConstants.MasterModKey, TestConstants.PluginModKey });
        ShouldHaveWinners(cache.WinningOverrides<INpcGetter>(), setup);
    }
}
