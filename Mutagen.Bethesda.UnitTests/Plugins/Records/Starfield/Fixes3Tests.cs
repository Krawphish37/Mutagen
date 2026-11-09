using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

public class Fixes3Tests
{
    [Fact]
    public void EdgeLinkMeshAcceptsAnyRecordType()
    {
        typeof(IEdgeLinkGetter).GetProperty(nameof(IEdgeLinkGetter.Mesh))!.PropertyType
            .ShouldBe(typeof(IFormLinkGetter<IStarfieldMajorRecordGetter>));
    }

    // MaxRank and LevelTierScaledOffset are real DATA fields since C68 (read after the target name);
    // ObjectModificationMaxRankTests covers them.
    [Theory]
    [InlineData("DataName")]
    public void ObjectModificationOverlaysHaveNoMisreadDataFields(string name)
    {
        var overlays = typeof(IAObjectModificationGetter).Assembly.GetTypes()
            .Where(t => t.Name.Contains("Modification") && t.Name.Contains("BinaryOverlay"))
            .ToList();
        overlays.ShouldNotBeEmpty();
        foreach (var overlay in overlays)
        {
            overlay.GetProperty(name).ShouldBeNull();
        }
    }
}
