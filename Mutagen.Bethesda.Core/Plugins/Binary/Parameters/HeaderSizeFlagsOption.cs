namespace Mutagen.Bethesda.Plugins.Binary.Parameters;

/// <summary>
/// Flag to specify what logic to use to set a mod's size flags (Light/Small, Medium, or neither: Full) and its Master
/// (ESM) flag in the mod header
/// </summary>
public enum HeaderSizeFlagsOption
{
    /// <summary>
    /// Do no check: the flags are written as the mod holds them (the FormID compaction check still applies)
    /// </summary>
    NoCheck,

    /// <summary>
    /// The game's default. Starfield: the size the mod holds (as set, or as read from its file) is kept, and raised to
    /// the smallest size that fits only when the mod's own records don't fit it (Small holds object IDs up to 0xFFF,
    /// Medium up to 0xFFFF; a size is never lowered); a mod not read from a file also gets the Master flag when its ModKey
    /// is an .esm or .esl. Other games: <see cref="NoCheck"/>.
    /// </summary>
    Auto,

    /// <summary>
    /// The smallest size whose object-ID range holds the mod's own records (Small up to 0xFFF, Medium up to 0xFFFF, else
    /// Full), whatever the mod holds; a mod not read from a file also gets the Master flag when its ModKey is an .esm or
    /// .esl. For a new patch: a new .esm with only a few records of its own is written Master + Light.
    /// </summary>
    Smallest,

    /// <summary>
    /// Write the mod as a Small (Light) master (games with small masters)
    /// </summary>
    Small,

    /// <summary>
    /// Write the mod as a Medium master (games with medium masters)
    /// </summary>
    Medium,

    /// <summary>
    /// Write the mod with neither size flag
    /// </summary>
    Full,
}
