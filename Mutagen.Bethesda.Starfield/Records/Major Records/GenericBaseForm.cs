namespace Mutagen.Bethesda.Starfield;

partial class GenericBaseForm
{
    // Record header flags, xEdit's GBFM list (Unique: 143 GBFM in all plugins, 49 in Starfield.esm)
    [Flags]
    public enum MajorFlag
    {
        Unique = 0x0000_0004,
    }
}
