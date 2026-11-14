namespace Mutagen.Bethesda.Plugins.Binary.Parameters;

/// <summary>
/// What reading does with a plugin whose records share a FormKey (two records whose stored FormIDs read as the same
/// FormKey, e.g. MissionsGalore.esm's GLOB 02000867 and GLOB 03000867, or GLOB 03000869 and TMLM 02000869)
/// </summary>
public enum DuplicateFormKeyOption
{
    /// <summary>
    /// The plugin is refused when it is opened: DuplicateFormKeyException names it and every clashing pair. A load
    /// order drops it (BinaryReadParameters.LoadOrderReadFailures) and loads the rest.
    /// </summary>
    Refuse,

    /// <summary>
    /// For tools that look inside a broken plugin: it is read as the game reads it, the later record of each pair in its
    /// group and the earlier one kept aside (DuplicateRecords.GetShadowedDuplicates) and written back in its place
    /// </summary>
    KeepLater,
}
