namespace Mutagen.Bethesda.Starfield;

partial class UnknownConditionData : IConditionParameters
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

    // A function Mutagen has no class for: its two parameters are the raw Int32s of the CTDA (offsets 12 and 16).
    // Function is the generated property (the function index), not a parameter (C58/W19).

    object? IConditionParameters.Parameter1
    {
        get => ParameterOne;
        set => ParameterOne = value is int v ? v : throw new ArgumentException($"{nameof(UnknownConditionData)}'s Parameter1 is an Int32");
    }

    object? IConditionParametersGetter.Parameter1 => ParameterOne;

    Type? IConditionParametersGetter.Parameter1Type => typeof(int);

    object? IConditionParameters.Parameter2
    {
        get => ParameterTwo;
        set => ParameterTwo = value is int v ? v : throw new ArgumentException($"{nameof(UnknownConditionData)}'s Parameter2 is an Int32");
    }

    object? IConditionParametersGetter.Parameter2 => ParameterTwo;

    Type? IConditionParametersGetter.Parameter2Type => typeof(int);
}