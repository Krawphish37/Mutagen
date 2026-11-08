namespace Mutagen.Bethesda.Starfield;

partial class Light
{
    [Flags]
    public enum MajorFlag
    {
        NonPlayable = 0x0000_0004,
        HiddenFromLocalMap = 0x0000_0200,
        AdaptiveLighting = 0x0000_8000,
        RandomAnimStart = 0x0001_0000,
        HasCurrents = 0x0008_0000,
        Obstacle = 0x0200_0000,
        PortalStrict = 0x1000_0000
    }

    // Starfield's light flags (DAT2), as in xEdit's Starfield definitions; bit 0 has no name
    [Flags]
    public enum Flag
    {
        CanBeCarried = 0x0000_0002,
        OffByDefault = 0x0000_0010,
        DisableSpecular = 0x0000_0020,
        DisableDistanceAttenuation = 0x0000_0040,
        IsDirectSpotlight = 0x0000_0080,
        UsePbrValue = 0x0000_0100,
        FocusSpotlightBeam = 0x0000_0200,
        ExternallyControlled = 0x0000_1000,
    }

    public enum LightType
    {
        Omnidirectional,
        ShadowSpotlight,
        NonShadowSpotlight
    }

    public enum FlickerEffectOption
    {
        None,
        Flicker,
        Pulse
    }

    public enum LightLayer
    {
        Default = 0x1,
        Helmet = 0x2,
        ShipInterior = 0x4,
        ShipExterior = 0x8,
        All = 0x10,
    }
}