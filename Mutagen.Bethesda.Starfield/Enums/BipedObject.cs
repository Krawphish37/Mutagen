namespace Mutagen.Bethesda.Starfield;

/// <summary>
/// Starfield's biped object slots 0-63, as named in Starfield.esm itself (a race's NAME list, e.g. HumanRace) and in
/// xEdit's Starfield definitions. Slot n is bit n of <see cref="FirstPersonFlag"/>. Slots 21, 28, 31, 55-58 and
/// 60-62 have no name.
/// </summary>
public enum BipedObject
{
    None = -1,
    HideHair = 0,
    MorphHair = 1,
    HideHead = 2,
    BODY = 3,
    Hat = 4,
    Backpack = 5,
    Gloves = 6,
    LowerBodyVisOnly = 7,
    SleevesVisOnly = 8,
    HeadVisOnly = 9,
    UpperBody1VisOnly = 10,
    UpperBody2VisOnly = 11,
    UpperBody3VisOnlyNoFirstPerson = 12,
    Misc1VisOnly = 13,
    Misc2VisOnly = 14,
    Misc3VisOnly = 15,
    Eyes = 16,
    Beard = 17,
    Neuroamp = 18,
    HideEar = 19,
    AddonRig = 20,
    Chronomark = 22,
    Chronomark1 = 23,
    Chronomark2 = 24,
    Chronomark3 = 25,
    Eyepatch = 26,
    MorphBeard = 27,
    Shield = 29,
    BoostPackFX = 30,
    SSHideHair = 32,
    SSMorphHair = 33,
    SSHideHead = 34,
    SSBODY = 35,
    SSHelmet = 36,
    SSBackpack = 37,
    SSGloves = 38,
    SSLowerBody = 39,
    SSSleeves = 40,
    SSHeadVis = 41,
    SSUBody1 = 42,
    SSUBody2 = 43,
    SSUBody3 = 44,
    SSMisc1 = 45,
    SSMisc2 = 46,
    SSMisc3 = 47,
    SSEyes = 48,
    SSBeard = 49,
    SSNeuroamp = 50,
    SSHideEar = 51,
    SSAddonRig = 52,
    SSBackpackMisc = 53,
    SSChronoMark = 54,
    SSMorphBeard = 59,
    FaceLights = 63,
}

public static class BipedObjectConverter
{
    /// <summary>
    /// returns the biped object of a Starfield slot number (0-63, as xEdit shows "0 - Hide Hair"),
    /// returns 'None' when called with -1,
    /// throws an exception for any other number
    /// </summary>
    /// <param name="slot">the slot to convert</param>
    /// <returns>the biped object of the slot, e.g. 'Beard' for 17, 'None' for -1</returns>
    public static BipedObject BipedObjectBySlot(int slot)
    {
        if (slot == -1)
            return BipedObject.None;
        if (slot is < 0 or > 63)
            throw new ArgumentException($"Slot '{slot}' not found");
        return (BipedObject)slot;
    }

    /// <summary>
    /// returns the first person flag (armor slot flag) of a Starfield slot number (0-63),
    /// throws an exception for any other number
    /// </summary>
    /// <param name="slot">the slot to convert</param>
    /// <returns>the flag of the slot, e.g. 'Beard' (bit 17) for 17</returns>
    public static FirstPersonFlag FirstPersonFlagBySlot(int slot)
    {
        if (slot is < 0 or > 63)
            throw new ArgumentException($"Slot '{slot}' not found");
        return (FirstPersonFlag)(1UL << slot);
    }
}

public static class BipedObjectExtensions
{
    /// <summary>
    /// converts the biped object to the corresponding first person flag (bit n for slot n), throws an exception when
    /// called with 'None'
    /// </summary>
    public static FirstPersonFlag ToFlagEnum(this BipedObject bipedObject)
    {
        if (bipedObject == BipedObject.None)
            throw new ArgumentException("Object enum 'None' does not exist as flag enum");
        return BipedObjectConverter.FirstPersonFlagBySlot((int)bipedObject);
    }

    /// <summary>
    /// returns the slot number of the biped object - e.g. 17 for 'Beard', -1 for 'None'
    /// </summary>
    public static int ToSlot(this BipedObject bipedObject)
    {
        return bipedObject == BipedObject.None ? -1 : (int)bipedObject;
    }
}
