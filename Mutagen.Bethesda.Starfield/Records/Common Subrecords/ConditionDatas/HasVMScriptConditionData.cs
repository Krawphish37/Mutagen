namespace Mutagen.Bethesda.Starfield;

public partial class HasVMScriptConditionData : IConditionParameters
{
    string? IConditionParametersGetter.StringParameter1 => FirstParameter;

    string? IConditionParametersGetter.StringParameter2 => SecondUnusedStringParameter;

    string? IConditionParameters.StringParameter1
    {
        get => FirstParameter;
        set => FirstParameter = value;
    }

    string? IConditionParameters.StringParameter2
    {
        get => SecondUnusedStringParameter;
        set => SecondUnusedStringParameter = value;
    }

    Condition.Function IConditionDataGetter.Function => Condition.Function.HasVMScript;

    // The parameters are the CTDA's as the game reads them: a string parameter is its CIS1/CIS2 subrecord, not the
    // unused Int32 slot before it (generateConditionData="false" in ConditionDatas.xml; C58/W19)

    object? IConditionParameters.Parameter1
    {
        get => FirstParameter;
        set => FirstParameter = value is null or string ? (string?)value : throw new ArgumentException("Parameter1 is a string");
    }

    object? IConditionParametersGetter.Parameter1 => FirstParameter;

    Type? IConditionParametersGetter.Parameter1Type => typeof(string);

    object? IConditionParameters.Parameter2
    {
        get => SecondParameter;
        set => SecondParameter = value is int v ? v : throw new ArgumentException("Parameter2 is an Int32");
    }

    object? IConditionParametersGetter.Parameter2 => SecondParameter;

    Type? IConditionParametersGetter.Parameter2Type => typeof(int);
}
