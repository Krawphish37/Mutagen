using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using TempFile = Noggog.IO.TempFile;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Hand-written binary parsing used to build FormLinks typed to the record class
// (FormLink<IdleAnimation>, FormLink<Footstep>) while the overlay, the generated parsers
// and the declared property types all use the getter interface. A class-typed link does
// not resolve against read-only records, and querying a link cache with the class type
// first left that type's cache empty for every later lookup.
public class FormLinkTargetTypeTests
{
    private static readonly ModKey TestModKey = ModKey.FromNameAndExtension("Test.esm");
    private static readonly MasterReferenceCollection Masters = new(TestModKey);

    private static ParsingMeta Meta() => new(
        GameConstants.Starfield,
        TestModKey,
        SeparatedMasterPackage.NotSeparate(Masters));

    private static byte[] Write(IBinaryItem item)
    {
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(
                   stream,
                   new WritingBundle(GameConstants.Starfield)
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
        var package = new Package(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
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
        var footstepSet = new FootstepSet(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield);
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
    public void FullImportIdleLink_ResolvesAgainstOverlayLinkCache()
    {
        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esm");
        var mod = new StarfieldMod(TestModKey, StarfieldRelease.Starfield);
        var idleA = mod.IdleAnimations.AddNew("IdleA");
        var idleB = mod.IdleAnimations.AddNew("IdleB");
        var package = mod.Packages.AddNew("Package");
        package.IdleAnimations = new PackageIdles
        {
            Animations = new() { new FormLink<IIdleAnimationGetter>(idleA.FormKey) },
        };
        var path = new ModPath(TestModKey, tmp.File.Path);
        mod.BeginWrite
            .ToPath(path)
            .WithNoLoadOrder()
            .NoModKeySync()
            .Write();

        var full = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield);
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield);
        var cache = overlay.ToImmutableLinkCache();

        var link = full.Packages.Records.ShouldHaveSingleItem().IdleAnimations!.Animations!.ShouldHaveSingleItem();
        link.TryResolve(cache).ShouldNotBeNull().EditorID.ShouldBe("IdleA");
        cache.TryResolve<IIdleAnimationGetter>(idleB.FormKey, out var other).ShouldBeTrue();
        other.EditorID.ShouldBe("IdleB");
    }
}
