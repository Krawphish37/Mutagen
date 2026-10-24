using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing;
using Noggog;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

public class GetOrAddAsOverrideUntypedTests
{
    [Fact]
    public void TryGetOrAddAsOverrideUntyped_Overlay_Success()
    {
        // Arrange
        var masterMod = new StarfieldMod(TestConstants.MasterModKey, StarfieldRelease.Starfield);
        var weapon = masterMod.Weapons.AddNew();
        weapon.EditorID = "TestWeapon";
        var stream = new MemoryTributary();
        masterMod.WriteToBinary(stream);
        stream.Position = 0;
        var overlayWeapon = StarfieldMod.CreateFromBinaryOverlay(stream, StarfieldRelease.Starfield, masterMod.ModKey)
            .Weapons.First();

        var pluginMod = new StarfieldMod(TestConstants.PluginModKey, StarfieldRelease.Starfield);

        // Act
        var result = pluginMod.Weapons.TryGetOrAddAsOverrideUntyped(overlayWeapon, out var overrideRecord);

        // Assert
        Assert.True(result);
        Assert.IsType<Weapon>(overrideRecord);
        Assert.Equal(weapon.FormKey, overrideRecord.FormKey);
        Assert.Equal("TestWeapon", overrideRecord.EditorID);
        Assert.Equal(1, pluginMod.Weapons.Count);
    }
}
