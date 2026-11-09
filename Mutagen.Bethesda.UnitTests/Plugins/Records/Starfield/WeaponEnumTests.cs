using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// WEAP fields that were plain numbers named Unknown: WGEN Type, WMEL Attack Speed, WAUD Melee Detection Sound Level (xEdit).
// The subrecords are copied whole from Starfield.esm. Starfield.esm uses Type 0-3 and 9-12 and every attack speed.
public class WeaponEnumTests
{
    public record Case(string Name, string Wgen, string Wmel, string Waud, Weapon.AnimationTypes Type, Weapon.AttackSpeedEnum Speed, SoundLevel Level, SoundLevel MeleeLevel);

    public static readonly TheoryData<string> Cases = new() { "Grendel", "CombatKnife" };

    private static readonly Dictionary<string, Case> Data = new()
    {
        // Grendel (028A02:Starfield.esm)
        ["Grendel"] = new("Grendel", "09-00-00-00-9A-99-29-40-FB-04-00-00-00-00-80-3F-00-00-60-42-27-CD-25-00", "02-00-00-40-41-00-00-80-3F-02-00-00-00", "00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-32-42-98-A1-94-7A-4A-D2-27-37-6F-A6-08-E0-F6-BC-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-B9-43-89-90-65-37-DF-4E-67-39-44-12-BB-16-F3-A2-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-B8-4C-39-16-9B-A7-65-24-7D-5E-DD-F4-30-4D-52-A8-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-04-00-00-00-02-00-00-00",
            Weapon.AnimationTypes.Gun, Weapon.AttackSpeedEnum.Medium, SoundLevel.Loud, SoundLevel.Quiet),
        // CombatKnife (035A48:Starfield.esm)
        ["CombatKnife"] = new("CombatKnife", "02-00-00-00-EC-51-B8-3E-F9-01-00-00-00-00-80-3F-00-00-20-42-00-00-00-00", "04-00-00-00-00-9A-99-19-3F-01-00-00-00", "DB-47-F4-C3-FA-B9-6D-73-84-B5-E1-A1-8E-B0-39-87-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-A9-4C-A4-50-C2-E9-4C-E0-16-59-A2-25-B0-63-F0-A2-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-FB-4C-12-62-93-0E-7B-27-53-B6-81-79-33-A7-A5-B4-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-01-00-00-00-01-00-00-00",
            Weapon.AnimationTypes.OneHandDagger, Weapon.AttackSpeedEnum.VeryFast, SoundLevel.Silent, SoundLevel.Silent),
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ReadsAndWrites(string name)
    {
        var c = Data[name];
        var bytes = Record("WEAP", Subrecord("WAUD", c.Waud), Subrecord("WGEN", c.Wgen), Subrecord("WMEL", c.Wmel));
        foreach (var weap in ReadBoth<IWeaponGetter>(bytes, f => Weapon.CreateFromBinary(f), (s, p) => WeaponBinaryOverlay.WeaponFactory(s, p)))
        {
            weap.AnimationType.ShouldBe(c.Type);
            weap.AttackSpeed.ShouldBe(c.Speed);
            weap.SoundLevel.ShouldBe(c.Level);
            weap.MeleeDetectionSoundLevel.ShouldBe(c.MeleeLevel);
            var written = GameConstants.Starfield.MajorRecord(Write(w => weap.WriteToBinary(w)));
            foreach (var (sig, hex) in new[] { ("WAUD", c.Waud), ("WGEN", c.Wgen), ("WMEL", c.Wmel) })
            {
                written.FindSubrecord(new RecordType(sig)).Content.ToArray().ShouldBe(Hex(hex), sig);
            }
        }
    }
}
