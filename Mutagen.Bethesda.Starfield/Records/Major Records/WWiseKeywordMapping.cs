namespace Mutagen.Bethesda.Starfield;

public partial class WWiseKeywordMapping
{
    // WMTI (xEdit). It also decides what each WMSI sound ID means.
    public enum MappingTemplateType
    {
        ShipEngine = 0,
        Cockpit = 1,
        GravDrive = 2,
        CreatureAnim = 3,
    }
}

public partial class WWiseKeywordMappingItem
{
    // WMSI (xEdit "Sound ID"). xEdit reads it as a union picked by the record's MappingTemplate (WMTI), but the
    // templates use disjoint values, so one enum holds them all:
    // ShipEngine 0-2, Cockpit 3, GravDrive 4-7 and 9, CreatureAnim 8.
    public enum SoundIdType : ushort
    {
        MainLoop = 0,
        Interior = 1,
        Afterburner = 2,
        Cockpit = 3,
        GravJumpPending = 4,
        GravJumpAnimStarted = 5,
        GravJumpComplete = 6,
        GravJumpWarmup = 7,
        CreatureAnim = 8,
        GravJumpInitiate = 9,
    }
}
