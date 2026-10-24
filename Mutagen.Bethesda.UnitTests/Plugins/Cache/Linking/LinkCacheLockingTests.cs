using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations.Internal;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Mapping;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Cache.Linking;

public class LinkCacheLockingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public enum LookupKind
    {
        LoadOrderRecord,
        LoadOrderContext,
        LoadOrderSimpleContext,
        ModRecord,
        ModContext,
        ModSimpleContext,
    }

    /// <summary>
    /// Wraps the normal mapping, but can hold a thread inside a lookup for one interface type.
    /// Link caches ask it while registering a new interface type cache, which they do under a lock.
    /// </summary>
    private sealed class BlockingInterfaceMapGetter : IMetaInterfaceMapGetter
    {
        public Type? BlockOn;
        public readonly ManualResetEventSlim Entered = new();
        public readonly ManualResetEventSlim Release = new();

        private void MaybeBlock(Type type)
        {
            if (type != BlockOn) return;
            Entered.Set();
            Release.Wait();
        }

        public bool TryGetRegistrationsForInterface(GameCategory mode, Type type, [MaybeNullWhen(false)] out InterfaceMappingResult registrations)
        {
            MaybeBlock(type);
            return MetaInterfaceMapping.Instance.TryGetRegistrationsForInterface(mode, type, out registrations);
        }

        public bool TryGetRegistrationsForInterface(Type type, [MaybeNullWhen(false)] out InterfaceMappingResult registrations)
        {
            MaybeBlock(type);
            return MetaInterfaceMapping.Instance.TryGetRegistrationsForInterface(type, out registrations);
        }

        public bool TryGetRegistrationsForInterface(GameCategory mode, string fullName, [MaybeNullWhen(false)] out InterfaceMappingResult registrations)
        {
            return MetaInterfaceMapping.Instance.TryGetRegistrationsForInterface(mode, fullName, out registrations);
        }

        public bool TryGetRegistrationsForInterface(string fullName, [MaybeNullWhen(false)] out InterfaceMappingResult registrations)
        {
            return MetaInterfaceMapping.Instance.TryGetRegistrationsForInterface(fullName, out registrations);
        }
    }

    private static (SkyrimMod Master, SkyrimMod Plugin, Armor MasterArmor, Armor PluginArmor) TypicalMods()
    {
        var master = new SkyrimMod(ModKey.FromNameAndExtension("Master.esm"), SkyrimRelease.SkyrimSE);
        var plugin = new SkyrimMod(ModKey.FromNameAndExtension("Plugin.esp"), SkyrimRelease.SkyrimSE);
        var masterArmor = master.Armors.AddNew();
        var pluginArmor = plugin.Armors.AddNew();
        return (master, plugin, masterArmor, pluginArmor);
    }

    private static Func<FormKey, Type, bool> GetLookup(LookupKind kind, SkyrimMod master, SkyrimMod plugin, LinkCachePreferences prefs)
    {
        var loadOrder = new LoadOrder<ISkyrimModGetter>(master.AsEnumerable<ISkyrimModGetter>().And(plugin));
        switch (kind)
        {
            case LookupKind.LoadOrderRecord:
            {
                var cache = loadOrder.ToUntypedImmutableLinkCache(prefs);
                return (formKey, type) => cache.TryResolve(formKey, type, out _);
            }
            case LookupKind.LoadOrderContext:
            {
                var cache = loadOrder.ToImmutableLinkCache<ISkyrimMod, ISkyrimModGetter>(prefs);
                return (formKey, type) => cache.TryResolveContext(formKey, type, out _);
            }
            case LookupKind.LoadOrderSimpleContext:
            {
                var cache = loadOrder.ToUntypedImmutableLinkCache(prefs);
                return (formKey, type) => cache.TryResolveSimpleContext(formKey, type, out _);
            }
            case LookupKind.ModRecord:
            {
                var cache = master.ToUntypedImmutableLinkCache(prefs);
                return (formKey, type) => cache.TryResolve(formKey, type, out _);
            }
            case LookupKind.ModContext:
            {
                var cache = master.ToImmutableLinkCache<ISkyrimMod, ISkyrimModGetter>(prefs);
                return (formKey, type) => cache.TryResolveContext(formKey, type, out _);
            }
            case LookupKind.ModSimpleContext:
            {
                var cache = master.ToUntypedImmutableLinkCache(prefs);
                return (formKey, type) => cache.TryResolveSimpleContext(formKey, type, out _);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    [Theory]
    [InlineData(LookupKind.LoadOrderRecord)]
    [InlineData(LookupKind.LoadOrderContext)]
    [InlineData(LookupKind.LoadOrderSimpleContext)]
    [InlineData(LookupKind.ModRecord)]
    [InlineData(LookupKind.ModContext)]
    [InlineData(LookupKind.ModSimpleContext)]
    public async Task WarmLookupDoesNotWaitForNewTypeRegistration(LookupKind kind)
    {
        var (master, plugin, masterArmor, _) = TypicalMods();
        var mapGetter = new BlockingInterfaceMapGetter();
        var lookup = GetLookup(kind, master, plugin, new LinkCachePreferences()
        {
            MetaInterfaceMapGetterOverride = mapGetter
        });

        // Warm up the armor type
        lookup(masterArmor.FormKey, typeof(IArmorGetter)).ShouldBeTrue();

        // Another thread is first to query an interface type, and is held while it is being registered
        mapGetter.BlockOn = typeof(IItemGetter);
        var newTypeQuery = Task.Run(() => lookup(masterArmor.FormKey, typeof(IItemGetter)));
        try
        {
            mapGetter.Entered.Wait(Timeout).ShouldBeTrue();

            // A lookup of the already known type should not have to wait for that
            var warmQuery = Task.Run(() => lookup(masterArmor.FormKey, typeof(IArmorGetter)));
            (await warmQuery.WaitAsync(Timeout)).ShouldBeTrue();
        }
        finally
        {
            mapGetter.Release.Set();
        }

        (await newTypeQuery.WaitAsync(Timeout)).ShouldBeTrue();
    }

    private static object GetPrivateField(object obj, string name)
    {
        for (var type = obj.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null) return field.GetValue(obj)!;
        }
        throw new ArgumentException($"No field {name} on {obj.GetType()}");
    }

    private static bool IsDone<TValue>(object category, string dictionaryName, Type type)
    {
        // The load order type maps also hold the type their caches are filled with
        var caches = (IReadOnlyDictionary<Type, (DepthCache<FormKey, TValue> Cache, Type EnumerationType)>)GetPrivateField(category, dictionaryName);
        return caches[type].Cache.Done;
    }

    [Theory]
    [InlineData(LookupKind.LoadOrderRecord)]
    [InlineData(LookupKind.LoadOrderContext)]
    [InlineData(LookupKind.LoadOrderSimpleContext)]
    public void FullyFilledTypeCacheIsDoneAfterOnlyHits(LookupKind kind)
    {
        var (master, plugin, masterArmor, pluginArmor) = TypicalMods();
        var loadOrder = new LoadOrder<ISkyrimModGetter>(master.AsEnumerable<ISkyrimModGetter>().And(plugin));
        Func<bool> isDone;
        Func<FormKey, bool> lookup;
        switch (kind)
        {
            case LookupKind.LoadOrderRecord:
            {
                var cache = loadOrder.ToUntypedImmutableLinkCache();
                var category = GetPrivateField(GetPrivateField(cache, "_cache"), "_formKeyCache");
                isDone = () => IsDone<LinkCacheItem>(category, "_winningRecords", typeof(IArmorGetter));
                lookup = formKey => cache.TryResolve<IArmorGetter>(formKey, out _);
                break;
            }
            case LookupKind.LoadOrderContext:
            {
                var cache = loadOrder.ToImmutableLinkCache<ISkyrimMod, ISkyrimModGetter>();
                var category = GetPrivateField(cache, "_formKeyContextCache");
                isDone = () => IsDone<IModContext<ISkyrimMod, ISkyrimModGetter, IMajorRecord, IMajorRecordGetter>>(category, "_winningContexts", typeof(IArmorGetter));
                lookup = formKey => cache.TryResolveContext(formKey, typeof(IArmorGetter), out _);
                break;
            }
            case LookupKind.LoadOrderSimpleContext:
            {
                var cache = loadOrder.ToUntypedImmutableLinkCache();
                var category = GetPrivateField(cache, "_formKeyContexts");
                isDone = () => IsDone<IModContext<IMajorRecordGetter>>(category, "_winningContexts", typeof(IArmorGetter));
                lookup = formKey => cache.TryResolveSimpleContext(formKey, typeof(IArmorGetter), out _);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        // Fills the plugin, then the master, and finds the record in the last mod processed
        lookup(pluginArmor.FormKey).ShouldBeTrue();
        isDone().ShouldBeFalse();
        lookup(masterArmor.FormKey).ShouldBeTrue();

        // Every mod is processed, so later hits can skip locking without needing a miss first
        isDone().ShouldBeTrue();
        lookup(masterArmor.FormKey).ShouldBeTrue();
        lookup(pluginArmor.FormKey).ShouldBeTrue();
    }
}
