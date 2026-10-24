using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Mapping;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

public class KeyBaseObjectTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static StarfieldMod MakeMod(out Key key, out MiscItem misc)
    {
        var mod = new StarfieldMod(TestModKey, StarfieldRelease.Starfield);
        key = mod.Keys.AddNew("TestKey");
        misc = mod.MiscItems.AddNew("TestMisc");
        var snap = mod.SnapTemplateBehaviors.AddNew("TestSnapBehavior");
        snap.Items.Add(new SnapTemplateBehaviorItem()
        {
            BaseObject = new FormLink<IBaseObjectGetter>(key.FormKey),
        });
        snap.Items.Add(new SnapTemplateBehaviorItem()
        {
            BaseObject = new FormLink<IBaseObjectGetter>(misc.FormKey),
        });
        return mod;
    }

    private static void AssertKeyIsBaseObject(IStarfieldModGetter mod, FormKey keyKey, FormKey miscKey)
    {
        mod.EnumerateMajorRecords<IBaseObjectGetter>()
            .Select(x => x.FormKey)
            .ShouldBe(new[] { keyKey, miscKey }, ignoreOrder: true);

        var linkCache = mod.ToImmutableLinkCache();
        linkCache.TryResolve<IBaseObjectGetter>(keyKey, out var resolvedKey).ShouldBeTrue();
        resolvedKey.ShouldBeAssignableTo<IKeyGetter>();
        linkCache.TryResolve<IBaseObjectGetter>(miscKey, out _).ShouldBeTrue();

        var items = mod.SnapTemplateBehaviors.Single().Items;
        items.Count.ShouldBe(2);
        foreach (var item in items)
        {
            item.BaseObject.TryResolve(linkCache, out _).ShouldBeTrue();
        }
    }

    [Fact]
    public void KeyImplementsBaseObject()
    {
        typeof(IBaseObjectGetter).IsAssignableFrom(typeof(IKeyGetter)).ShouldBeTrue();
        typeof(IBaseObject).IsAssignableFrom(typeof(IKey)).ShouldBeTrue();
    }

    [Fact]
    public void LinkInterfaceMappingIncludesKey()
    {
        var mapping = LinkInterfaceMapping.Instance.InterfaceToObjectTypes(GameCategory.Starfield);
        mapping.TryGetValue(typeof(IBaseObjectGetter), out var result).ShouldBeTrue();
        result!.Registrations.Select(x => x.GetterType).ShouldContain(typeof(IKeyGetter));
        result.Registrations.Select(x => x.GetterType).ShouldContain(typeof(IMiscItemGetter));
    }

    [Fact]
    public void KeyIsBaseObjectInMemory()
    {
        var mod = MakeMod(out var key, out var misc);
        AssertKeyIsBaseObject(mod, key.FormKey, misc.FormKey);
    }

    [Theory, MutagenAutoData]
    public async Task KeyIsBaseObjectInOverlay(
        IFileSystem fileSystem,
        DirectoryPath existingTempDir)
    {
        var mod = MakeMod(out var key, out var misc);
        var modPath = Path.Combine(existingTempDir, TestModKey.FileName);
        await mod.BeginWrite
            .ToPath(modPath)
            .WithNoLoadOrder()
            .WithFileSystem(fileSystem)
            .WriteAsync();

        using var overlay = StarfieldMod.Create(StarfieldRelease.Starfield)
            .FromPath(modPath)
            .WithNoLoadOrder()
            .WithFileSystem(fileSystem)
            .Construct();
        AssertKeyIsBaseObject(overlay, key.FormKey, misc.FormKey);
    }
}
