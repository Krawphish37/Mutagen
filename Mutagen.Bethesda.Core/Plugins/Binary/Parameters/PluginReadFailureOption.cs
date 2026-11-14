namespace Mutagen.Bethesda.Plugins.Binary.Parameters;

/// <summary>
/// What importing a load order does with a plugin that can't be read
/// </summary>
public enum PluginReadFailureOption
{
    /// <summary>
    /// The plugin is left unloaded (its listing has no mod) and its error is listed (LoadOrderErrors.GetLoadErrors);
    /// plugins that have it as a master are left unloaded too, and the rest loads
    /// </summary>
    DropPlugin,

    /// <summary>
    /// The import throws the plugin's error, as before
    /// </summary>
    Throw,
}
