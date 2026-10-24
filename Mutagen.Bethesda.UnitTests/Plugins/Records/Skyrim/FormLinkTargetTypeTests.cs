using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Skyrim;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Skyrim;

// Hand-written binary parsing used to build FormLinks typed to the record class
// (FormLink<IdleAnimation>, FormLink<Footstep>, FormLink<PlacedObject>) while the overlay,
// the generated parsers and the declared property types all use the getter interface.
public class FormLinkTargetTypeTests
{
    private static readonly ModKey TestModKey = ModKey.FromNameAndExtension("Test.esp");
    private static readonly MasterReferenceCollection Masters = new(TestModKey);

    private static ParsingMeta Meta() => new(
        GameConstants.SkyrimSE,
        TestModKey,
        SeparatedMasterPackage.NotSeparate(Masters));

    private static byte[] Write(IBinaryItem item)
    {
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(
                   stream,
                   new WritingBundle(GameConstants.SkyrimSE)
                   {
                       MasterReferences = Masters,
                       SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(Masters),
                   }))
        {
            item.WriteToBinary(writer);
        }
        return stream.ToArray();
    }

    private static MutagenFrame Frame(byte[] bytes) => new(new MutagenMemoryReadStream(bytes, Meta()));

    private static BinaryOverlayFactoryPackage OverlayPackage() => new(Meta());

    [Fact]
    public void Package_IdleAnimationLinks_TargetGetterInterface()
    {
        var idle = new FormKey(TestModKey, 0x801);
        var package = new Package(new FormKey(TestModKey, 0x800), SkyrimRelease.SkyrimSE)
        {
            IdleAnimations = new PackageIdles(),
        };
        package.IdleAnimations.Animations.Add(new FormLink<IIdleAnimationGetter>(idle));
        var bytes = Write(package);

        var parsed = new IPackageGetter[]
        {
            Package.CreateFromBinary(Frame(bytes)),
            PackageBinaryOverlay.PackageFactory(bytes, OverlayPackage()),
        };
        foreach (var item in parsed)
        {
            var link = item.IdleAnimations!.Animations.ShouldHaveSingleItem();
            link.FormKey.ShouldBe(idle);
            link.Type.ShouldBe(typeof(IIdleAnimationGetter));
        }
    }

    [Fact]
    public void FootstepSet_FootstepLinks_TargetGetterInterface()
    {
        var footstep = new FormKey(TestModKey, 0x801);
        var footstepSet = new FootstepSet(new FormKey(TestModKey, 0x800), SkyrimRelease.SkyrimSE);
        footstepSet.WalkForwardFootsteps.Add(new FormLink<IFootstepGetter>(footstep));
        footstepSet.RunForwardFootsteps.Add(new FormLink<IFootstepGetter>(footstep));
        footstepSet.WalkForwardAlternateFootsteps.Add(new FormLink<IFootstepGetter>(footstep));
        footstepSet.RunForwardAlternateFootsteps.Add(new FormLink<IFootstepGetter>(footstep));
        footstepSet.WalkForwardAlternateFootsteps2.Add(new FormLink<IFootstepGetter>(footstep));
        var bytes = Write(footstepSet);

        var parsed = new IFootstepSetGetter[]
        {
            FootstepSet.CreateFromBinary(Frame(bytes)),
            FootstepSetBinaryOverlay.FootstepSetFactory(bytes, OverlayPackage()),
        };
        foreach (var item in parsed)
        {
            var links = item.WalkForwardFootsteps
                .Concat(item.RunForwardFootsteps)
                .Concat(item.WalkForwardAlternateFootsteps)
                .Concat(item.RunForwardAlternateFootsteps)
                .Concat(item.WalkForwardAlternateFootsteps2)
                .ToArray();
            links.Length.ShouldBe(5);
            foreach (var link in links)
            {
                link.FormKey.ShouldBe(footstep);
                link.Type.ShouldBe(typeof(IFootstepGetter));
            }
        }
    }

    [Fact]
    public void PlacedObject_LinkedRooms_TargetGetterInterface()
    {
        var room = new FormKey(TestModKey, 0x801);
        var placed = new PlacedObject(new FormKey(TestModKey, 0x800), SkyrimRelease.SkyrimSE);
        placed.LinkedRooms.Add(new FormLink<IPlacedObjectGetter>(room));
        var bytes = Write(placed);

        var parsed = new IPlacedObjectGetter[]
        {
            PlacedObject.CreateFromBinary(Frame(bytes)),
            PlacedObjectBinaryOverlay.PlacedObjectFactory(bytes, OverlayPackage()),
        };
        foreach (var item in parsed)
        {
            var link = item.LinkedRooms.ShouldHaveSingleItem();
            link.FormKey.ShouldBe(room);
            link.Type.ShouldBe(typeof(IPlacedObjectGetter));
        }
    }
}
