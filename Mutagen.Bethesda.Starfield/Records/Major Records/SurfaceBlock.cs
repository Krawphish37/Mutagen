namespace Mutagen.Bethesda.Starfield;

partial class SurfaceBlock
{
    public enum RarityEnum
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
    }

    public enum AlignmentMode
    {
        Relative = 0,
        Absolute = 1,
    }

    public enum GridAlignment
    {
        DefaultIniMedian = 0,
        Min = 1,
        Max = 2,
        Average = 3,
        Median = 4,
        Percent = 5,
    }

    public enum GridSampling
    {
        DefaultIniBorders = 0,
        Legacy = 1,
        Full = 2,
        Borders = 3,
        Corners = 4,
    }
}
