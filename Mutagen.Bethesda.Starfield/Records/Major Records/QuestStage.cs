namespace Mutagen.Bethesda.Starfield;

public partial class QuestStage
{
    [Flags]
    public enum Flag
    {
        RunOnStart = 0x02,
        RunOnStop = 0x04,
        KeepInstanceDataFromHereOn = 0x08,
        StartQuestTimer = 0x20,
        ReadyToDisplayOnUi = 0x40,
    }
}