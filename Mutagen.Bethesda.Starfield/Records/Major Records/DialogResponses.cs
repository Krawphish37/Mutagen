using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Noggog;

namespace Mutagen.Bethesda.Starfield;

partial class DialogResponses
{
    [Flags]
    public enum MajorFlag
    {
        InfoGroup = 0x0000_0040,
        ExcludeFromExport = 0x0000_0080,
        ActorChanged = 0x0000_2000
    }

    [Flags]
    public enum Flag
    {
        StartSceneOnEnd = 0x1,
        Random = 0x2,
        SayOnce = 0x4,
        RequiresPlayerActivation = 0x8,
        RandomEnd = 0x20,
        EndRunningScene = 0x40,
        NeedsReview = 0x80,
        PlayerAddress = 0x100,
        CanMoveWhileGreeting = 0x400,
        NoLipFile = 0x800,
        DoNotGrayOut = 0x1000,
        AudioOutputOverride = 0x2000,
        HasCapture = 0x4000,
    }
    
    // ENAM flags of an INFO group (MajorFlag.InfoGroup). DialogResponseFlags.Flags is typed with the response flags (Flag);
    // on a group record, read the same value as InfoFlag: (DialogResponses.InfoFlag)(int)flags.Flags
    [Flags]
    public enum InfoFlag
    {
        Random = 0x2,
        ForceAllChildrenPlayerActivateOnly = 0x8,
        RandomEnd = 0x20,
        ChildInfosDontInheritResetData = 0x100,
        ForceAllChildrenRandom = 0x200,
        DoNotDoAllBeforeRepeating = 0x800,
    }
    
    // Starfield stores Low 0, Normal 1, High 2, Force 3 (xEdit's Starfield definitions)
    public enum SubtitlePriorityLevel
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Force = 3,
    }
}

/// <summary>
/// Reads a response's NAM9 (<see cref="IDialogResponseGetter.CreationTime"/>) as a date.
/// </summary>
public static partial class DialogResponseMixIn
{
    /// <summary>
    /// NAM9 holds a Windows FILETIME (100 ns ticks since 1601-01-01, UTC) that the Creation Kit stamps on a response.
    /// xEdit calls it "Text Hash"; every value in Starfield.esm decodes to 2016-2023 and every value in Fallout4.esm to
    /// 2013-2015. Returns null when the response has no NAM9 or the value is not a valid FILETIME.
    /// </summary>
    public static DateTime? GetCreationTimeUtc(this IDialogResponseGetter response)
    {
        if (response.CreationTime is not { } ticks) return null;
        if (ticks > (ulong)DateTime.MaxValue.ToFileTimeUtc()) return null;
        return DateTime.FromFileTimeUtc((long)ticks);
    }
}
