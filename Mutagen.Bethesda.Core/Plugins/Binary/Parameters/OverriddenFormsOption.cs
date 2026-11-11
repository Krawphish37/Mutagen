namespace Mutagen.Bethesda.Plugins.Binary.Parameters;

/// <summary>
/// Flag to specify what logic to use to ensure a mod's overridden form list in the mod header is accurate
/// </summary>
public enum OverriddenFormsOption
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
    /// The game's default. Starfield: the list as the mod holds it while the records it covers are the ones the mod
    /// was read with (so a plugin read and written back keeps the Creation Kit's list, order and all), updated by the
    /// difference when they changed, and computed as <see cref="Iterate"/> for a mod not read from a file.
    /// Other games: <see cref="NoCheck"/>.
    /// </summary>
    Auto,
}