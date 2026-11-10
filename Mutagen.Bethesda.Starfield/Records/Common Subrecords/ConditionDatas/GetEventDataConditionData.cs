namespace Mutagen.Bethesda.Starfield;

public partial class GetEventDataConditionData : IConditionParameters
{
    string? IConditionParametersGetter.StringParameter1 => FirstUnusedStringParameter;

    string? IConditionParametersGetter.StringParameter2 => SecondUnusedStringParameter;

    string? IConditionParameters.StringParameter1
    {
        get => FirstUnusedStringParameter;
        set => FirstUnusedStringParameter = value;
    }

    string? IConditionParameters.StringParameter2
    {
        get => SecondUnusedStringParameter;
        set => SecondUnusedStringParameter = value;
    }

    Condition.Function IConditionDataGetter.Function => Condition.Function.GetEventData;
    
    /// <summary>
    /// The event member a condition reads: two ASCII characters, e.g. 0x3152 is "R1".  What a member holds depends on
    /// the event (L1 and L2 are the old and new location for a location change), so the names are xEdit's generic ones.
    /// Also stored in Parameter #3 when Run On is Event Data (see ConditionDataExtension.GetEventDataMember).
    /// </summary>
    public enum EventMember
    {
        None = 0x0,
        Form = 0x3146,
        Global = 0x3147,
        Identifier = 0x3149,
        Keyword1 = 0x314b,
        Keyword2 = 0x324b,
        Keyword3 = 0x334b,
        /// <summary>"L1" (the old location for a location change)</summary>
        Location1 = 0x314c,
        /// <summary>"L2" (the new location for a location change)</summary>
        Location2 = 0x324c,
        CreatedObject = 0x314f,
        Player1 = 0x3150,
        Player2 = 0x3250,
        /// <summary>"Q2" in the file (0x51 'Q', 0x32 '2'); xEdit calls it Quest</summary>
        Quest = 0x3251,
        Reference1 = 0x3152,
        Reference2 = 0x3252,
        Reference3 = 0x3352,
        Value1 = 0x3156,
        Value2 = 0x3256,
        All = 0xffff,
    }
    
    public enum EventFunction : ushort
    {
        GetIsID,
        IsInList,
        GetValue,
        HasKeyword,
        GetItemValue
    }
}
