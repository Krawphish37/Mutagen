using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
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
    public void DirectEmpty(LinkCachePreferences.RetentionType cacheType)
    {
        using var disp = ConvertMod(new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimLE), out var mod);
        var (style, package) = GetLinkCache(mod, cacheType);

        // Test FormKey fails
        Assert.False(package.TryResolve(UnusedFormKey, out var _));
        Assert.False(package.TryResolve(FormKey.Null, out var _));
        Assert.False(package.TryResolve<IMajorRecordGetter>(UnusedFormKey, out var _));
        Assert.False(package.TryResolve<IMajorRecordGetter>(FormKey.Null, out var _));
        Assert.False(package.TryResolve<ISkyrimMajorRecordGetter>(UnusedFormKey, out var _));
        Assert.False(package.TryResolve<ISkyrimMajorRecordGetter>(FormKey.Null, out var _));
        Assert.False(package.TryResolve<IObjectEffectGetter>(UnusedFormKey, out var _));
        Assert.False(package.TryResolve<IObjectEffectGetter>(FormKey.Null, out var _));
        Assert.False(package.TryResolve<IObjectEffect>(UnusedFormKey, out var _));
        Assert.False(package.TryResolve<IObjectEffect>(FormKey.Null, out var _));
        Assert.False(package.TryResolve<ObjectEffect>(UnusedFormKey, out var _));
        Assert.False(package.TryResolve<ObjectEffect>(FormKey.Null, out var _));
        Assert.False(package.TryResolve<IEffectRecord>(UnusedFormKey, out var _));
        Assert.False(package.TryResolve<IEffectRecord>(FormKey.Null, out var _));
        Assert.False(package.TryResolve<IEffectRecordGetter>(UnusedFormKey, out var _));
        Assert.False(package.TryResolve<IEffectRecordGetter>(FormKey.Null, out var _));

        // Test EditorID fails
        Assert.False(package.TryResolve(UnusedEditorID, out var _));
        Assert.False(package.TryResolve(string.Empty, out var _));
        Assert.False(package.TryResolve<IMajorRecordGetter>(UnusedEditorID, out var _));
        Assert.False(package.TryResolve<IMajorRecordGetter>(string.Empty, out var _));
        Assert.False(package.TryResolve<ISkyrimMajorRecordGetter>(UnusedEditorID, out var _));
        Assert.False(package.TryResolve<ISkyrimMajorRecordGetter>(string.Empty, out var _));
        Assert.False(package.TryResolve<IObjectEffectGetter>(UnusedEditorID, out var _));
        Assert.False(package.TryResolve<IObjectEffectGetter>(string.Empty, out var _));
        Assert.False(package.TryResolve<IObjectEffect>(UnusedEditorID, out var _));
        Assert.False(package.TryResolve<IObjectEffect>(string.Empty, out var _));
        Assert.False(package.TryResolve<ObjectEffect>(UnusedEditorID, out var _));
        Assert.False(package.TryResolve<ObjectEffect>(string.Empty, out var _));
        Assert.False(package.TryResolve<IEffectRecord>(UnusedEditorID, out var _));
        Assert.False(package.TryResolve<IEffectRecord>(string.Empty, out var _));
        Assert.False(package.TryResolve<IEffectRecordGetter>(UnusedEditorID, out var _));
        Assert.False(package.TryResolve<IEffectRecordGetter>(string.Empty, out var _));
    }

    [Theory]
    [InlineData(LinkCachePreferences.RetentionType.OnlyIdentifiers)]
    [InlineData(LinkCachePreferences.RetentionType.WholeRecord)]
    public void DirectNoMatch(LinkCachePreferences.RetentionType cacheType)
    {
        var prototype = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimLE);
        prototype.ObjectEffects.AddNew();
        using var disp = ConvertMod(prototype, out var mod);
        var (style, package) = GetLinkCache(mod, cacheType);

        // Test FormKey fails
        Assert.False(package.TryResolve(UnusedFormKey, out var _));
        Assert.False(package.TryResolve(FormKey.Null, out var _));
        Assert.False(package.TryResolve<IMajorRecordGetter>(UnusedFormKey, out var _));
        Assert.False(package.TryResolve<IMajorRecordGetter>(FormKey.Null, out var _));
        Assert.False(package.TryResolve<ISkyrimMajorRecordGetter>(UnusedFormKey, out var _));
        Assert.False(package.TryResolve<ISkyrimMajorRecordGetter>(FormKey.Null, out var _));
        Assert.False(package.TryResolve<IObjectEffectGetter>(UnusedFormKey, out var _));
        Assert.False(package.TryResolve<IObjectEffectGetter>(FormKey.Null, out var _));
        Assert.False(package.TryResolve<IObjectEffect>(UnusedFormKey, out var _));
        Assert.False(package.TryResolve<IObjectEffect>(FormKey.Null, out var _));
        Assert.False(package.TryResolve<ObjectEffect>(UnusedFormKey, out var _));
        Assert.False(package.TryResolve<ObjectEffect>(FormKey.Null, out var _));
        Assert.False(package.TryResolve<IEffectRecord>(UnusedFormKey, out var _));
        Assert.False(package.TryResolve<IEffectRecord>(FormKey.Null, out var _));
        Assert.False(package.TryResolve<IEffectRecordGetter>(UnusedFormKey, out var _));
        Assert.False(package.TryResolve<IEffectRecordGetter>(FormKey.Null, out var _));

        // Test EditorID fails
        Assert.False(package.TryResolve(UnusedEditorID, out var _));
        Assert.False(package.TryResolve(string.Empty, out var _));
        Assert.False(package.TryResolve<IMajorRecordGetter>(UnusedEditorID, out var _));
        Assert.False(package.TryResolve<IMajorRecordGetter>(string.Empty, out var _));
        Assert.False(package.TryResolve<ISkyrimMajorRecordGetter>(UnusedEditorID, out var _));
        Assert.False(package.TryResolve<ISkyrimMajorRecordGetter>(string.Empty, out var _));
        Assert.False(package.TryResolve<IObjectEffectGetter>(UnusedEditorID, out var _));
        Assert.False(package.TryResolve<IObjectEffectGetter>(string.Empty, out var _));
        Assert.False(package.TryResolve<IObjectEffect>(UnusedEditorID, out var _));
        Assert.False(package.TryResolve<IObjectEffect>(string.Empty, out var _));
        Assert.False(package.TryResolve<ObjectEffect>(UnusedEditorID, out var _));
        Assert.False(package.TryResolve<ObjectEffect>(string.Empty, out var _));
        Assert.False(package.TryResolve<IEffectRecord>(UnusedEditorID, out var _));
        Assert.False(package.TryResolve<IEffectRecord>(string.Empty, out var _));
        Assert.False(package.TryResolve<IEffectRecordGetter>(UnusedEditorID, out var _));
        Assert.False(package.TryResolve<IEffectRecordGetter>(string.Empty, out var _));
    }

    [Theory]
    [InlineData(LinkCachePreferences.RetentionType.OnlyIdentifiers)]
    [InlineData(LinkCachePreferences.RetentionType.WholeRecord)]
    public void DirectTypical(LinkCachePreferences.RetentionType cacheType)
    {
        var prototype = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimLE, forceUseLowerFormIDRanges: false);
        var objEffect1 = prototype.ObjectEffects.AddNew("EDID1");
        var objEffect2 = prototype.ObjectEffects.AddNew("EDID2");
        using var disp = ConvertMod(prototype, out var mod);
        var (style, package) = GetLinkCache(mod, cacheType);

        // Do linked interfaces first, as this tests a specific edge case
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<IEffectRecordGetter>(objEffect1.FormKey, out var rec));
            Assert.Equal(rec.FormKey, objEffect1.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<IEffectRecordGetter>(objEffect1.EditorID, out var rec));
            Assert.Equal(rec.FormKey, objEffect1.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<IEffectRecordGetter>(objEffect2.FormKey, out var rec));
            Assert.Equal(rec.FormKey, objEffect2.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<IEffectRecordGetter>(objEffect2.EditorID, out var rec));
            Assert.Equal(rec.FormKey, objEffect2.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve(objEffect1.FormKey, out var rec));
            Assert.Equal(rec.FormKey, objEffect1.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve(objEffect1.EditorID, out var rec));
            Assert.Equal(rec.FormKey, objEffect1.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve(objEffect2.FormKey, out var rec));
            Assert.Equal(rec.FormKey, objEffect2.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve(objEffect2.EditorID, out var rec));
            Assert.Equal(rec.FormKey, objEffect2.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<IMajorRecordGetter>(objEffect1.FormKey, out var rec));
            Assert.Equal(rec.FormKey, objEffect1.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<IMajorRecordGetter>(objEffect1.EditorID, out var rec));
            Assert.Equal(rec.FormKey, objEffect1.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<IMajorRecordGetter>(objEffect2.FormKey, out var rec));
            Assert.Equal(rec.FormKey, objEffect2.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<IMajorRecordGetter>(objEffect2.EditorID, out var rec));
            Assert.Equal(rec.FormKey, objEffect2.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<ISkyrimMajorRecordGetter>(objEffect1.FormKey, out var rec));
            Assert.Equal(rec.FormKey, objEffect1.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<ISkyrimMajorRecordGetter>(objEffect1.EditorID, out var rec));
            Assert.Equal(rec.FormKey, objEffect1.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<ISkyrimMajorRecordGetter>(objEffect2.FormKey, out var rec));
            Assert.Equal(rec.FormKey, objEffect2.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<ISkyrimMajorRecordGetter>(objEffect2.EditorID, out var rec));
            Assert.Equal(rec.FormKey, objEffect2.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<IObjectEffectGetter>(objEffect1.FormKey, out var rec));
            Assert.Equal(rec.FormKey, objEffect1.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<IObjectEffectGetter>(objEffect1.EditorID, out var rec));
            Assert.Equal(rec.FormKey, objEffect1.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<IObjectEffectGetter>(objEffect2.FormKey, out var rec));
            Assert.Equal(rec.FormKey, objEffect2.FormKey);
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<IObjectEffectGetter>(objEffect2.EditorID, out var rec));
            Assert.Equal(rec.FormKey, objEffect2.FormKey);
        });
        if (ReadOnly)
        {
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.False(package.TryResolve<ObjectEffect>(objEffect1.FormKey, out var _));
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.False(package.TryResolve<ObjectEffect>(objEffect2.FormKey, out var _));
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.False(package.TryResolve<IObjectEffect>(objEffect1.FormKey, out var _));
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.False(package.TryResolve<IObjectEffect>(objEffect2.FormKey, out var _));
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.False(package.TryResolve<IEffectRecord>(objEffect1.FormKey, out var _));
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.False(package.TryResolve<IEffectRecord>(objEffect2.FormKey, out var _));
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.False(package.TryResolve<ObjectEffect>(objEffect1.EditorID, out var _));
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.False(package.TryResolve<ObjectEffect>(objEffect2.EditorID, out var _));
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.False(package.TryResolve<IObjectEffect>(objEffect1.EditorID, out var _));
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.False(package.TryResolve<IObjectEffect>(objEffect2.EditorID, out var _));
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.False(package.TryResolve<IEffectRecord>(objEffect1.EditorID, out var _));
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.False(package.TryResolve<IEffectRecord>(objEffect2.EditorID, out var _));
            });

            Assert.False(package.TryResolve<ObjectEffect>(TestConstants.UnusedForm, out var _));
            Assert.False(package.TryResolve<IObjectEffect>(TestConstants.UnusedForm, out var _));
            Assert.False(package.TryResolve<IEffectRecord>(TestConstants.UnusedForm, out var _));
            Assert.False(package.TryResolve<ObjectEffect>(TestConstants.UnusedEdid, out var _));
            Assert.False(package.TryResolve<IObjectEffect>(TestConstants.UnusedEdid, out var _));
            Assert.False(package.TryResolve<IEffectRecord>(TestConstants.UnusedEdid, out var _));
        }
        else
        {
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(package.TryResolve<ObjectEffect>(objEffect1.FormKey, out var rec));
                Assert.Equal(rec.FormKey, objEffect1.FormKey);
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(package.TryResolve<ObjectEffect>(objEffect1.EditorID, out var rec));
                Assert.Equal(rec.FormKey, objEffect1.FormKey);
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(package.TryResolve<ObjectEffect>(objEffect2.FormKey, out var rec));
                Assert.Equal(rec.FormKey, objEffect2.FormKey);
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(package.TryResolve<ObjectEffect>(objEffect2.EditorID, out var rec));
                Assert.Equal(rec.FormKey, objEffect2.FormKey);
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(package.TryResolve<IObjectEffect>(objEffect1.FormKey, out var rec));
                Assert.Equal(rec.FormKey, objEffect1.FormKey);
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(package.TryResolve<IObjectEffect>(objEffect1.EditorID, out var rec));
                Assert.Equal(rec.FormKey, objEffect1.FormKey);
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(package.TryResolve<IObjectEffect>(objEffect2.FormKey, out var rec));
                Assert.Equal(rec.FormKey, objEffect2.FormKey);
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(package.TryResolve<IObjectEffect>(objEffect2.EditorID, out var rec));
                Assert.Equal(rec.FormKey, objEffect2.FormKey);
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(package.TryResolve<IEffectRecord>(objEffect1.FormKey, out var rec));
                Assert.Equal(rec.FormKey, objEffect1.FormKey);
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(package.TryResolve<IEffectRecord>(objEffect1.EditorID, out var rec));
                Assert.Equal(rec.FormKey, objEffect1.FormKey);
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(package.TryResolve<IEffectRecord>(objEffect2.FormKey, out var rec));
                Assert.Equal(rec.FormKey, objEffect2.FormKey);
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(package.TryResolve<IEffectRecord>(objEffect2.EditorID, out var rec));
                Assert.Equal(rec.FormKey, objEffect2.FormKey);
            });
        }
    }

    [Theory]
    [InlineData(LinkCachePreferences.RetentionType.OnlyIdentifiers)]
    [InlineData(LinkCachePreferences.RetentionType.WholeRecord)]
    public void DirectReadOnlyMechanics(LinkCachePreferences.RetentionType cacheType)
    {
        var wrapper = SkyrimMod.CreateFromBinaryOverlay(TestDataPathing.SkyrimTestMod, SkyrimRelease.SkyrimSE);
        var (style, package) = GetLinkCache(wrapper, cacheType);
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<INpcGetter>(TestFileFormKey, out var rec));
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<INpcGetter>(TestFileEditorID, out var rec));
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.False(package.TryResolve<INpc>(TestFileFormKey, out var rec));
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.False(package.TryResolve<INpc>(TestFileEditorID, out var rec));
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.False(package.TryResolve<Npc>(TestFileFormKey, out var rec));
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.False(package.TryResolve<Npc>(TestFileEditorID, out var rec));
        });
    }

    [Theory]
    [InlineData(LinkCachePreferences.RetentionType.OnlyIdentifiers)]
    [InlineData(LinkCachePreferences.RetentionType.WholeRecord)]
    public void DirectReadOnlyMechanicsClassFormFirst(LinkCachePreferences.RetentionType cacheType)
    {
        var wrapper = SkyrimMod.CreateFromBinaryOverlay(TestDataPathing.SkyrimTestMod, SkyrimRelease.SkyrimSE);
        var (style, package) = GetLinkCache(wrapper, cacheType);
        // Query the class and setter forms first; they share one cache with the getter form
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.False(package.TryResolve<Npc>(TestFileFormKey, out var rec));
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.False(package.TryResolve<INpc>(TestFileEditorID, out var rec));
        });
        package.TryResolveIdentifier<Npc>(TestFileFormKey, out _);
        package.TryResolveIdentifier<INpc>(TestFileEditorID, out _);

        // The getter form still sees the read-only records
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<INpcGetter>(TestFileFormKey, out var rec));
        });
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<INpcGetter>(TestFileEditorID, out var rec));
        });
        Assert.True(package.TryResolveIdentifier<INpcGetter>(TestFileFormKey, out var edid));
        Assert.Equal(TestFileEditorID, edid);
        Assert.True(package.TryResolveIdentifier<INpcGetter>(TestFileEditorID, out var formKey));
        Assert.Equal(TestFileFormKey, formKey);
        Assert.Equal(
            new[] { TestFileFormKey, TestFileFormKey2 }.OrderBy(x => x.ID),
            package.AllIdentifiers<INpcGetter>().Select(x => x.FormKey).OrderBy(x => x.ID));
    }

    [Theory]
    [InlineData(LinkCachePreferences.RetentionType.OnlyIdentifiers)]
    [InlineData(LinkCachePreferences.RetentionType.WholeRecord)]
    public void EditorIdCaseDifferences(LinkCachePreferences.RetentionType cacheType)
    {
        var wrapper = SkyrimMod.CreateFromBinaryOverlay(TestDataPathing.SkyrimTestMod, SkyrimRelease.SkyrimSE);
        var (style, package) = GetLinkCache(wrapper, cacheType);
        WrapPotentialThrow(cacheType, style, null, () =>
        {
            Assert.True(package.TryResolve<INpcGetter>(TestFileEditorID.ToLower(), out var rec));
        });
    }

    [Theory]
    [InlineData(LinkCachePreferences.RetentionType.OnlyIdentifiers)]
    [InlineData(LinkCachePreferences.RetentionType.WholeRecord)]
    public void EditorIdCaseDifferencesAllQueryForms(LinkCachePreferences.RetentionType cacheType)
    {
        var prototype = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimLE, forceUseLowerFormIDRanges: false);
        var objEffect = prototype.ObjectEffects.AddNew("EditorID1");
        using var disp = ConvertMod(prototype, out var mod);
        var (style, package) = GetLinkCache(mod, cacheType);

        foreach (var editorId in new[] { objEffect.EditorID.ToUpperInvariant(), objEffect.EditorID.ToLowerInvariant() })
        {
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(TryTest<IEffectRecord, IEffectRecordGetter>(package, editorId, out var rec));
                Assert.Equal(objEffect.FormKey, rec.FormKey);
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(TryTest<INamed, INamedGetter>(package, editorId, out var rec));
                Assert.Equal(objEffect.FormKey, ((IMajorRecordGetter)rec).FormKey);
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(package.TryResolve(editorId, out var rec));
                Assert.Equal(objEffect.FormKey, rec.FormKey);
            });
            if (cacheType == LinkCachePreferences.RetentionType.WholeRecord)
            {
                Assert.True(package.TryResolveContext(editorId, out var context));
                Assert.Equal(objEffect.FormKey, context.Record.FormKey);
            }
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(TryTest<IMajorRecord, IMajorRecordGetter>(package, editorId, out var rec));
                Assert.Equal(objEffect.FormKey, rec.FormKey);
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(TryTest<IObjectEffect, IObjectEffectGetter>(package, editorId, out var rec));
                Assert.Equal(objEffect.FormKey, rec.FormKey);
            });
            WrapPotentialThrow(cacheType, style, null, () =>
            {
                Assert.True(package.TryResolve(editorId, typeof(IObjectEffectGetter), out var rec));
                Assert.Equal(objEffect.FormKey, rec.FormKey);
            });
            WrapPotentialThrow(cacheType, style, new SimpleContextRetriever(), () =>
            {
                Assert.True(package.TryResolveSimpleContext<IObjectEffectGetter>(editorId, out var context));
                Assert.Equal(objEffect.FormKey, context.Record.FormKey);
            });
            WrapPotentialThrow(cacheType, style, new SimpleContextRetriever(), () =>
            {
                Assert.True(package.TryResolveSimpleContext(editorId, out var context));
                Assert.Equal(objEffect.FormKey, context.Record.FormKey);
            });
            Assert.True(package.TryResolveIdentifier(editorId, out var formKey));
            Assert.Equal(objEffect.FormKey, formKey);
            Assert.True(package.TryResolveIdentifier<IObjectEffectGetter>(editorId, out formKey));
            Assert.Equal(objEffect.FormKey, formKey);
        }
    }
}