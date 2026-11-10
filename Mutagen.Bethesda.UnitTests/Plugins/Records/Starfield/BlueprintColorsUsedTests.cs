using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Starfield;
using Noggog;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// GBFM blueprint part BODV = three colors + a u32 that says which of them the part uses (bits 1, 2, 4; xEdit
// "known values 0 - 7"). In Starfield.esm the bits match the non-black colors on 33,404 of 33,560 items; the other
// 156 have a bit set while that color is black. Bytes from Cruiser_C_Nova_Kirov01_Template (02834B:Starfield.esm).
public class BlueprintColorsUsedTests
{
    private const string Grey = "8988083E C1C0403E D9D8583E";
    private const string Black = "00000000 00000000 00000000";
    private const string White = "0000803F 0000803F 0000803F";

    private static byte[] Item(string name, string bodv) =>
        Subrecord("BODS", System.Text.Encoding.ASCII.GetBytes(name + "\0")).Concat(Subrecord("BODV", bodv)).ToArray();

    private static IEnumerable<IBlueprintComponentBODSItemGetter> Read(byte[] bytes) =>
        ReadBoth(
            bytes,
            f => BlueprintComponentBODSItem.CreateFromBinary(f),
            (s, p) => BlueprintComponentBODSItemBinaryOverlay.BlueprintComponentBODSItemFactory(s, p));

    public static IEnumerable<object[]> Cases() => new[]
    {
        // ECLIPTIC: all three colors set, flags 7
        new object[] { "ECLIPTIC", $"{Grey} {Grey} {Grey} 07000000",
            BlueprintComponentBODSItem.ColorFlag.Color1 | BlueprintComponentBODSItem.ColorFlag.Color2 | BlueprintComponentBODSItem.ColorFlag.Color3 },
        // DEFAULT: Color2 black but its bit set, Color3 white, flags 6
        new object[] { "DEFAULT", $"{Black} {Black} {White} 06000000",
            BlueprintComponentBODSItem.ColorFlag.Color2 | BlueprintComponentBODSItem.ColorFlag.Color3 },
        // DEFAULT: all black, Color2 bit set, flags 2
        new object[] { "DEFAULT", $"{Black} {Black} {Black} 02000000",
            BlueprintComponentBODSItem.ColorFlag.Color2 },
        // ECLIPTIC: Color1 only, flags 1
        new object[] { "ECLIPTIC", $"{Grey} {Black} {Black} 01000000",
            BlueprintComponentBODSItem.ColorFlag.Color1 },
        // all black, no flags
        new object[] { "DEFAULT", $"{Black} {Black} {Black} 00000000",
            (BlueprintComponentBODSItem.ColorFlag)0 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ColorsUsed_ReadsAndWritesByteIdentical(string name, string bodv, BlueprintComponentBODSItem.ColorFlag expected)
    {
        var bytes = Item(name, bodv);
        foreach (var item in Read(bytes))
        {
            item.Name.ShouldBe(name);
            item.ColorsUsed.ShouldBe(expected);
            Write(w => item.WriteToBinary(w)).ShouldBe(bytes);
            Write(w => item.DeepCopy().WriteToBinary(w)).ShouldBe(bytes);
        }
    }

    [Fact]
    public void ColorsUsed_KeepsBitsOutsideTheEnum()
    {
        // Not seen in Bethesda plugins (0-7 only), but a value above 7 must survive a round trip
        var bytes = Item("DEFAULT", $"{Black} {Black} {Black} 09000000");
        foreach (var item in Read(bytes))
        {
            ((int)item.ColorsUsed).ShouldBe(9);
            Write(w => item.WriteToBinary(w)).ShouldBe(bytes);
        }
    }
}
