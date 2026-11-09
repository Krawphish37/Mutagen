using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Music type VNAM (Re-evaluate Interval) and UNAM (Stability Interval) are 8-byte unsigned integers in milliseconds
// (xEdit: itU64 with wbDiv(1000), shown in seconds). Mutagen kept them as raw bytes. Starfield.esm: 8 bytes on all 83,
// non-zero only on MUSGenesisCombat (2000 and 6000 ms).
public class MusicTypeIntervalTests
{
    // MUSGenesisCombat (013D5F:Starfield.esm)
    private const string GenesisCombat = "4544494411004D555347656E65736973436F6D62617400464E414D040024000000504E414D04000F001027574E414D04000000A041564E414D0800D007000000000000554E414D08007017000000000000544E414D3800787D2400DA6A2400D96A2400DB04180004362400FA072400EFF82300D74D230072561500C94D0200FFEE110047EF11000336240058EF1100";

    private static IEnumerable<IMusicTypeGetter> Read(string hex)
        => ReadBoth<IMusicTypeGetter>(Record("MUSC", Hex(hex)), f => MusicType.CreateFromBinary(f), (s, p) => MusicTypeBinaryOverlay.MusicTypeFactory(s, p));

    [Fact]
    public void GenesisCombat_Reads()
    {
        foreach (var musc in Read(GenesisCombat))
        {
            musc.FadeDuration.ShouldBe(20f);
            musc.ReevaluateIntervalMilliseconds.ShouldBe(2000UL);
            musc.StabilityIntervalMilliseconds.ShouldBe(6000UL);
            musc.Tracks.Count.ShouldBe(14);
        }
    }

    [Fact]
    public void GenesisCombat_WritesSameBytes()
    {
        foreach (var musc in Read(GenesisCombat))
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => musc.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(GenesisCombat));
        }
    }

    [Fact]
    public void Flag_RemovalQueued()
    {
        // xEdit's FNAM bit 0x10 (Removal Queued); not set in Starfield.esm
        ((int)MusicType.Flag.RemovalQueued).ShouldBe(0x10);
        foreach (var musc in ReadBoth<IMusicTypeGetter>(Record("MUSC", Subrecord("FNAM", "10-00-00-00")), f => MusicType.CreateFromBinary(f), (s, p) => MusicTypeBinaryOverlay.MusicTypeFactory(s, p)))
        {
            musc.Flags.ShouldBe(MusicType.Flag.RemovalQueued);
        }
    }
}
