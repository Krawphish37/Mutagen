using System.Collections.Immutable;
using System.Reflection;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations.Internal;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Mapping;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using Shouldly;

namespace Mutagen.Bethesda.UnitTests.Plugins.Cache.Linking;

public class ResolveAllDepthCacheTests
{
    private static readonly FormKey MissingKey = new(ModKey.FromFileName("NotInLoadOrder.esp"), 0x800);

    private static (SkyrimMod First, SkyrimMod Second, IReadOnlyList<IModGetter> LoadOrder) GetLoadOrder()
    {
        var first = new SkyrimMod(ModKey.FromFileName("First.esp"), SkyrimRelease.SkyrimSE);
        var second = new SkyrimMod(ModKey.FromFileName("Second.esp"), SkyrimRelease.SkyrimSE);
        first.Npcs.AddNew();
        second.Npcs.AddNew();
        return (first, second, new IModGetter[] { first, second });
    }

    private static ImmutableLoadOrderLinkCacheCategory<FormKey> GetCategory(IReadOnlyList<IModGetter> loadOrder)
    {
        return new ImmutableLoadOrderLinkCacheCategory<FormKey>(
            GameCategory.Skyrim,
            hasAny: true,
            simple: false,
            listedOrder: loadOrder,
            metaInterfaceMapGetter: MetaInterfaceMapping.Instance,
            m => TryGet<FormKey>.Succeed(m.FormKey),
            f => f.IsNull,
            equalityComparer: null);
    }

    private static DepthCache<FormKey, T> GetAllCache<T>(object category, string fieldName, Type type)
    {
        var field = category.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        var dict = (Dictionary<Type, DepthCache<FormKey, T>>)field.GetValue(category)!;
        lock (dict)
        {
            return dict[type];
        }
    }

    [Fact]
    public void ResolveAllMissingKeyDoesNotAddCacheEntry()
    {
        var (first, second, loadOrder) = GetLoadOrder();
        var category = GetCategory(loadOrder);

        category.ResolveAll(MissingKey, MissingKey.ModKey, typeof(INpcGetter)).ShouldBeEmpty();

        var cache = GetAllCache<ImmutableList<LinkCacheItem>>(category, "_allRecords", typeof(INpcGetter));
        cache.Done.ShouldBeTrue();
        cache.TryGetValue(MissingKey, out _).ShouldBeFalse();
        category.ResolveAll(first.Npcs.First().FormKey, null, typeof(INpcGetter)).Count().ShouldBe(1);
        category.ResolveAll(second.Npcs.First().FormKey, null, typeof(INpcGetter)).Count().ShouldBe(1);
    }

    [Fact]
    public void ResolveAllSimpleContextsMissingKeyDoesNotAddCacheEntry()
    {
        var (_, _, loadOrder) = GetLoadOrder();
        var linkCache = loadOrder.Cast<ISkyrimModGetter>().ToImmutableLinkCache();
        var category = new ImmutableLoadOrderLinkCacheSimpleContextCategory<FormKey>(
            GameCategory.Skyrim,
            metaInterfaceMapGetter: MetaInterfaceMapping.Instance,
            simple: false,
            hasAny: true,
            linkCache,
            loadOrder,
            m => TryGet<FormKey>.Succeed(m.FormKey),
            f => f.IsNull,
            equalityComparer: null);

        category.ResolveAllSimpleContexts(MissingKey, MissingKey.ModKey, typeof(INpcGetter)).ShouldBeEmpty();

        var cache = GetAllCache<ImmutableList<IModContext<IMajorRecordGetter>>>(category, "_allContexts", typeof(INpcGetter));
        cache.Done.ShouldBeTrue();
        cache.TryGetValue(MissingKey, out _).ShouldBeFalse();
    }

    [Fact]
    public void ResolveAllContextsMissingKeyDoesNotAddCacheEntry()
    {
        var (_, _, loadOrder) = GetLoadOrder();
        var mods = loadOrder.Cast<ISkyrimModGetter>().ToArray();
        var linkCache = mods.ToImmutableLinkCache();
        var category = new ImmutableLoadOrderLinkCacheContextCategory<ISkyrimMod, ISkyrimModGetter, FormKey>(
            GameCategory.Skyrim,
            metaInterfaceMapGetter: MetaInterfaceMapping.Instance,
            simple: false,
            hasAny: true,
            linkCache,
            mods,
            m => TryGet<FormKey>.Succeed(m.FormKey),
            f => f.IsNull,
            equalityComparer: null);

        category.ResolveAllContexts(MissingKey, MissingKey.ModKey, typeof(INpcGetter)).ShouldBeEmpty();

        var cache = GetAllCache<ImmutableList<IModContext<ISkyrimMod, ISkyrimModGetter, IMajorRecord, IMajorRecordGetter>>>(category, "_allContexts", typeof(INpcGetter));
        cache.Done.ShouldBeTrue();
        cache.TryGetValue(MissingKey, out _).ShouldBeFalse();
    }

    [Fact]
    public void ResolveAllWaitingWhileCacheFinishesDoesNotWriteDoneCache()
    {
        var (first, second, loadOrder) = GetLoadOrder();
        var category = GetCategory(loadOrder);

        // Process only the last mod, so the cache is not done yet
        category.ResolveAll(second.Npcs.First().FormKey, second.ModKey, typeof(INpcGetter)).Count().ShouldBe(1);
        var cache = GetAllCache<ImmutableList<LinkCacheItem>>(category, "_allRecords", typeof(INpcGetter));
        cache.Done.ShouldBeFalse();

        Thread waiter;
        lock (cache)
        {
            // A second thread sees the cache is not done, and waits on its lock
            waiter = new Thread(() => category.ResolveAll(MissingKey, MissingKey.ModKey, typeof(INpcGetter)).ToArray());
            waiter.Start();
            while ((waiter.ThreadState & ThreadState.WaitSleepJoin) == 0)
            {
                Thread.Yield();
            }
            Thread.Sleep(50);

            // Meanwhile this thread finishes the cache, after which it is read without locking
            category.ResolveAll(first.Npcs.First().FormKey, null, typeof(INpcGetter)).Count().ShouldBe(1);
            cache.Done.ShouldBeTrue();
        }
        waiter.Join();

        // The waiting thread must not have written into the done cache
        cache.TryGetValue(MissingKey, out _).ShouldBeFalse();
    }
}
