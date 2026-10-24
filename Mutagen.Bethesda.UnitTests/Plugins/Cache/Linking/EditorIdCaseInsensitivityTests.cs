using Shouldly;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Cache.Internals.Implementations;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Xunit;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Mutagen.Bethesda.UnitTests.Plugins.Cache.Linking;

public class EditorIdCaseInsensitivityTests
{
    private const string EditorId = "EditorID1";

    public static IEnumerable<object[]> Spellings => new[]
    {
        new object[] { EditorId.ToUpperInvariant() },
        new object[] { EditorId.ToLowerInvariant() },
    };

    private static void AssertAllQueryFormsFind(ILinkCache cache, string editorId, IMajorRecordGetter expected)
    {
        cache.TryResolve<IObjectEffectGetter>(editorId, out var typed).ShouldBeTrue();
        typed.ShouldBeSameAs(expected);
        cache.TryResolve<IEffectRecordGetter>(editorId, out var linkInterface).ShouldBeTrue();
        linkInterface.ShouldBeSameAs(expected);
        cache.TryResolve<INamedGetter>(editorId, out var aspectInterface).ShouldBeTrue();
        aspectInterface.ShouldBeSameAs(expected);
        cache.TryResolve(editorId, typeof(IObjectEffectGetter), out var byType).ShouldBeTrue();
        byType.ShouldBeSameAs(expected);
        cache.TryResolve(editorId, out var untyped).ShouldBeTrue();
        untyped.ShouldBeSameAs(expected);
        cache.TryResolveIdentifier<IObjectEffectGetter>(editorId, out var typedFormKey).ShouldBeTrue();
        typedFormKey.ShouldBe(expected.FormKey);
        cache.TryResolveIdentifier(editorId, out var untypedFormKey).ShouldBeTrue();
        untypedFormKey.ShouldBe(expected.FormKey);
        cache.TryResolveSimpleContext<IObjectEffectGetter>(editorId, out var simpleContext).ShouldBeTrue();
        simpleContext.Record.ShouldBeSameAs(expected);
        cache.TryResolveSimpleContext(editorId, out var untypedSimpleContext).ShouldBeTrue();
        untypedSimpleContext.Record.ShouldBeSameAs(expected);
    }

    private static void AssertAllQueryFormsFind(ILinkCache<ISkyrimMod, ISkyrimModGetter> cache, string editorId, IMajorRecordGetter expected)
    {
        AssertAllQueryFormsFind((ILinkCache)cache, editorId, expected);
        cache.TryResolveContext<IObjectEffect, IObjectEffectGetter>(editorId, out var context).ShouldBeTrue();
        context.Record.ShouldBeSameAs(expected);
        cache.TryResolveContext(editorId, out var untypedContext).ShouldBeTrue();
        untypedContext.Record.ShouldBeSameAs(expected);
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public void ImmutableModCacheMatchesAnyCase(string editorId)
    {
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var objEffect = mod.ObjectEffects.AddNew(EditorId);
        AssertAllQueryFormsFind(new ImmutableModLinkCache(mod), editorId, objEffect);
        AssertAllQueryFormsFind(mod.ToImmutableLinkCache<ISkyrimMod, ISkyrimModGetter>(), editorId, objEffect);
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public void MutableModCacheMatchesAnyCase(string editorId)
    {
        var mod = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var objEffect = mod.ObjectEffects.AddNew(EditorId);
        AssertAllQueryFormsFind(new MutableModLinkCache(mod), editorId, objEffect);
        AssertAllQueryFormsFind(new MutableModLinkCache<ISkyrimMod, ISkyrimModGetter>(mod), editorId, objEffect);
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public void MutableLoadOrderCacheReturnsMutableOverrideForAnyCase(string editorId)
    {
        var master = new SkyrimMod(TestConstants.MasterModKey, SkyrimRelease.SkyrimSE);
        var masterRec = master.ObjectEffects.AddNew(EditorId);
        var mutablePlugin = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var overrideRec = mutablePlugin.ObjectEffects.GetOrAddAsOverride(masterRec);

        var immutableBase = new LoadOrder<ISkyrimModGetter>() { master }
            .ToImmutableLinkCache<ISkyrimMod, ISkyrimModGetter>();
        var cache = new MutableLoadOrderLinkCache<ISkyrimMod, ISkyrimModGetter>(immutableBase, mutablePlugin);

        AssertAllQueryFormsFind(cache, editorId, overrideRec);
    }
}
