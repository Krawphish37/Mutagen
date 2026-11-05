using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield.Internals;
using Noggog;

namespace Mutagen.Bethesda.Starfield;

public partial class ResearchProject
{
    public enum TierEnum
    {
        Tier0,
        Tier1,
        Tier2,
        Tier3,
        Tier4,
        Tier5,
    }
}

partial class ResearchProjectBinaryCreateTranslation
{
    public static partial void FillBinaryConditionsCustom(
        MutagenFrame frame,
        IResearchProjectInternal item,
        PreviousParse lastParsed)
    {
        // The frame ends with the first subrecord, and the conditions run on past it
        var outer = new MutagenFrame(frame.Reader);
        item.ConditionsHaveNoCount = outer.GetSubrecordHeader().RecordType != RecordTypes.CITC;
        item.Conditions = ListBinaryTranslation<Condition>.Instance.ParsePerItem(
                reader: outer,
                countLengthLength: 4,
                countRecord: RecordTypes.CITC,
                triggeringRecord: Condition_Registration.TriggerSpecs,
                translationParams: default(TypedParseParams),
                transl: Condition.TryCreateFromBinary)
            .CastExtendedList<Condition>();
    }
}

partial class ResearchProjectBinaryWriteTranslation
{
    public static partial void WriteBinaryConditionsCustom(
        MutagenWriter writer,
        IResearchProjectGetter item)
    {
        if (item.ConditionsHaveNoCount)
        {
            if (item.Conditions is { } conditions)
            {
                foreach (var condition in conditions)
                {
                    condition.WriteToBinary(writer);
                }
            }
            return;
        }
        ListBinaryTranslation<IConditionGetter>.Instance.WriteWithCounter(
            writer: writer,
            items: item.Conditions,
            counterType: RecordTypes.CITC,
            counterLength: 4,
            transl: (MutagenWriter subWriter, IConditionGetter subItem, TypedWriteParams conv) =>
            {
                subItem.WriteToBinary(subWriter, conv);
            });
    }
}

partial class ResearchProjectBinaryOverlay
{
    public IReadOnlyList<IConditionGetter>? Conditions { get; private set; }
    public bool ConditionsHaveNoCount { get; private set; }

    partial void ConditionsCustomParse(
        OverlayStream stream,
        int finalPos,
        int offset,
        RecordType type,
        PreviousParse lastParsed)
    {
        ConditionsHaveNoCount = type != RecordTypes.CITC;
        Conditions = BinaryOverlayList.FactoryByCountPerItem<IConditionGetter>(
            stream: stream,
            package: _package,
            countLength: 4,
            trigger: Condition_Registration.TriggerSpecs,
            countType: RecordTypes.CITC,
            translationParams: default(TypedParseParams),
            getter: (s, p, recConv) => ConditionBinaryOverlay.ConditionFactory(new OverlayStream(s, p), p, recConv),
            skipHeader: false);
    }
}
