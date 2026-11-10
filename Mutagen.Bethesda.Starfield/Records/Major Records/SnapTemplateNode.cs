namespace Mutagen.Bethesda.Starfield;

partial class SnapTemplateNode
{
    [Flags]
    public enum Flag
    {
        SnapsToSelf = 0x01,
        IsHinge = 0x02,
        DisplayNode = 0x04,
        RunBehaviorsWhenNotSnapped = 0x08,
    }

    public enum StairwayTypeEnum
    {
        None = 0,
        Upstairs = 1,
        Downstairs = 2,
    }
}
