namespace Mutagen.Bethesda.Plugins.Binary.Parameters;

/// <summary>
/// Flag to specify what logic to use to keep a mod header's next formID field in sync
/// </summary>
public enum NextFormIDOption
{
    /// <summary>
    /// Do no check
    /// </summary>
    NoCheck,

    /// <summary>
    /// Iterate source mod and locate the maximum formID present
    /// </summary>
    Iterate,

    /// <summary>
    /// The game's default. Starfield: a mod read from a file keeps the value while it has no own record above the highest
    /// it was read with, and also when the value is the Creation Kit's (FF001000: high byte FF) or still above every own
    /// ID; otherwise, and for a mod not read from a file, as <see cref="Iterate"/>. Other games: <see cref="Iterate"/>.
    /// </summary>
    Auto,
}