using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A Starfield surface block's ENAM Height and NAM4 Height Range are Min/Max floats (xEdit's Starfield definitions).
// The most common values in Starfield.esm are -500/1000 (491 blocks) and 0/800 (1,239), which UInt32 read as
// 3,287,941,120 / 1,148,846,080 and 0 / 1,145,569,280.
public class SurfaceBlockHeightTests
{
    [Fact]
    public void HeightAndRange_ReadAsFloats()
    {
        var bytes = Record("SFBK", Subrecord("ENAM", "00-00-FA-C3-00-00-7A-44"), Subrecord("NAM4", "00-00-00-00-00-00-48-44"));
        foreach (var block in ReadBoth<ISurfaceBlockGetter>(bytes, f => SurfaceBlock.CreateFromBinary(f), (s, p) => SurfaceBlockBinaryOverlay.SurfaceBlockFactory(s, p)))
        {
            block.Height.Min.ShouldBe(-500f);
            block.Height.Max.ShouldBe(1000f);
            block.HeightRange!.Min.ShouldBe(0f);
            block.HeightRange!.Max.ShouldBe(800f);
        }
    }

    [Fact]
    public void Height_Writes()
    {
        var block = new SurfaceBlock(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield);
        block.Height.Min = -500f;
        block.Height.Max = 1000f;
        var written = GameConstants.Starfield.MajorRecord(Write(w => block.WriteToBinary(w)));
        written.FindSubrecord(new RecordType("ENAM")).Content.ToArray().ShouldBe(Hex("00-00-FA-C3-00-00-7A-44"));
    }
}
