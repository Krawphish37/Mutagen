using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Fallout4;

// Hand-written binary parsing used to build FormLinks typed to the record class
// (FormLink<IdleAnimation>, FormLink<Footstep>, FormLink<PlacedObject>) while the overlay,
// the generated parsers and the declared property types all use the getter interface.
public class FormLinkTargetTypeTests
{
    private static readonly ModKey TestModKey = ModKey.FromNameAndExtension("Test.esp");
    private static readonly MasterReferenceCollection Masters = new(TestModKey);

    private static ParsingMeta Meta() => new(
        GameConstants.Fallout4,
        TestModKey,
        SeparatedMasterPackage.NotSeparate(Masters));

    private static byte[] Write(IBinaryItem item)
    {
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(
                   stream,
                   new WritingBundle(GameConstants.Fallout4)
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
        var package = new Package(new FormKey(TestModKey, 0x800), Fallout4Release.Fallout4)
        {
            IdleAnimations = new PackageIdles
            {
                Animations = new() { new FormLink<IIdleAnimationGetter>(idle) },
            },
        };
        var bytes = Write(package);

        var parsed = new IPackageGetter[]
        {
            Package.CreateFromBinary(Frame(bytes)),
            PackageBinaryOverlay.PackageFactory(bytes, OverlayPackage()),
        };
        foreach (var item in parsed)
        {
            var link = item.IdleAnimations!.Animations!.ShouldHaveSingleItem();
            link.FormKey.ShouldBe(idle);
            link.Type.ShouldBe(typeof(IIdleAnimationGetter));
        }
    }

    [Fact]
    public void FootstepSet_FootstepLinks_TargetGetterInterface()
    {
        var footstep = new FormKey(TestModKey, 0x801);
        var footstepSet = new FootstepSet(new FormKey(TestModKey, 0x800), Fallout4Release.Fallout4);
        footstepSet.WalkFootsteps.Add(new FormLink<IFootstepGetter>(footstep));
        footstepSet.RunFootsteps.Add(new FormLink<IFootstepGetter>(footstep));
        footstepSet.SprintFootsteps.Add(new FormLink<IFootstepGetter>(footstep));
        footstepSet.SneakFootsteps.Add(new FormLink<IFootstepGetter>(footstep));
        footstepSet.SwimFootsteps.Add(new FormLink<IFootstepGetter>(footstep));
        var bytes = Write(footstepSet);

        var parsed = new IFootstepSetGetter[]
        {
            FootstepSet.CreateFromBinary(Frame(bytes)),
            FootstepSetBinaryOverlay.FootstepSetFactory(bytes, OverlayPackage()),
        };
        foreach (var item in parsed)
        {
            var links = item.WalkFootsteps
                .Concat(item.RunFootsteps)
                .Concat(item.SprintFootsteps)
                .Concat(item.SneakFootsteps)
                .Concat(item.SwimFootsteps)
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
        var placed = new PlacedObject(new FormKey(TestModKey, 0x800), Fallout4Release.Fallout4);
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
