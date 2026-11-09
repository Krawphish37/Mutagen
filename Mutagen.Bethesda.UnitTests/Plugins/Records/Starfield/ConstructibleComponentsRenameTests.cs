using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// The recipe components are only ConstructibleComponents. The old spelling ConstructableComponents was kept for a while
// as an [Obsolete] alias over the same list, but serializers that walk public properties (Spriggit) saw it as a second
// field and wrote every recipe's components twice (coverage bug C60), so the alias is gone.
public class ConstructibleComponentsRenameTests
{
    private static ConstructibleObjectComponent Component(uint count, ModKey? owner = null) => new()
    {
        Component = new FormLink<IItemGetter>(new FormKey(owner ?? ModKey.FromFileName("Test.esp"), 0x123456)),
        RequiredCount = count,
    };

    [Fact]
    public void OnlyOneComponentsProperty()
    {
        foreach (var type in new[] { typeof(ConstructibleObject), typeof(IConstructibleObject), typeof(IConstructibleObjectGetter) })
        {
            type.GetProperty("ConstructableComponents").ShouldBeNull();
        }
        typeof(IConstructibleObjectGetter).GetProperty(nameof(IConstructibleObjectGetter.ConstructibleComponents)).ShouldNotBeNull();
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void ComponentsRoundTripOnce(StarfieldMod mod)
    {
        var cobj = mod.ConstructibleObjects.AddNew();
        cobj.ConstructibleComponents = new ExtendedList<ConstructibleObjectComponent> { Component(4, mod.ModKey), Component(5, mod.ModKey) };

        var stream = new MemoryTributary();
        mod.WriteToBinary(stream);
        stream.Position = 0;
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(stream, StarfieldRelease.Starfield, mod.ModKey);

        var read = overlay.ConstructibleObjects.ShouldHaveSingleItem();
        read.ConstructibleComponents.ShouldNotBeNull();
        read.ConstructibleComponents.Select(c => c.RequiredCount).ShouldBe(new uint[] { 4, 5 });
    }
}
