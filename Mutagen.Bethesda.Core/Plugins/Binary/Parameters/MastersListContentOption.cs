namespace Mutagen.Bethesda.Plugins.Binary.Parameters;

/// <summary>
/// Flag to specify what logic to use to keep a mod's master list keys in sync<br/>
/// This setting is just used to sync the contents of the list, not the order
/// </summary>
public enum MastersListContentOption
{
    /// <summary>
    /// Do no check
    /// </summary>
    NoCheck,

    /// <summary>
    /// Iterate source mod
    /// </summary>
    Iterate,

    /// <summary>
    /// The game's default. Starfield: a mod read from a file (or given its read state through RoundTripMixIn) keeps every
    /// master it was read with, referenced or not (the Creation Kit and xEdit leave them in, and dropping one moves every
    /// master index after it); masters its records now reference are added. Other games, and a mod not read from a
    /// file: <see cref="Iterate"/>.
    /// </summary>
    Auto,
}