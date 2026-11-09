namespace Mutagen.Bethesda.Starfield;

partial class PhotoModeFeature
{
    public enum Mode
    {
        TextureOverlay,
        Frame,
        Filter,
    }

    // FNAM Flags (xEdit)
    [Flags]
    public enum Flag : uint
    {
        TileX = 0x01,
        TileY = 0x02,
        TopMost = 0x04,
    }
}
