namespace Mutagen.Bethesda.Starfield;

partial class CameraPath
{
    [Flags]
    public enum Flags
    {
        Disable = 0x0001,
        ShotList = 0x0002,
        DynamicCameraTimes = 0x0004,
        Unknown3 = 0x0008,
        RandomizePaths = 0x0040,
        NotMustHaveCameraShots = 0x0080,
    }
}