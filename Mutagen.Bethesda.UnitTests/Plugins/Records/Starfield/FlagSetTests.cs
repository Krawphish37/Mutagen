using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// FirstPersonFlag (armor slots, BO64) and FurnitureMarkerFlags (MNAM) are sets of flags. FirstPersonFlag bit n is
// biped slot n: SS Morph Beard is bit 59 and FaceLights bit 63, as Starfield.esm's own race NAME list and xEdit
// name them. Starfield.esm: bit 63 is set on the 11 armor addons named ..._FaceLights; bit 59 only on
// AA_Spacesuit_FlightCap_01 (its clothes version AA_Clothes_FlightCap_01 uses bit 27, Morph Beard).
public class FlagSetTests
{
    private static IEnumerable<IArmorAddonGetter> ReadArmorAddon(string bo64) =>
        ReadBoth<IArmorAddonGetter>(Record("ARMA", Subrecord("BO64", bo64)), f => ArmorAddon.CreateFromBinary(f), (s, p) => ArmorAddonBinaryOverlay.ArmorAddonFactory(s, p));

    [Fact]
    public void FaceLights_IsBit63()
    {
        // AA_Spacesuit_UCMarine_FaceLights (ARMA 3D0266)
        foreach (var addon in ReadArmorAddon("00-00-00-00-00-00-00-80"))
        {
            addon.FirstPersonFlags.ShouldBe(FirstPersonFlag.FaceLights);
        }
    }

    [Fact]
    public void FlightCap_SsSlots()
    {
        // AA_Spacesuit_FlightCap_01 (ARMA 1E7E74): slots 32, 51, 59
        foreach (var addon in ReadArmorAddon("00-00-00-00-01-00-08-08"))
        {
            addon.FirstPersonFlags.ShouldBe(FirstPersonFlag.SSHideHair | FirstPersonFlag.SSHideEar | FirstPersonFlag.SSMorphBeard);
            addon.FirstPersonFlags.ToString().ShouldBe("SSHideHair, SSHideEar, SSMorphBeard");
        }
    }

    [Fact]
    public void FirstPersonFlag_IsAFlagSet()
    {
        typeof(FirstPersonFlag).IsDefined(typeof(FlagsAttribute), false).ShouldBeTrue();
        ((ulong)FirstPersonFlag.SSMorphBeard).ShouldBe(1UL << 59);
        ((ulong)FirstPersonFlag.FaceLights).ShouldBe(1UL << 63);
    }

    [Fact]
    public void FurnitureMarkerFlags_IsAFlagSet()
    {
        // The most common combination in Starfield.esm's furniture MNAM (57 records): 0x40000003
        var bytes = Record("FURN", Subrecord("MNAM", "03-00-00-40"));
        foreach (var furniture in ReadBoth<IFurnitureGetter>(bytes, f => Furniture.CreateFromBinary(f), (s, p) => FurnitureBinaryOverlay.FurnitureFactory(s, p)))
        {
            furniture.MarkerFlags!.Value.ToString().ShouldBe("InteractionPoint0, InteractionPoint1, HasModel");
        }
    }

    [Fact]
    public void FaceLights_Writes()
    {
        var addon = new ArmorAddon(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            FirstPersonFlags = FirstPersonFlag.FaceLights,
        };
        var written = GameConstants.Starfield.MajorRecord(Write(w => addon.WriteToBinary(w)));
        BitConverter.ToUInt64(written.FindSubrecord(new RecordType("BO64")).Content.Slice(0, 8)).ShouldBe(1UL << 63);
    }
}
