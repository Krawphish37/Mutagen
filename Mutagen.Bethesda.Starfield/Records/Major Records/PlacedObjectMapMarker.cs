namespace Mutagen.Bethesda.Starfield;

partial class PlacedObjectMapMarker
{
    // Starfield map flags (FNAM), as in xEdit's Starfield definitions; bit 8 is unused
    [Flags]
    public enum Flag
    {
        Visible = 0x01,
        CanTravelTo = 0x02,
        ShowAllIsHidden = 0x04,
        UseLocationName = 0x08,
        VisibleOnStarMap = 0x10,
        Discovered = 0x20,
        RequiresScanning = 0x40,
        AllowGravJump = 0x80,
        SuppressDiscovery = 0x200,
        UseAccurateZHeight = 0x400,
        LimitMapVisibilityToCompassRange = 0x800,
        MustCruiseToFirst = 0x1000,
    }
}