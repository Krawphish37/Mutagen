namespace Mutagen.Bethesda.Starfield;

partial class Projectile
{
    // PROD Flags, one UInt32 (xEdit). PenetratesGeometry keeps its name (xEdit: Penetrates Through Actors).
    [Flags]
    public enum Flag
    {
        Hitscan = 0x0001,
        Explosion = 0x0002,
        AltTrigger = 0x0004,
        MuzzleFlash = 0x0008,
        CanBeDisabled = 0x0020,
        CanBePickedUp = 0x0040,
        Supersonic = 0x0080,
        PinsLimbs = 0x0100,
        PassThroughSmallTransparent = 0x0200,
        DisableCombatAimCorrection = 0x0400,
        PenetratesGeometry = 0x0800,
        ContinuousUpdate = 0x1000,
        SeeksTarget = 0x2000,
        PredictiveAimAssist = 0x4000,
        AlignOnProjectile = 0x8000,
        ContinuousImpacts = 0x1_0000,
        NoExplosiveIndicator = 0x2_0000,
        VariableIntensity = 0x4_0000,
        ApplyChargeMultiplier = 0x8_0000,
    }

    // PROD Type, one byte (xEdit's Starfield list; Fallout 4's used bit values)
    public enum TypeEnum
    {
        Missile = 0,
        Lobber = 1,
        Beam = 2,
        Flame = 3,
        Cone = 4,
        Barrier = 5,
        Arrow = 6,
        Plasma = 7,
        Emitter = 8,
    }

    public enum SoundActivationConditionOption
    {
        PlayOnSpawn = 0,
        PlayOnNearMissPlayer = 1,
        Default = 2,
    }
}
