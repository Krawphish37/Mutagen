using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Mapping;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

public class LinkInterfaceImplementerTests
{
    // Pairs of link interface and a record type that real Starfield data points that link at
    public static IEnumerable<object[]> Implementers => new[]
    {
        // PlacedObject.Base and the IPlaceableObject condition parameters
        new object[] { typeof(IPlaceableObjectGetter), typeof(IStaticGetter) },
        new object[] { typeof(IPlaceableObjectGetter), typeof(IProjectedDecalGetter) },
        new object[] { typeof(IPlaceableObjectGetter), typeof(IMoveableStaticGetter) },
        new object[] { typeof(IPlaceableObjectGetter), typeof(ILightGetter) },
        new object[] { typeof(IPlaceableObjectGetter), typeof(IPackInGetter) },
        new object[] { typeof(IPlaceableObjectGetter), typeof(IMiscItemGetter) },
        new object[] { typeof(IPlaceableObjectGetter), typeof(IFurnitureGetter) },
        new object[] { typeof(IPlaceableObjectGetter), typeof(IActivatorGetter) },
        new object[] { typeof(IPlaceableObjectGetter), typeof(IKeyGetter) },
        new object[] { typeof(IPlaceableObjectGetter), typeof(INpcGetter) },
        new object[] { typeof(IPlaceableObjectGetter), typeof(ITextureSetGetter) },
        new object[] { typeof(IPlaceableObjectGetter), typeof(ITalkingActivatorGetter) },
        // Explosion.PlacedObject
        new object[] { typeof(IExplodeSpawnGetter), typeof(IHazardGetter) },
        new object[] { typeof(IExplodeSpawnGetter), typeof(IActivatorGetter) },
        new object[] { typeof(IExplodeSpawnGetter), typeof(IMoveableStaticGetter) },
        new object[] { typeof(IExplodeSpawnGetter), typeof(IExplosionGetter) },
        new object[] { typeof(IExplodeSpawnGetter), typeof(ITalkingActivatorGetter) },
        // IdleAnimation.ParentAnimation / PreviousAnimation
        new object[] { typeof(IIdleRelationGetter), typeof(IIdleAnimationGetter) },
        new object[] { typeof(IIdleRelationGetter), typeof(IActionRecordGetter) },
        // QuestReferenceAlias.VoiceTypes
        new object[] { typeof(IAliasVoiceTypeGetter), typeof(IVoiceTypeGetter) },
        new object[] { typeof(IAliasVoiceTypeGetter), typeof(IFormListGetter) },
        new object[] { typeof(IAliasVoiceTypeGetter), typeof(INpcGetter) },
        new object[] { typeof(IAliasVoiceTypeGetter), typeof(IFactionGetter) },
    };

    [Theory]
    [MemberData(nameof(Implementers))]
    public void LinkInterfaceIsImplemented(Type linkInterface, Type recordGetter)
    {
        linkInterface.IsAssignableFrom(recordGetter).ShouldBeTrue();
        MetaInterfaceMapping.Instance.TryGetRegistrationsForInterface(GameCategory.Starfield, linkInterface, out var regis)
            .ShouldBeTrue();
        regis.Registrations.Select(x => x.GetterType).ShouldContain(recordGetter);
    }

    [Theory, MutagenAutoData]
    public void LinksResolveAgainstOverlay(
        IFileSystem fileSystem,
        ModKey modKey,
        DirectoryPath existingDir)
    {
        var mod = new StarfieldMod(modKey, StarfieldRelease.Starfield);
        var stat = mod.Statics.AddNew();
        var hazard = mod.Hazards.AddNew();
        var idle = mod.IdleAnimations.AddNew();
        var voiceType = mod.VoiceTypes.AddNew();
        var npc = mod.Npcs.AddNew();
        var modPath = Path.Combine(existingDir, mod.ModKey.FileName);
        mod.WriteToBinary(modPath, new BinaryWriteParameters()
        {
            FileSystem = fileSystem
        });

        using var overlay = StarfieldMod.Create(StarfieldRelease.Starfield)
            .FromPath(modPath)
            .WithLoadOrderFromHeaderMasters()
            .WithNoDataFolder()
            .WithFileSystem(fileSystem)
            .Construct();
        var cache = overlay.ToImmutableLinkCache();

        var placed = new PlacedObject(FormKey.Factory($"123456:{modKey.FileName}"), StarfieldRelease.Starfield);
        placed.Base.SetTo(stat.FormKey);
        placed.Base.TryResolve(cache, out var placedBase).ShouldBeTrue();
        placedBase!.FormKey.ShouldBe(stat.FormKey);

        var getIsId = new GetIsIDConditionData();
        getIsId.FirstParameter.Link.SetTo(npc.FormKey);
        getIsId.FirstParameter.Link.TryResolve(cache, out _).ShouldBeTrue();

        var explosion = new Explosion(FormKey.Factory($"123457:{modKey.FileName}"), StarfieldRelease.Starfield);
        explosion.PlacedObject.SetTo(hazard.FormKey);
        explosion.PlacedObject.TryResolve(cache, out _).ShouldBeTrue();

        var childIdle = new IdleAnimation(FormKey.Factory($"123458:{modKey.FileName}"), StarfieldRelease.Starfield);
        childIdle.ParentAnimation.SetTo(idle.FormKey);
        childIdle.ParentAnimation.TryResolve(cache, out _).ShouldBeTrue();

        var alias = new QuestReferenceAlias();
        alias.VoiceTypes.SetTo(voiceType.FormKey);
        alias.VoiceTypes.TryResolve(cache, out _).ShouldBeTrue();

        overlay.EnumerateMajorRecords<IPlaceableObjectGetter>().Select(x => x.FormKey)
            .ShouldContain(stat.FormKey);
    }
}
