using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield's biped object slots are 0-63 as named in Starfield.esm's own race NAME lists (HumanRace: 0 Hide Hair,
// 3 BODY, 17 Beard, 29 Shield, 30 BoostPackFX, 63 FaceLights ...), not Fallout 4's HairTop ... Pipboy list.
// Slot n is bit n of FirstPersonFlag.
public class BipedObjectTests
{
    // RACE DAT2 from Starfield.esm; the shield, beard and body biped objects are at bytes 64-75
    private const string HumanRaceData =
        "00-00-80-3F-00-00-80-3F-4C-A6-AA-3E-4C-A6-AA-3E-4C-A6-AA-3E-4C-A6-AA-3E-4C-A6-AA-3E-4C-A6-AA-3E-63-89-A0-10-21-40-84-00-00-00-80-3F-00-00-80-3F-01-00-00-00-02-00-00-00-00-00-00-00-0A-D7-23-3C-1D-00-00-00-11-00-00-00-03-00-00-00-00-00-F0-41-00-00-00-00-00-00-80-3E-00-00-34-42-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-96-C3-00-00-00-00-FF-FF-FF-FF-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-B4-42-00-00-B4-42-00-00-80-3E-00-00-80-3E-00-00-80-3E-00-00-00-41-00-00-40-41-0A-D7-23-3C-01-00-00-00-02-00-00-00-01-00-00-80-3F-00-00-16-44-00-00-16-44-00-00-80-3F-00-00-00-00-00-00-80-3F-00-00-80-3F";
    private const string TerrormorphElderRaceData =
        "00-00-80-3F-00-00-80-3F-4C-A6-AA-3E-4C-A6-AA-3E-4C-A6-AA-3E-4C-A6-AA-3E-4C-A6-AA-3E-4C-A6-AA-3E-39-8D-10-40-00-42-80-02-00-00-00-3F-00-00-00-3F-03-00-00-00-FF-FF-FF-FF-FF-FF-FF-FF-00-00-00-00-1E-00-00-00-FF-FF-FF-FF-03-00-00-00-00-00-00-00-00-00-00-00-00-00-40-3F-00-00-70-41-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-96-C3-00-00-00-00-FF-FF-FF-FF-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-B4-42-00-00-B4-42-CD-CC-4C-3D-00-00-40-3F-00-00-00-3F-00-00-20-41-00-00-80-41-CD-CC-CC-3D-03-00-00-00-03-00-00-00-01-00-00-80-3F-00-00-A0-40-00-00-20-41-00-00-80-3F-00-00-00-00-00-00-80-3F-00-00-80-3F";

    private static IEnumerable<IRaceGetter> ReadRace(string dat2) =>
        ReadBoth<IRaceGetter>(Record("RACE", Subrecord("DAT2", dat2)), f => Race.CreateFromBinary(f), (s, p) => RaceBinaryOverlay.RaceFactory(s, p));

    [Fact]
    public void HumanRace_ShieldBeardBody()
    {
        // HumanRace (0000347D): shield 29, beard 17, body 3
        foreach (var race in ReadRace(HumanRaceData))
        {
            race.ShieldBipedObject.ShouldBe(BipedObject.Shield);
            race.BeardBipedObject.ShouldBe(BipedObject.Beard);
            race.BodyBipedObject.ShouldBe(BipedObject.BODY);
        }
    }

    [Fact]
    public void TerrormorphElderRace_BoostPackFxAsShield()
    {
        // TerrormorphElderRace (3E4F51): shield slot 30 = BoostPackFX, beard none
        foreach (var race in ReadRace(TerrormorphElderRaceData))
        {
            race.ShieldBipedObject.ShouldBe(BipedObject.BoostPackFX);
            race.BeardBipedObject.ShouldBe(BipedObject.None);
        }
    }

    [Theory]
    [InlineData(0, BipedObject.HideHair, FirstPersonFlag.HideHair)]
    [InlineData(17, BipedObject.Beard, FirstPersonFlag.Beard)]
    [InlineData(30, BipedObject.BoostPackFX, FirstPersonFlag.BoostPackFX)]
    [InlineData(48, BipedObject.SSEyes, FirstPersonFlag.SSEyes)]
    public void Slot_ObjectAndFlag(int slot, BipedObject obj, FirstPersonFlag flag)
    {
        BipedObjectConverter.BipedObjectBySlot(slot).ShouldBe(obj);
        BipedObjectConverter.FirstPersonFlagBySlot(slot).ShouldBe(flag);
        obj.ToSlot().ShouldBe(slot);
        obj.ToFlagEnum().ShouldBe(flag);
    }

    [Fact]
    public void Slot_None()
    {
        BipedObjectConverter.BipedObjectBySlot(-1).ShouldBe(BipedObject.None);
        BipedObject.None.ToSlot().ShouldBe(-1);
        Should.Throw<ArgumentException>(() => BipedObjectConverter.BipedObjectBySlot(64));
    }
}
