using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield's object palette footprint sizes are X-Large 0, Large 1, Medium 2, Small 3 and None 5 (no 4).
public class FootprintSizeTests
{
    // OPDS of ModelA_LaserTracker (WEAP 3B9AB4) in Starfield.esm: footprint 5 = None
    private const string LaserTrackerOpds =
        "00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-05-00-00-00-00-00-80-3F-00-00-80-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-80-3F-00-00-00-00-00-00-80-3F-00-00-80-3F-00-00-00-00-00-00-00-00-FF-FF-7F-7F";

    [Fact]
    public void Weapon_FootprintFive_ReadsAsNone()
    {
        var bytes = Record("WEAP", Subrecord("OPDS", LaserTrackerOpds));
        foreach (var weapon in ReadBoth<IWeaponGetter>(bytes,
                     f => Weapon.CreateFromBinary(f),
                     (s, p) => WeaponBinaryOverlay.WeaponFactory(s, p)))
        {
            weapon.ObjectPaletteDefaults!.FootprintSize.ShouldBe(ObjectPaletteDefaults.FootprintSizes.None);
        }
    }

    [Theory]
    [InlineData(ObjectPaletteDefaults.FootprintSizes.XLarge, 0)]
    [InlineData(ObjectPaletteDefaults.FootprintSizes.Small, 3)]
    [InlineData(ObjectPaletteDefaults.FootprintSizes.None, 5)]
    public void Weapon_Footprint_WritesStarfieldValue(ObjectPaletteDefaults.FootprintSizes size, byte expected)
    {
        var weapon = new Weapon(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            ObjectPaletteDefaults = new ObjectPaletteDefaults { FootprintSize = size },
        };
        var written = GameConstants.Starfield.MajorRecord(Write(w => weapon.WriteToBinary(w)));
        written.FindSubrecord(new RecordType("OPDS")).Content.Slice(16, 4).ToArray()
            .ShouldBe(new byte[] { expected, 0, 0, 0 });
    }
}
