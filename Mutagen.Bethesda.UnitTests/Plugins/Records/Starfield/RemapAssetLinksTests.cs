using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Starfield.Assets;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

public class RemapAssetLinksTests
{
    private const string SharedPath = "Clothes\\Test\\Shared.nif";
    private const string RenamedPath = "Clothes\\Test\\Renamed.nif";

    private static Armor AddArmor(StarfieldMod mod, string editorId)
    {
        var armor = mod.Armors.AddNew(editorId);
        armor.WorldModel = new GenderedItem<Model?>(
            new Model { File = new AssetLink<StarfieldModelAssetType>(SharedPath) },
            new Model { File = new AssetLink<StarfieldModelAssetType>(SharedPath) });
        return armor;
    }

    private static Dictionary<IAssetLinkGetter, string> MapOwnLinks(IAssetLinkContainer container)
    {
        var mapping = new Dictionary<IAssetLinkGetter, string>();
        foreach (var link in container.EnumerateListedAssetLinks())
        {
            mapping.TryAdd(link, RenamedPath);
        }
        return mapping;
    }

    [Fact]
    public void RemapListedWithOwnLinksAsKeysRemapsSharedPaths()
    {
        var mod = new StarfieldMod(ModKey.FromFileName("Test.esm"), StarfieldRelease.Starfield);
        var armor = AddArmor(mod, "Armor");
        var mapping = MapOwnLinks(armor);
        mapping.Count.ShouldBe(1);

        armor.RemapListedAssetLinks(mapping);

        armor.WorldModel!.Male!.File!.GivenPath.ShouldBe(RenamedPath);
        armor.WorldModel!.Female!.File!.GivenPath.ShouldBe(RenamedPath);
    }

    [Fact]
    public void RemapListedDoesNotModifyMappingKeys()
    {
        var mod = new StarfieldMod(ModKey.FromFileName("Test.esm"), StarfieldRelease.Starfield);
        var armor = AddArmor(mod, "Armor");
        var mapping = MapOwnLinks(armor);
        var key = mapping.Keys.Single();

        armor.RemapListedAssetLinks(mapping);

        key.GivenPath.ShouldBe(SharedPath);
        mapping.ContainsKey(new AssetLink<StarfieldModelAssetType>(SharedPath)).ShouldBeTrue();
    }

    [Fact]
    public void RemapListedAcrossRecordsWithOwnLinksAsKeys()
    {
        var mod = new StarfieldMod(ModKey.FromFileName("Test.esm"), StarfieldRelease.Starfield);
        var first = AddArmor(mod, "First");
        var second = AddArmor(mod, "Second");
        var mapping = MapOwnLinks(mod);
        mapping.Count.ShouldBe(1);

        mod.RemapListedAssetLinks(mapping);

        foreach (var armor in new[] { first, second })
        {
            armor.WorldModel!.Male!.File!.GivenPath.ShouldBe(RenamedPath);
            armor.WorldModel!.Female!.File!.GivenPath.ShouldBe(RenamedPath);
        }
    }

    [Fact]
    public void RemapListedLeavesUnmappedLinks()
    {
        var mod = new StarfieldMod(ModKey.FromFileName("Test.esm"), StarfieldRelease.Starfield);
        var armor = AddArmor(mod, "Armor");
        var other = new AssetLink<StarfieldModelAssetType>("Clothes\\Test\\Other.nif");
        armor.WorldModel!.Female!.File = other;

        armor.RemapListedAssetLinks(new Dictionary<IAssetLinkGetter, string>
        {
            { new AssetLink<StarfieldModelAssetType>(SharedPath), RenamedPath },
        });

        armor.WorldModel!.Male!.File!.GivenPath.ShouldBe(RenamedPath);
        armor.WorldModel!.Female!.File.ShouldBeSameAs(other);
        other.GivenPath.ShouldBe("Clothes\\Test\\Other.nif");
    }
}
