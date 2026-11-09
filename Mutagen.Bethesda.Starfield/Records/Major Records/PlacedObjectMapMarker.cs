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

    // TNAM (xEdit; 63 has no name)
    public enum MarkerType
    {
        AkilaCity = 0,
        Cydonia = 1,
        Dazra = 2,
        Neon = 3,
        NewAtlantis = 4,
        RyujinIndustries = 5,
        ScaledCitadel = 6,
        TheLodge = 7,
        TheMAST = 8,
        TheRock = 9,
        Cave = 10,
        MiningBase = 11,
        Outpost = 12,
        ScienceLab = 13,
        NaturalLandmark = 14,
        MechGraveyard = 15,
        FracturedEarth = 16,
        MountainPeak = 17,
        CrystalGrotto = 18,
        CrashedStarship = 19,
        Farm = 20,
        Industrial = 21,
        MilitaryBase = 22,
        NASA = 23,
        StarbornTemple = 24,
        CoralColony = 25,
        FossilOutcropping = 26,
        AnimalDen = 27,
        FrozenWatersource = 28,
        Geysers = 29,
        BoilingHotsprings = 30,
        AcidFog = 31,
        RadiationZone = 32,
        StarStation = 33,
        StarYard = 34,
        Settlement = 35,
        LegendaryShip = 36,
        TheVigilance = 37,
        DerelictShip = 38,
        SpaceGraveyard = 39,
        IcyAsteroids = 40,
        RockyAsteroids = 41,
        TheUnity = 42,
        TheEye = 43,
        TheKey = 44,
        POI = 45,
        DebrisField = 46,
        Ship = 47,
        SurfaceSettlement = 48,
        DistressCall = 49,
        StarbornTempleObelisk = 50,
        Bar = 51,
        Food = 52,
        Vendor = 53,
        ArmorVendor = 54,
        WeaponVendor = 55,
        AidVendor = 56,
        Surgery = 57,
        Realtor = 58,
        PlayerHouse = 59,
        Transit = 60,
        TheWell = 61,
        TrackersAlliance = 62,
        ShrineDevotion = 64,
        ShrineObedience = 65,
        ShrinePerseverance = 66,
        ShrineRedemption = 67,
        ShrineStrength = 68,
        ShrineGeneric = 69,
    }

    // VNAM (xEdit)
    public enum MarkerCategory
    {
        None = 0,
        Landmark = 1,
        Structure = 2,
        LifeSigns = 3,
        Hazard = 4,
        SpaceLandmark = 5,
        Ship = 6,
        Station = 7,
        Asteroids = 8,
        Simple = 9,
    }

    // VISI (xEdit)
    public enum Visibility
    {
        Always = 0,
        System = 1,
        Planet = 2,
    }
}
