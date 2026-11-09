using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// PKIN FNAM was raw bytes; xEdit names it as flags. Values are real Starfield.esm subrecords.
// Starfield.esm uses bits 0x01-0x40 (0x01 has no name), plus one junk value on OPMinePod18MHexA01PackIn,
// which still round-trips because the enum is a full uint.
public class PackInFlagTests
{
    private static IEnumerable<IPackInGetter> Read(string fnam)
        => ReadBoth<IPackInGetter>(Record("PKIN", Subrecord("FNAM", fnam)), f => PackIn.CreateFromBinary(f), (s, p) => PackInBinaryOverlay.PackInFactory(s, p));

    [Theory]
    // SC_LD_Carts_D03
    [InlineData("4C-00-00-00", PackIn.Flag.Fixed | PackIn.Flag.Grouped | PackIn.Flag.OverrideDirtiness)]
    // ClutterPI_EngineeringGreebA04
    [InlineData("2C-00-00-00", PackIn.Flag.Fixed | PackIn.Flag.Grouped | PackIn.Flag.UserDefinedPrimitives)]
    // OPMinePod18MHexA01PackIn
    [InlineData("E0-35-2F-58", (PackIn.Flag)0x582F35E0)]
    public void Flags(string fnam, PackIn.Flag expected)
    {
        foreach (var pkin in Read(fnam))
        {
            pkin.Flags.ShouldBe(expected);
            var written = GameConstants.Starfield.MajorRecord(Write(w => pkin.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("FNAM")).Content.ToArray().ShouldBe(Hex(fnam));
        }
    }

    [Fact]
    public void MajorFlagValues()
    {
        ((int)PackIn.MajorFlag.Prefab).ShouldBe(0x200);
        ((int)PackIn.MajorFlag.PackInUseOnly).ShouldBe(0x2000);
        ((int)PackIn.MajorFlag.Obstacle).ShouldBe(0x0200_0000);
    }
}
