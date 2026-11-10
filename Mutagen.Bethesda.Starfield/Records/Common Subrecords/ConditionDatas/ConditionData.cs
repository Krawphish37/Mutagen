using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Translations;

namespace Mutagen.Bethesda.Starfield;

public static class ConditionDataExtension
{
    public static bool TryGetParameter1(this IConditionParametersGetter conditionData, [MaybeNullWhen(false)] out Type type, [MaybeNullWhen(false)] out object value)
    {
        type = conditionData.Parameter1Type;
        if (type is null)
        {
            value = null;
            return false;
        }

        value = conditionData.Parameter1;
        return value is not null;
    }

    /// <summary>
    /// Parameter #3 as the quest alias the condition runs on, when Run On is Quest Alias (else null).
    /// </summary>
    public static int? GetQuestAlias(this IConditionDataGetter conditionData)
        => conditionData.RunOnType == Condition.RunOnType.QuestAlias ? conditionData.ParameterThree : null;

    /// <summary>
    /// Parameter #3 as the package data input the condition runs on, when Run On is Package Data (else null).
    /// </summary>
    public static int? GetPackageDataInput(this IConditionDataGetter conditionData)
        => conditionData.RunOnType == Condition.RunOnType.PackageData ? conditionData.ParameterThree : null;

    /// <summary>
    /// Parameter #3 as the event member the condition runs on, when Run On is Event Data (else null); -1 is None.
    /// </summary>
    public static GetEventDataConditionData.EventMember? GetEventDataMember(this IConditionDataGetter conditionData)
    {
        if (conditionData.RunOnType != Condition.RunOnType.EventData) return null;
        return conditionData.ParameterThree == -1
            ? GetEventDataConditionData.EventMember.None
            : (GetEventDataConditionData.EventMember)conditionData.ParameterThree;
    }

    /// <summary>
    /// Sets Run On to Quest Alias and Parameter #3 to the alias.
    /// </summary>
    public static void SetQuestAlias(this IConditionData conditionData, int alias)
    {
        conditionData.RunOnType = Condition.RunOnType.QuestAlias;
        conditionData.ParameterThree = alias;
    }

    /// <summary>
    /// Sets Run On to Package Data and Parameter #3 to the input.
    /// </summary>
    public static void SetPackageDataInput(this IConditionData conditionData, int input)
    {
        conditionData.RunOnType = Condition.RunOnType.PackageData;
        conditionData.ParameterThree = input;
    }

    /// <summary>
    /// Sets Run On to Event Data and Parameter #3 to the member.
    /// </summary>
    public static void SetEventDataMember(this IConditionData conditionData, GetEventDataConditionData.EventMember member)
    {
        conditionData.RunOnType = Condition.RunOnType.EventData;
        conditionData.ParameterThree = (int)member;
    }

    public static bool TrySetParameter1(this IConditionParameters conditionData, object? value)
    {
        try
        {
            conditionData.Parameter1 = value;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

partial class ConditionData : IFormLinkOrIndexFlagGetter, IConditionParameters
{
    string? IConditionParameters.StringParameter1
    {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }
    string? IConditionParameters.StringParameter2
    {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }
    object? IConditionParameters.Parameter1
    {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }
    object? IConditionParameters.Parameter2
    {
        get => throw new NotImplementedException();
        set => throw new NotImplementedException();
    }
    string? IConditionParametersGetter.StringParameter1 => throw new NotImplementedException();
    string? IConditionParametersGetter.StringParameter2 => throw new NotImplementedException();
    object? IConditionParametersGetter.Parameter1 => throw new NotImplementedException();
    object? IConditionParametersGetter.Parameter2 => throw new NotImplementedException();
    Type? IConditionParametersGetter.Parameter1Type => throw new NotImplementedException();
    Type? IConditionParametersGetter.Parameter2Type => throw new NotImplementedException();
    Condition.Function IConditionDataGetter.Function => throw new NotImplementedException();
}

public partial interface IConditionDataGetter
{
    Condition.Function Function { get; }
}

partial class ConditionDataBinaryOverlay : IFormLinkOrIndexFlagGetter, IConditionParametersGetter
{
    public bool UseAliases { get; internal set; }
    public bool UsePackageData { get; internal set; }
    string? IConditionParametersGetter.StringParameter1 => throw new NotImplementedException();
    string? IConditionParametersGetter.StringParameter2 => throw new NotImplementedException();
    Condition.Function IConditionDataGetter.Function => throw new NotImplementedException();
    object? IConditionParametersGetter.Parameter1 => throw new NotImplementedException();
    object? IConditionParametersGetter.Parameter2 => throw new NotImplementedException();
    Type? IConditionParametersGetter.Parameter1Type => throw new NotImplementedException();
    Type? IConditionParametersGetter.Parameter2Type => throw new NotImplementedException();
}

partial class ConditionDataBinaryOverlay
{
    public Condition.RunOnType RunOnType => (Condition.RunOnType)BinaryPrimitives.ReadInt32LittleEndian(_structData.Span.Slice(0xC, 0x4));
    public IFormLinkGetter<IStarfieldMajorRecordGetter> Reference => FormLinkBinaryTranslation.Instance.OverlayFactory<IStarfieldMajorRecordGetter>(_package, _structData.Span.Slice(0x10, 0x4));
    public Int32 ParameterThree => BinaryPrimitives.ReadInt32LittleEndian(_structData.Slice(0x14, 0x4));
    public IFormLinkGetter<IKeywordGetter> LinkedReferenceKeyword =>
        RunOnType == Condition.RunOnType.LinkedReference && ParameterThree != 0 && ParameterThree != -1
            ? FormLinkBinaryTranslation.Instance.OverlayFactory<IKeywordGetter>(_package, _structData.Span.Slice(0x14, 0x4))
            : FormLink<IKeywordGetter>.Null;
}