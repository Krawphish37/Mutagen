using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Assets;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Skyrim.Assets;

public class AssetTestsSkyrim
{
    [Fact]
    public void TestRemapBook() {
	    var book = new Book(FormKey.Null, SkyrimRelease.SkyrimSE);

	    book.BookText.String = """
		Some test text
		abc <img src='img://Textures/11.png'>  def
		Textures/11.png
		  <img src='img://Textures/44.png'>
		  <img src='img://Textures/22.png'>
		More random text
		  <img src='img://Textures/33.png'>
		XXX
		""";

	    book.RemapInferredAssetLinks(new Dictionary<IAssetLinkGetter, string>
	    {
		    { new AssetLink<SkyrimTextureAssetType>("Textures/11.png"), "Textures/aa.png" },
		    { new AssetLink<SkyrimTextureAssetType>("Textures/22.png"), "Textures/bb.png" },
		    { new AssetLink<SkyrimTextureAssetType>("Textures/33.png"), "Textures/cc.png" },
		    { new AssetLink<SkyrimTextureAssetType>("Textures/44.png"), "Textures/dd.png" },
	    });

	    book.BookText.String.ShouldBe("""
		Some test text
		abc <img src='img://Textures/aa.png'>  def
		Textures/11.png
		  <img src='img://Textures/dd.png'>
		  <img src='img://Textures/bb.png'>
		More random text
		  <img src='img://Textures/cc.png'>
		XXX
		""");
    }

    private static Dictionary<IAssetLinkGetter, string> MapOwnLinks(IAssetLinkContainer container, string to)
    {
	    var mapping = new Dictionary<IAssetLinkGetter, string>();
	    foreach (var link in container.EnumerateListedAssetLinks())
	    {
		    mapping.TryAdd(link, to);
	    }
	    return mapping;
    }

    [Fact]
    public void RemapListedFieldsWithOwnLinksAsKeys()
    {
	    var lod = new Lod
	    {
		    Level0 = new AssetLink<SkyrimModelAssetType>("Test\\Shared.nif"),
		    Level1 = new AssetLink<SkyrimModelAssetType>("Test\\Shared.nif"),
		    Level2 = new AssetLink<SkyrimModelAssetType>("Test\\Shared.nif"),
		    Level3 = new AssetLink<SkyrimModelAssetType>("Test\\Other.nif"),
	    };
	    var mapping = MapOwnLinks(lod, "Test\\Renamed.nif");
	    mapping.Remove(new AssetLink<SkyrimModelAssetType>("Test\\Other.nif"));

	    lod.RemapListedAssetLinks(mapping);

	    lod.Level0.GivenPath.ShouldBe("Test\\Renamed.nif");
	    lod.Level1.GivenPath.ShouldBe("Test\\Renamed.nif");
	    lod.Level2.GivenPath.ShouldBe("Test\\Renamed.nif");
	    lod.Level3.GivenPath.ShouldBe("Test\\Other.nif");
	    mapping.Keys.Single().GivenPath.ShouldBe("Test\\Shared.nif");
    }

    [Fact]
    public void RemapListedListWithOwnLinksAsKeys()
    {
	    var sound = new SoundDescriptor(FormKey.Null, SkyrimRelease.SkyrimSE);
	    sound.SoundFiles.Add(new AssetLink<SkyrimSoundAssetType>("Test\\Shared.wav"));
	    sound.SoundFiles.Add(new AssetLink<SkyrimSoundAssetType>("Test\\Shared.wav"));
	    var mapping = MapOwnLinks(sound, "Test\\Renamed.wav");

	    sound.RemapListedAssetLinks(mapping);

	    sound.SoundFiles.Select(x => x.GivenPath).ShouldBe(new[] { "Test\\Renamed.wav", "Test\\Renamed.wav" });
	    mapping.Keys.Single().GivenPath.ShouldBe("Test\\Shared.wav");
    }

    [Fact]
    public void RemapListedArrayWithOwnLinksAsKeys()
    {
	    var weather = new Weather(FormKey.Null, SkyrimRelease.SkyrimSE);
	    weather.CloudTextures[0] = new AssetLink<SkyrimTextureAssetType>("Test\\Shared.dds");
	    weather.CloudTextures[2] = new AssetLink<SkyrimTextureAssetType>("Test\\Shared.dds");
	    var mapping = MapOwnLinks(weather, "Test\\Renamed.dds");

	    weather.RemapListedAssetLinks(mapping);

	    weather.CloudTextures[0]!.GivenPath.ShouldBe("Test\\Renamed.dds");
	    weather.CloudTextures[1].ShouldBeNull();
	    weather.CloudTextures[2]!.GivenPath.ShouldBe("Test\\Renamed.dds");
	    mapping.Keys.Single().GivenPath.ShouldBe("Test\\Shared.dds");
    }

