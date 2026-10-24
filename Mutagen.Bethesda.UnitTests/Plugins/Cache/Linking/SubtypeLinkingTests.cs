using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Noggog;
using Xunit;

#nullable disable
#pragma warning disable CS0618 // Type or member is obsolete

namespace Mutagen.Bethesda.UnitTests.Plugins.Cache.Linking;

public partial class ALinkingTests
{
    [Theory]
    [InlineData(LinkCachePreferences.RetentionType.OnlyIdentifiers)]
    [InlineData(LinkCachePreferences.RetentionType.WholeRecord)]
    public void SubtypeLinking_Typical(LinkCachePreferences.RetentionType cacheType)
    {
        var prototype = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimLE);
        var armor = prototype.Armors.AddNew();
        var llist = prototype.LeveledItems.AddNew();
        llist.Entries = new ExtendedList<LeveledItemEntry>()
        {
            new LeveledItemEntry()
            {
                Data = new LeveledItemEntryData()
                {
                    Reference = armor.AsLink()
                }
            }
        };
        using var disp = ConvertMod(prototype, out var mod);
        var (style, package) = GetLinkCache(mod, cacheType);
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(mod.LeveledItems.First().Entries[0].Data.Reference.TryResolve(package, out IArmorGetter armorGetterLink));
        });
    }

    [Theory]
    [InlineData(LinkCachePreferences.RetentionType.OnlyIdentifiers)]
    [InlineData(LinkCachePreferences.RetentionType.WholeRecord)]
    public void SubtypeLinking_InheritingSiblingNotResolved(LinkCachePreferences.RetentionType cacheType)
    {
        var prototype = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimLE);
        var globalInt = prototype.Globals.AddNewInt();
        var globalFloat = prototype.Globals.AddNewFloat();
        using var disp = ConvertMod(prototype, out var mod);
        var loadOrder = new LoadOrder<ISkyrimModGetter>();
        loadOrder.Add(mod);
        foreach (var (style, package) in new[] { GetLinkCache(mod, cacheType), GetLinkCache(loadOrder, cacheType) })
        {
            Assert.Equal(globalInt.FormKey, Assert.Single(package.AllIdentifiers<IGlobalIntGetter>()).FormKey);
            Assert.Equal(globalFloat.FormKey, Assert.Single(package.AllIdentifiers(typeof(IGlobalFloatGetter))).FormKey);
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(package.TryResolve(globalInt.FormKey, typeof(IGlobalIntGetter), out var rec));
                Assert.IsAssignableFrom<IGlobalIntGetter>(rec);
                Assert.False(package.TryResolve(globalFloat.FormKey, typeof(IGlobalIntGetter), out _));
                Assert.Empty(package.ResolveAll<IGlobalIntGetter>(globalFloat.FormKey));
                Assert.False(package.TryResolveContext(globalFloat.FormKey, typeof(IGlobalIntGetter), out _));
            });
        }
    }
}