namespace Mutagen.Bethesda.Starfield;

/// <summary>
/// XALG flags, on base objects and placed references.  Names are xEdit's (its list is from Fallout 76, marked
/// "probably wrong"); in Starfield.esm the checkable ones fit: Disable Physics Sim on 933 books and 233 keys,
/// References Are Moveable on projectiles and explosions, Disable Audio Occlusion/Obstruction on sound markers,
/// Non-Interactive on references and actors.  Only bits 0 to 11 occur in Starfield.esm (50,046 XALG).
/// </summary>
[Flags]
public enum XalgFlag : ulong
{
    /// <summary>18,405 in Starfield.esm, all references</summary>
    DisallowPermanentProjectedDecals = 0x1,
    /// <summary>Not in Starfield.esm</summary>
    Unknown1 = 0x2,
    /// <summary>1,602 in Starfield.esm (REFR, STAT); unnamed in xEdit</summary>
    Unknown2 = 0x4,
    DisablePhysicsSim = 0x8,
    /// <summary>23 in Starfield.esm (GMST, DFOB); unnamed in xEdit</summary>
    Unknown4 = 0x10,
    DisableAudioOcclusionObstruction = 0x20,
    IgnoreAudioObstructionHits = 0x40,
    /// <summary>36 in Starfield.esm (REFR); unnamed in xEdit</summary>
    Unknown7 = 0x80,
    HighCostPath = 0x100,
    NonInteractive = 0x200,
    ReferencesAreMoveable = 0x400,
    DisableLoopSound = 0x800,
    /// <summary>Not in Starfield.esm</summary>
    Unknown12 = 0x1000,
    /// <summary>Not in Starfield.esm</summary>
    Unknown13 = 0x2000,
}
