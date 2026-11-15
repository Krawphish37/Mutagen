using Mutagen.Bethesda.Plugins;

namespace Mutagen.Bethesda.Starfield;

public partial class GetVMQuestVariableConditionData : IConditionParameters
{
    string? IConditionParametersGetter.StringParameter1 => FirstUnusedStringParameter;

    string? IConditionParametersGetter.StringParameter2 => SecondParameter;

    string? IConditionParameters.StringParameter1
    {
        get => FirstUnusedStringParameter;
        set => FirstUnusedStringParameter = value;
    }

    string? IConditionParameters.StringParameter2
    {
        get => SecondParameter;
        set => SecondParameter = value;
    }

    Condition.Function IConditionDataGetter.Function => Condition.Function.GetVMQuestVariable;

    // The parameters are the CTDA's as the game reads them: a string parameter is its CIS1/CIS2 subrecord, not the
    // unused Int32 slot before it (generateConditionData="false" in ConditionDatas.xml; C58/W19)

    object? IConditionParameters.Parameter1
    {
        get => FirstParameter;
        set => FirstParameter = value is IFormLinkOrIndex<IQuestGetter> v ? v : throw new ArgumentException("Parameter1 is a quest link");
    }

    object? IConditionParametersGetter.Parameter1 => FirstParameter;

    Type? IConditionParametersGetter.Parameter1Type => typeof(IFormLinkOrIndexGetter<IQuestGetter>);

    object? IConditionParameters.Parameter2
    {
        get => SecondParameter;
        set => SecondParameter = value is null or string ? (string?)value : throw new ArgumentException("Parameter2 is a string");
    }

    object? IConditionParametersGetter.Parameter2 => SecondParameter;

    Type? IConditionParametersGetter.Parameter2Type => typeof(string);
}
