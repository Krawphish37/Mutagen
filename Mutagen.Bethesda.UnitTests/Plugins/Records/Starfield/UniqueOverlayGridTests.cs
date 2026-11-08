using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Noggog;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// The unique overlay list component (surface patterns' worldspaces) stores each worldspace's grid as two Int32s,
// Grid Y then Grid X (xEdit's Starfield definitions), now GridY and GridX. Read as a float pair, 7 became 9.8E-45, so the grid was
// effectively 0 on all 37 surface patterns that have it.
public class UniqueOverlayGridTests
{
    [Fact]
    public void Grid_ReadsAsIntegers()
    {
        // SFPT 3FFD57 in Starfield.esm: 1 worldspace (00299116), Grid Y 7, Grid X 8
        var bytes = Record("SFPT",
            Subrecord("BFCB", Encoding.ASCII.GetBytes("UniqueOverlayList_Component\0")),
            Subrecord("DATA", "01-00-00-00-16-91-29-00-07-00-00-00-08-00-00-00"),
            Subrecord("BFCE", ""));
        foreach (var pattern in ReadBoth<ISurfacePatternGetter>(bytes, f => SurfacePattern.CreateFromBinary(f), (s, p) => SurfacePatternBinaryOverlay.SurfacePatternFactory(s, p)))
        {
            var item = pattern.Components.OfType<IUniqueOverlayListComponentGetter>().Single().Worldspaces.Single();
            item.Worldspace.FormKey.ID.ShouldBe(0x299116u);
            item.GridY.ShouldBe(7);
            item.GridX.ShouldBe(8);
        }
    }
}