    [Theory]
    [InlineData(AssetLinkQuery.Inferred)]
    [InlineData(AssetLinkQuery.Resolved)]
    [InlineData(AssetLinkQuery.Inferred | AssetLinkQuery.Resolved)]
    public void RemapAssetLinksSkipsListedListWhenNotQueried(AssetLinkQuery query)
    {
	    var soundDescriptor = new SoundDescriptor(FormKey.Null, SkyrimRelease.SkyrimSE);
	    soundDescriptor.SoundFiles.Add(new AssetLink<SkyrimSoundAssetType>("Sound/fx/old.wav"));

	    soundDescriptor.RemapAssetLinks(new Dictionary<IAssetLinkGetter, string>
	    {
		    { new AssetLink<SkyrimSoundAssetType>("Sound/fx/old.wav"), "Sound/fx/new.wav" },
	    }, query, null);

	    soundDescriptor.SoundFiles[0].GivenPath.ShouldBe("Sound/fx/old.wav");
    }

    [Theory]
    [InlineData(AssetLinkQuery.Inferred)]
    [InlineData(AssetLinkQuery.Resolved)]
    [InlineData(AssetLinkQuery.Inferred | AssetLinkQuery.Resolved)]
    public void RemapAssetLinksSkipsListedArrayWhenNotQueried(AssetLinkQuery query)
    {
	    var weather = new Weather(FormKey.Null, SkyrimRelease.SkyrimSE);
	    weather.CloudTextures[0] = new AssetLink<SkyrimTextureAssetType>("Textures/sky/old.dds");

	    weather.RemapAssetLinks(new Dictionary<IAssetLinkGetter, string>
	    {
		    { new AssetLink<SkyrimTextureAssetType>("Textures/sky/old.dds"), "Textures/sky/new.dds" },
	    }, query, null);

	    weather.CloudTextures[0]!.GivenPath.ShouldBe("Textures/sky/old.dds");
    }

    [Fact]
    public void RemapInferredAssetLinksSkipsListedList()
    {
	    var mod = new SkyrimMod(ModKey.FromNameAndExtension("Test.esp"), SkyrimRelease.SkyrimSE);
	    var soundDescriptor = mod.SoundDescriptors.AddNew();
	    soundDescriptor.SoundFiles.Add(new AssetLink<SkyrimSoundAssetType>("Sound/fx/old.wav"));

	    mod.RemapInferredAssetLinks(new Dictionary<IAssetLinkGetter, string>
	    {
		    { new AssetLink<SkyrimSoundAssetType>("Sound/fx/old.wav"), "Sound/fx/new.wav" },
	    });

	    soundDescriptor.SoundFiles[0].GivenPath.ShouldBe("Sound/fx/old.wav");
    }

    [Fact]
    public void RemapListedAssetLinksRemapsListedListAndArray()
    {
	    var soundDescriptor = new SoundDescriptor(FormKey.Null, SkyrimRelease.SkyrimSE);
	    soundDescriptor.SoundFiles.Add(new AssetLink<SkyrimSoundAssetType>("Sound/fx/old.wav"));
	    var weather = new Weather(FormKey.Null, SkyrimRelease.SkyrimSE);
	    weather.CloudTextures[0] = new AssetLink<SkyrimTextureAssetType>("Textures/sky/old.dds");
	    var mapping = new Dictionary<IAssetLinkGetter, string>
	    {
		    { new AssetLink<SkyrimSoundAssetType>("Sound/fx/old.wav"), "Sound/fx/new.wav" },
		    { new AssetLink<SkyrimTextureAssetType>("Textures/sky/old.dds"), "Textures/sky/new.dds" },
	    };

	    soundDescriptor.RemapListedAssetLinks(mapping);
	    weather.RemapListedAssetLinks(mapping);

	    soundDescriptor.SoundFiles[0].GivenPath.ShouldBe("Sound/fx/new.wav");
	    weather.CloudTextures[0]!.GivenPath.ShouldBe("Textures/sky/new.dds");
    }
}
