using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Mutagen.Bethesda.UnitTests.Plugins.Cache.Linking.Helpers;
using Xunit;

#nullable disable
#pragma warning disable CS0618 // Type or member is obsolete

namespace Mutagen.Bethesda.UnitTests.Plugins.Cache.Linking;

public partial class ALinkingTests
{
    [Theory]
    [InlineData(LinkCachePreferences.RetentionType.OnlyIdentifiers)]
    [InlineData(LinkCachePreferences.RetentionType.WholeRecord)]
    public void Direct_UntypedContext(LinkCachePreferences.RetentionType cacheType)
    {
        var prototype = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimLE, forceUseLowerFormIDRanges: false);
        var objEffect = prototype.ObjectEffects.AddNew("EDID1");
        using var disp = ConvertMod(prototype, out var mod);
        var (style, package) = GetLinkCache(mod, cacheType);

        WrapPotentialThrow(cacheType, style, () =>
        {
            package.TryResolveContext(objEffect.FormKey, out var context).ShouldBeTrue();
            context.Record.FormKey.ShouldBe(objEffect.FormKey);
        });
        WrapPotentialThrow(cacheType, style, () =>
        {
            package.TryResolveContext(objEffect.EditorID, out var context).ShouldBeTrue();
            context.Record.FormKey.ShouldBe(objEffect.FormKey);
        });
        WrapPotentialThrow(cacheType, style, () =>
        {
            package.ResolveAllContexts(objEffect.FormKey).ShouldHaveSingleItem()
                .Record.FormKey.ShouldBe(objEffect.FormKey);
        });
    }

    [Theory]
    [InlineData(LinkCachePreferences.RetentionType.OnlyIdentifiers)]
    [InlineData(LinkCachePreferences.RetentionType.WholeRecord)]
    public void Direct_UntypedSimpleContext(LinkCachePreferences.RetentionType cacheType)
    {
        var prototype = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimLE, forceUseLowerFormIDRanges: false);
        var objEffect = prototype.ObjectEffects.AddNew("EDID1");
        using var disp = ConvertMod(prototype, out var mod);
        var (style, package) = GetLinkCache(mod, cacheType);
        var simpleRetriever = new SimpleContextRetriever();

        WrapPotentialThrow(cacheType, style, simpleRetriever, () =>
        {
            package.TryResolveSimpleContext(objEffect.FormKey, out var context).ShouldBeTrue();
            context.Record.FormKey.ShouldBe(objEffect.FormKey);
            context.ModKey.ShouldBe(TestConstants.PluginModKey);
        });
        WrapPotentialThrow(cacheType, style, simpleRetriever, () =>
        {
            package.TryResolveSimpleContext(objEffect.EditorID, out var context).ShouldBeTrue();
            context.Record.FormKey.ShouldBe(objEffect.FormKey);
        });
        WrapPotentialThrow(cacheType, style, simpleRetriever, () =>
        {
            package.ResolveAllSimpleContexts(objEffect.FormKey).ShouldHaveSingleItem()
                .Record.FormKey.ShouldBe(objEffect.FormKey);
        });
        WrapPotentialThrow(cacheType, style, simpleRetriever, () =>
        {
            package.TryResolveSimpleContext(UnusedFormKey, out _).ShouldBeFalse();
            package.TryResolveSimpleContext(FormKey.Null, out _).ShouldBeFalse();
            package.TryResolveSimpleContext(UnusedEditorID, out _).ShouldBeFalse();
            package.TryResolveSimpleContext(string.Empty, out _).ShouldBeFalse();
            package.TryResolveSimpleContext(objEffect.FormKey, out _, ResolveTarget.Origin).ShouldBeTrue();
        });
    }
}
