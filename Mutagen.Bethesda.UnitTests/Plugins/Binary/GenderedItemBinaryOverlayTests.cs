using Loqui;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Noggog;
using Shouldly;
using Xunit;
using StarfieldMod = Mutagen.Bethesda.Starfield.StarfieldMod;
using StarfieldModBinaryTranslationMixIn = Mutagen.Bethesda.Starfield.StarfieldModBinaryTranslationMixIn;
using StarfieldRelease = Mutagen.Bethesda.Starfield.StarfieldRelease;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

public class GenderedItemBinaryOverlayTests
{
    private static readonly ModKey TestModKey = ModKey.FromNameAndExtension("Test.esp");

    private static void ShouldNotBeLoquiObject(object? item)
    {
        item.ShouldNotBeNull();
        item.GetType().GetGenericTypeDefinition().ShouldBe(typeof(GenderedItemBinaryOverlay<>));
        (item is ILoquiObject).ShouldBeFalse();
    }

    [Fact]
    public void SkyrimAssociationTypeParentTitle()
    {
        var mod = new SkyrimMod(TestModKey, SkyrimRelease.SkyrimSE);
        var assoc = mod.AssociationTypes.AddNew();
        assoc.ParentTitle = new GenderedItem<string?>("Father", "Mother");

        var stream = new MemoryTributary();
        mod.WriteToBinary(stream);
        stream.Position = 0;
        using var overlay = SkyrimMod.CreateFromBinaryOverlay(stream, SkyrimRelease.SkyrimSE, TestModKey);

        var parentTitle = overlay.AssociationTypes.First().ParentTitle;
        ShouldNotBeLoquiObject(parentTitle);
        parentTitle!.Male.ShouldBe("Father");
        parentTitle.Female.ShouldBe("Mother");
    }

    [Fact]
    public void SkyrimArmorAddonSkinTexture()
    {
        var mod = new SkyrimMod(TestModKey, SkyrimRelease.SkyrimSE);
        var maleTxst = mod.TextureSets.AddNew();
        var femaleTxst = mod.TextureSets.AddNew();
        var arma = mod.ArmorAddons.AddNew();
        arma.SkinTexture = new GenderedItem<IFormLinkNullableGetter<ITextureSetGetter>>(
            maleTxst.ToNullableLink(),
            femaleTxst.ToNullableLink());

        var stream = new MemoryTributary();
        mod.WriteToBinary(stream);
        stream.Position = 0;
        using var overlay = SkyrimMod.CreateFromBinaryOverlay(stream, SkyrimRelease.SkyrimSE, TestModKey);

        var skinTexture = overlay.ArmorAddons.First().SkinTexture;
        ShouldNotBeLoquiObject(skinTexture);
        skinTexture!.Male.FormKey.ShouldBe(maleTxst.FormKey);
        skinTexture.Female.FormKey.ShouldBe(femaleTxst.FormKey);
    }

    [Fact]
    public void StarfieldArmorAddonAltSkeleton()
    {
        var mod = new StarfieldMod(TestModKey, StarfieldRelease.Starfield);
        var arma = mod.ArmorAddons.AddNew();
        arma.AltSkeleton = new GenderedItem<string?>(@"Actors\Male.nif", null);

        var stream = new MemoryTributary();
        StarfieldModBinaryTranslationMixIn.WriteToBinary(mod, stream);
        stream.Position = 0;
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(stream, StarfieldRelease.Starfield, TestModKey);

        var altSkeleton = overlay.ArmorAddons.First().AltSkeleton;
        ShouldNotBeLoquiObject(altSkeleton);
        altSkeleton!.Male.ShouldBe(@"Actors\Male.nif");
        altSkeleton.Female.ShouldBeNull();
    }
}
