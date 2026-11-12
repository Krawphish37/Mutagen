namespace Mutagen.Bethesda.Starfield;

partial class AQuestAlias
{
    [Flags]
    public enum Flag : uint
    {
        ReservesLocationOrReference = 0x0000_0001,
        Optional = 0x0000_0002,
        QuestObject = 0x0000_0004,
        AllowReuseInQuest = 0x0000_0008,
        AllowDead = 0x0000_0010,
        MatchingRefInLoadedArea = 0x0000_0020,
        Essential = 0x0000_0040,
        AllowDisabled = 0x0000_0080,
        StoresText = 0x0000_0100,
        AllowReserved = 0x0000_0200,
        Protected = 0x0000_0400,
        ForcedByAliases = 0x0000_0800,
        AllowDestroyed = 0x0000_1000,
        MatchingRefClosest = 0x0000_2000,
        UsesStoredText = 0x0000_4000,
        InitiallyDisabled = 0x0000_8000,
        AllowCleared = 0x0001_0000,
        ClearNamesWhenRemoved = 0x0002_0000,
        MatchingRefForActorsOnly = 0x0004_0000,
        CreateRefTemp = 0x0008_0000,
        ExternalAliasLinked = 0x0010_0000,
        NoPickpocket = 0x0020_0000,
        CanApplyDataToNonAliasedRefs = 0x0040_0000,
        IsCompanion = 0x0080_0000,
        OptionalAllScenes = 0x0100_0000,
        // Bits 25, 26 and 31 are reference alias flags, 27-30 location alias flags (xEdit names)
        MatchingRefFromCrowd = 0x0200_0000,
        QuestObjectCargo = 0x0400_0000,
        SearchShipLocations = 0x0800_0000,
        MatchingLocPlanetsAndSystemsOnly = 0x1000_0000,
        CurrentAliasSystem = 0x2000_0000,
        CurrentAliasPlanet = 0x4000_0000,
        Consumable = 0x8000_0000,
    }
}