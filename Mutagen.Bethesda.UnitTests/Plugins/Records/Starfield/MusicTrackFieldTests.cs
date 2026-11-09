using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Music track names per xEdit: MTSH is the Single Track Sound, MSTF is "Conditions can't fail after success"
// (a 1-byte bool; Mutagen kept it as raw bytes). Starfield.esm has MSTF on 169 tracks, always 0.
public class MusicTrackFieldTests
{
    // MUSGenesisPlanetFPaletteADayA01 (000F65:Starfield.esm): CNAM Palette, MTSH, MSTF 0
    private const string GenesisPalette = "4544494420004D555347656E65736973506C616E65744650616C657474654144617941303100434E414D040048E0D76E4D5453482800A94CCB8C79DF127C6BD25D659A5AF18A0000000000000000000000000000000000000000000000004D535446010000";

    private static IEnumerable<IMusicTrackGetter> Read(string hex)
        => ReadBoth<IMusicTrackGetter>(Record("MUST", Hex(hex)), f => MusicTrack.CreateFromBinary(f), (s, p) => MusicTrackBinaryOverlay.MusicTrackFactory(s, p));

    [Fact]
    public void GenesisPalette_Reads()
    {
        foreach (var must in Read(GenesisPalette))
        {
            must.Type.ShouldBe(MusicTrack.TypeEnum.SingleTrack);
            must.SingleTrackSound.ShouldNotBeNull();
            must.ConditionsCantFailAfterSuccess.ShouldBe(false);
        }
    }

    [Fact]
    public void GenesisPalette_WritesSameBytes()
    {
        foreach (var must in Read(GenesisPalette))
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => must.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(GenesisPalette));
        }
    }

    [Fact]
    public void ConditionsCantFailAfterSuccess_True_WritesOne()
    {
        var must = new MusicTrack(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield) { ConditionsCantFailAfterSuccess = true };
        var written = GameConstants.Starfield.MajorRecord(Write(w => must.WriteToBinary(w)));
        written.FindSubrecord(new RecordType("MSTF")).Content.ToArray().ShouldBe(new byte[] { 1 });
    }
}
