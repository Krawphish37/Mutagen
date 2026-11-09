using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// OMOD DATA: the two bytes after the target name are xEdit's Max Rank and Level Tier Scaled Offset. Mutagen read them
// as one UInt16 (Unknown2), and the overlay also had MaxRank/LevelTierScaledOffset getters at Fallout 4's fixed
// offsets 0xE/0xF, which fall inside Starfield's name string: on a weapon mod they returned 84 and 69 ("T", "E").
public class ObjectModificationMaxRankTests
{
    // DONOTUSE_CUTmod_Grendel_LightLaser_LaserSightFlashlight_Combo (OMOD 0191F6): attach point ap_gun_Laser.
    // Every OMOD in Starfield.esm stores 0 and 0 here.
    private const string Data = "00-00-00-00-00-00-00-00-00-00-1B-00-00-00-54-45-53-4F-62-6A-65-63-74-57-45-41-50-5F-49-6E-73-74-61-6E-63-65-44-61-74-61-00-00-00-EE-91-01-00-00-00-00-00-00-00-00-00";

    [Theory]
    [InlineData("00", "00", 0, 0)]
    // The same record with the two bytes set (not from Starfield.esm), to check that each byte lands in its own field
    [InlineData("03", "02", 3, 2)]
    public void ReadsBothBytes(string maxRank, string levelTier, byte expectedRank, byte expectedTier)
    {
        var data = Data.Substring(0, 41 * 3) + maxRank + "-" + levelTier + Data.Substring(43 * 3 - 1);
        foreach (var omod in ReadBoth<IWeaponModificationGetter>(Record("OMOD", Subrecord("DATA", data)), f => WeaponModification.CreateFromBinary(f), (s, p) => WeaponModificationBinaryOverlay.WeaponModificationFactory(s, p)))
        {
            omod.MaxRank.ShouldBe(expectedRank);
            omod.LevelTierScaledOffset.ShouldBe(expectedTier);
            omod.AttachPoint.FormKey.ID.ShouldBe(0x0191EEu);
            var written = GameConstants.Starfield.MajorRecord(Write(w => omod.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("DATA")).Content.ToArray().ShouldBe(Hex(data));
        }
    }
}
