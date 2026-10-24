using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Exceptions;
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
    public void Direct_SimpleContextOverloads(LinkCachePreferences.RetentionType cacheType)
    {
        var prototype = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimLE, forceUseLowerFormIDRanges: false);
        var objEffect = prototype.ObjectEffects.AddNew("EDID1");
        using var disp = ConvertMod(prototype, out var mod);
        var (style, package) = GetLinkCache(mod, cacheType);
        var simpleRetriever = new SimpleContextRetriever();
        var link = objEffect.ToLink<IObjectEffectGetter>();
        var type = typeof(IObjectEffectGetter);

        WrapPotentialThrow(cacheType, style, simpleRetriever, () =>
        {
            package.ResolveAllSimpleContexts(objEffect.FormKey, type).ShouldHaveSingleItem()
                .Record.FormKey.ShouldBe(objEffect.FormKey);
        });
        WrapPotentialThrow(cacheType, style, simpleRetriever, () =>
        {
            package.ResolveAllSimpleContexts((IFormLinkIdentifier)link).ShouldHaveSingleItem()
                .Record.FormKey.ShouldBe(objEffect.FormKey);
        });
        WrapPotentialThrow(cacheType, style, simpleRetriever, () =>
        {
            package.ResolveSimpleContext<IObjectEffectGetter>(link)
                .Record.FormKey.ShouldBe(objEffect.FormKey);
        });
        WrapPotentialThrow(cacheType, style, simpleRetriever, () =>
        {
            package.TryResolveSimpleContext(objEffect.EditorID, type, out var context).ShouldBeTrue();
            context.Record.FormKey.ShouldBe(objEffect.FormKey);
        });
        WrapPotentialThrow(cacheType, style, simpleRetriever, () =>
        {
            package.ResolveSimpleContext(objEffect.FormKey).Record.FormKey.ShouldBe(objEffect.FormKey);
            package.ResolveSimpleContext(objEffect.EditorID).Record.FormKey.ShouldBe(objEffect.FormKey);
            package.ResolveSimpleContext(objEffect.FormKey, type).Record.FormKey.ShouldBe(objEffect.FormKey);
            package.ResolveSimpleContext((IFormLinkIdentifier)link).Record.FormKey.ShouldBe(objEffect.FormKey);
            package.ResolveSimpleContext(objEffect.EditorID, type).Record.FormKey.ShouldBe(objEffect.FormKey);
        });
    }

    [Theory]
    [InlineData(LinkCachePreferences.RetentionType.OnlyIdentifiers)]
    [InlineData(LinkCachePreferences.RetentionType.WholeRecord)]
    public void Direct_SimpleContextOverloadsMissing(LinkCachePreferences.RetentionType cacheType)
    {
        var prototype = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimLE, forceUseLowerFormIDRanges: false);
        var objEffect = prototype.ObjectEffects.AddNew("EDID1");
        using var disp = ConvertMod(prototype, out var mod);
        var (style, package) = GetLinkCache(mod, cacheType);
        var simpleRetriever = new SimpleContextRetriever();
        var type = typeof(IObjectEffectGetter);

        WrapPotentialThrow(cacheType, style, simpleRetriever, () =>
        {
            package.ResolveAllSimpleContexts(UnusedFormKey, type).ShouldBeEmpty();
            package.ResolveAllSimpleContexts((IFormLinkIdentifier)new FormLink<IObjectEffectGetter>(UnusedFormKey)).ShouldBeEmpty();
            package.ResolveAllSimpleContexts(objEffect.FormKey, typeof(INpcGetter)).ShouldBeEmpty();
            package.TryResolveSimpleContext(UnusedEditorID, type, out _).ShouldBeFalse();
            package.TryResolveSimpleContext(string.Empty, type, out _).ShouldBeFalse();
        });
        WrapPotentialThrow(cacheType, style, simpleRetriever, () =>
        {
            Assert.Throws<MissingRecordException>(() => package.ResolveSimpleContext(UnusedFormKey));
            Assert.Throws<MissingRecordException>(() => package.ResolveSimpleContext(UnusedEditorID));
            Assert.Throws<MissingRecordException>(() => package.ResolveSimpleContext(UnusedFormKey, type));
            Assert.Throws<MissingRecordException>(() => package.ResolveSimpleContext(UnusedEditorID, type));
            Assert.Throws<MissingRecordException>(() => package.ResolveSimpleContext<IObjectEffectGetter>(new FormLink<IObjectEffectGetter>(UnusedFormKey)));
        });
    }
}
