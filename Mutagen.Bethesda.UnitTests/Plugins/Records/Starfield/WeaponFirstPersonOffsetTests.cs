using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// WEAP WVIS starts with xEdit's First Person Offset, a Vec3 of floats. Mutagen read it as three UInt32
// (WVISUnknown1-3). In Starfield.esm all 406 WVIS are 24 bytes and the offset is 0 or -0.0 (00 00 00 80): 7 weapons
// hold a -0.0, which must survive a round trip. The tests read real WVIS bytes with both readers and check the
// written WVIS subrecord is byte-identical.
public class WeaponFirstPersonOffsetTests
{
    private const int NegativeZeroBits = unchecked((int)0x80000000);

    // MagShear (02EB3C:Starfield.esm): Y is -0.0
    private const string MagShearEdid = "4D61675368656172 00";
    private const string MagShearWvis = "00000000 00000080 00000000 2CD00000 FFFF7F7F 00000000";

    // MagShot (02EB42:Starfield.esm): X, Y and Z are -0.0
    private const string MagShotEdid = "4D616753686F74 00";
    private const string MagShotWvis = "00000080 00000080 00000080 2CD00000 FFFF7F7F 00000000";

    private static IEnumerable<IWeaponGetter> Read(string edid, string wvis)
        => ReadBoth<IWeaponGetter>(Record("WEAP", Subrecord("EDID", edid), Subrecord("WVIS", wvis)),
            f => Weapon.CreateFromBinary(f), (s, p) => WeaponBinaryOverlay.WeaponFactory(s, p));

    private static void WritesBackWvis(IWeaponGetter weap, string wvis)
    {
        var content = GameConstants.Starfield.MajorRecord(Write(w => weap.WriteToBinary(w))).Content.ToArray();
        var expected = Subrecord("WVIS", wvis);
        var at = content.AsSpan().IndexOf(expected);
        at.ShouldBeGreaterThanOrEqualTo(0);
        // the WVIS written is exactly the original one (same length header, same 24 bytes)
        content.AsSpan(at, expected.Length).ToArray().ShouldBe(expected);
    }

    [Fact]
    public void MagShear_NegativeZeroY()
    {
        foreach (var weap in Read(MagShearEdid, MagShearWvis))
        {
            var offset = weap.FirstPersonOffset;
            BitConverter.SingleToInt32Bits(offset.X).ShouldBe(0);
            BitConverter.SingleToInt32Bits(offset.Y).ShouldBe(NegativeZeroBits);
            BitConverter.SingleToInt32Bits(offset.Z).ShouldBe(0);
            weap.ImpactDataSet.FormKey.ID.ShouldBe(0x00D02Cu);
            weap.ColorRemappingIndex.ShouldBe(float.MaxValue);
            WritesBackWvis(weap, MagShearWvis);
        }
    }

    [Fact]
    public void MagShot_NegativeZeroXYZ()
    {
        foreach (var weap in Read(MagShotEdid, MagShotWvis))
        {
            var offset = weap.FirstPersonOffset;
            BitConverter.SingleToInt32Bits(offset.X).ShouldBe(NegativeZeroBits);
            BitConverter.SingleToInt32Bits(offset.Y).ShouldBe(NegativeZeroBits);
            BitConverter.SingleToInt32Bits(offset.Z).ShouldBe(NegativeZeroBits);
            WritesBackWvis(weap, MagShotWvis);
        }
    }
}
