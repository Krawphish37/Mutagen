using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Flora FNAM and JNAM per xEdit: FNAM is a 2-byte Flags field (xEdit names no bits; 00 00 on all 300 floras in
// Starfield.esm), JNAM is the UInt16 "Activation Angle - For Player" (360 on all 300), as on Furniture.
// Mutagen kept both as raw bytes.
public class FloraFieldTests
{
    // FloraBloodStoneTall (0242F8:Starfield.esm), without its FULL and ATTX: they are localized string IDs and the
    // test reader treats them as plain strings
    private const string BloodStoneTall = "454449441400466C6F7261426C6F6F6453746F6E6554616C6C004F424E4418000000000000000000000000000000000000000000000000004F4454590400000000004F504453500000000000000000000000000000000000050000000000803F000000000000000000000000000000000000000000000000000000000000803F000000000000803F0000803F920A063F00000000FFFF7F7F4D4F444C1C004C616E6473636170655C466C6F72615C466C6F726130312E6E696600464C4C440400010000004B53495A0400020000004B57444108001DC12A00032E2200505250530C0005E923000000A04000000000504E414D0400CC4C3300464E414D020000004A4E414D02006801504649470400AF550000504648532800754D2C828D95DEBB40E8BB1324BE65B3000000000000000000000000000000000000000000000000504650430400646464644150505204001EFE29004F4254450400010000004F42544600004F4254532000020000000000000000000000FFFF01000000FB420200000101FD42020000010153544F500000424E414D040000000000464D414804000000803F464D494804000000803F464D41470400B4CF0500464D49470400B5CF0500";

    private static IEnumerable<IFloraGetter> Read(string hex)
        => ReadBoth<IFloraGetter>(Record("FLOR", Hex(hex)), f => Flora.CreateFromBinary(f), (s, p) => FloraBinaryOverlay.FloraFactory(s, p));

    [Fact]
    public void BloodStoneTall_Reads()
    {
        foreach (var flor in Read(BloodStoneTall))
        {
            flor.Flags.ShouldBe((ushort)0);
            flor.ActivationAngleForPlayer.ShouldBe((ushort)360);
        }
    }

    [Fact]
    public void BloodStoneTall_WritesSameBytes()
    {
        foreach (var flor in Read(BloodStoneTall))
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => flor.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(BloodStoneTall));
        }
    }
}
