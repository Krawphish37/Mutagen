namespace Mutagen.Bethesda.Starfield;

partial class SceneActor
{
    [Flags]
    // Starfield's actor flags (LNAM), as in xEdit's Starfield definitions
    public enum Flag
    {
        NoPlayerActivation = 0x01,
        Optional = 0x02,
        CanBeRemoved = 0x04,
        RunOnlyScenePackages = 0x08,
        NoCommandState = 0x10,
        NoPlayerDialogue = 0x20,
        TimelineHeadtracking = 0x40,
        TimelineCameras = 0x80,
    }

    [Flags]
    public enum BehaviorFlag
    {
        DeathPause = 0x001,
        DeathEnd = 0x002,
        CombatPause = 0x004,
        CombatEnd = 0x008,
        DialoguePause = 0x010,
        DialogueEnd = 0x020,
        ObsComPause = 0x040,
        ObsComEnd = 0x080,
    }
}
