using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Race mannequin skin swaps: MSSS (Gender: Male 0, Female 1) and MSSI (Skin Swap Index: Body 0, Hands 1) were raw bytes.
// Values are MannequinRace's (1EE4DA:Starfield.esm), the only race that has them: both genders swap Body and Hands
// to the same layered material swap 0F58ED.
public class MannequinSkinSwapTests
{
    // A race writes its 64 biped object names (NAME) and their conditions (RBPC); the binary overlay needs them present.
    private static byte[][] BipedObjectNames()
        => Enumerable.Range(0, 64).Select(_ => Subrecord("NAME", "00")).Append(Subrecord("RBPC", new byte[256])).ToArray();

    private static readonly string[] Types = ["MSSS", "MSSI", "MSSA", "MSSI", "MSSA", "MSSS", "MSSI", "MSSA", "MSSI", "MSSA"];
    private static readonly string[] Contents = ["00000000", "00000000", "ED580F00", "01000000", "ED580F00", "01000000", "00000000", "ED580F00", "01000000", "ED580F00"];

    [Fact]
    public void GenderAndSkinSwapIndex()
    {
        var bytes = Record("RACE", [.. BipedObjectNames(), .. Types.Select((t, i) => Subrecord(t, Contents[i]))]);
        foreach (var race in ReadBoth<IRaceGetter>(bytes, f => Race.CreateFromBinary(f), (s, p) => RaceBinaryOverlay.RaceFactory(s, p)))
        {
            race.MannequinSkinSwaps.Select(x => x.Gender).ShouldBe([MaleFemaleGender.Male, MaleFemaleGender.Female]);
            foreach (var swap in race.MannequinSkinSwaps)
            {
                swap.Items.Select(x => x.SkinSwap).ShouldBe([Race.MannequinSkinSwapType.Body, Race.MannequinSkinSwapType.Hands]);
                swap.Items.ShouldAllBe(x => x.MaterialSwap.FormKey.ID == 0x0F58ED);
            }

            var written = GameConstants.Starfield.MajorRecord(Write(w => race.WriteToBinary(w)));
            var subs = written.EnumerateSubrecords().Where(x => x.RecordTypeInt == new RecordType("MSSS").TypeInt
                || x.RecordTypeInt == new RecordType("MSSI").TypeInt || x.RecordTypeInt == new RecordType("MSSA").TypeInt).ToArray();
            subs.Select(x => x.RecordType.Type).ShouldBe(Types);
            subs.Select(x => Convert.ToHexString(x.Content.ToArray())).ShouldBe(Contents);
        }
    }
}
