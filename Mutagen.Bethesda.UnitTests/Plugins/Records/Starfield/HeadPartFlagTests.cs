using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield head part flags (DATA): bit 5 (0x20) is Uses Body Texture and bit 6 (0x40) Hide with "HideEar" Morph
// (xEdit's Starfield definitions). In Starfield.esm bit 6 is set on 18 head parts, all earrings.
public class HeadPartFlagTests
{
    [Fact]
    public void Earring_HidesWithHideEarMorph()
    {
        // Human_Female_Jewelry_Rings_Assorted_Right_F (HDPT 1F7F02): DATA 45 = Playable, Female, bit 6
        var bytes = Record("HDPT", Subrecord("DATA", "45"));
        foreach (var part in ReadBoth<IHeadPartGetter>(bytes, f => HeadPart.CreateFromBinary(f), (s, p) => HeadPartBinaryOverlay.HeadPartFactory(s, p)))
        {
            part.Flags.ShouldBe(HeadPart.Flag.Playable | HeadPart.Flag.Female | HeadPart.Flag.HideWithHideEarMorph);
        }
    }

    [Theory]
    [InlineData(HeadPart.Flag.UsesBodyTexture, 0x20)]
    [InlineData(HeadPart.Flag.HideWithHideEarMorph, 0x40)]
    public void Flag_Writes(HeadPart.Flag flag, byte expected)
    {
        var part = new HeadPart(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield) { Flags = flag };
        var written = GameConstants.Starfield.MajorRecord(Write(w => part.WriteToBinary(w)));
        written.FindSubrecord(new RecordType("DATA")).Content[0].ShouldBe(expected);
    }
}
