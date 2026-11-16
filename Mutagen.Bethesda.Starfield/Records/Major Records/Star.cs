namespace Mutagen.Bethesda.Starfield;

partial class Star
{
    // Record header flags, xEdit's STDT list (Starfield.esm: Starts Hidden on 6 stars, Always Display Name on 9)
    [Flags]
    public enum MajorFlag
    {
        StartsHidden = 0x1000_0000,
        AlwaysDisplayName = 0x2000_0000,
    }
}
