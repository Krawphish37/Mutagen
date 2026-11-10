namespace Mutagen.Bethesda.Starfield;

partial class VoiceType
{
    [Flags]
    public enum Flag
    {
        AllowDefaultDialog = 0x01,
        Female = 0x02,
    }

    public enum Pronoun
    {
        Unselected = 0,
        HeHim = 1,
        SheHer = 2,
        TheyThem = 3,
    }
}