namespace Mutagen.Bethesda.Plugins.Binary.Parameters;

/// <summary>
/// Flag to specify what logic to use to keep a mod's record count in sync
/// </summary>
public enum RecordCountOption
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
    /// The game's default. Starfield: a mod read from a file keeps its count when it has the same number of records and
    /// moves it by the difference otherwise (the file's convention: the Creation Kit counts records + 1, xEdit records +
    /// groups); a mod not read from a file gets the Creation Kit's count (records + 1). Other games: <see cref="Iterate"/>.
    /// </summary>
    Auto,
}