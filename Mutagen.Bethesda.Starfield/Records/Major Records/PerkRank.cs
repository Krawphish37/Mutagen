using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Starfield.Internals;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Translations.Binary;
using Noggog;

namespace Mutagen.Bethesda.Starfield;

partial class PerkRankBinaryCreateTranslation
{
    public record Payload
    {
        public SubrecordFrame? DATA { get; set; }
        public SubrecordFrame? EPF2 { get; set; }
        public SubrecordFrame? EPF3 { get; set; }
        public SubrecordFrame? EPFD { get; set; }
        public SubrecordFrame? EPFT { get; set; }
        public SubrecordFrame? EPFB { get; set; }
        public List<PerkCondition>? Conditions { get; set; }

        // C60: where each subrecord's content starts (with the stream's offset), while value positions are recorded
        internal long DataAt { get; set; } = -1;
        internal long Epf2At { get; set; } = -1;
        internal long Epf3At { get; set; } = -1;
        internal long EpfdAt { get; set; } = -1;
        internal long EpfbAt { get; set; } = -1;
    }

    private static Payload ReadPayload<TStream>(TStream stream)
        where TStream : IMutagenReadStream
    {
        var ret = new Payload();
        while (stream.TryReadSubrecord(out var subFrame))
        {
            var contentAt = ValuePositionRecorder.Here(stream);
            if (contentAt >= 0) contentAt += subFrame.HeaderLength - subFrame.TotalLength;
            switch (subFrame.RecordTypeInt)
            {
                case RecordTypeInts.DATA:
                    ret.DATA = subFrame;
                    ret.DataAt = contentAt;
                    break;
                case RecordTypeInts.EPF2:
                    ret.EPF2 = subFrame;
                    ret.Epf2At = contentAt;
                    break;
                case RecordTypeInts.EPF3:
                    ret.EPF3 = subFrame;
                    ret.Epf3At = contentAt;
                    break;
                case RecordTypeInts.EPFD:
                    ret.EPFD = subFrame;
                    ret.EpfdAt = contentAt;
                    break;
                case RecordTypeInts.EPFT:
                    ret.EPFT = subFrame;
                    break;
                case RecordTypeInts.EPFB:
                    ret.EPFB = subFrame;
                    ret.EpfbAt = contentAt;
                    break;
                case RecordTypeInts.PRKC:
                case RecordTypeInts.CTDA:
                    stream.Position -= subFrame.TotalLength;
                    if (ValuePositionRecorder.On) ValuePositionRecorder.List(stream, "Conditions");
                    ret.Conditions = ListBinaryTranslation<PerkCondition>.Instance.Parse(
                        reader: new MutagenFrame(stream),
                        transl: (MutagenFrame r, [MaybeNullWhen(false)] out PerkCondition listSubItem) =>
                        {
                            return LoquiBinaryTranslation<PerkCondition>.Instance.Parse(
                                frame: r,
                                item: out listSubItem!);
                        });
                    if (ValuePositionRecorder.On) ValuePositionRecorder.End(stream);
                    break;
                default:
                    stream.Position -= subFrame.TotalLength;
                    return ret;
            }
        }

        return ret;
    }
    public static IEnumerable<APerkEffect> ParseEffects(IMutagenReadStream stream)
    {
        var effectIndex = 0;
        while (stream.TryReadSubrecord(RecordTypes.PRKE, out var prkeFrame))
        {
            var prkeAt = ValuePositionRecorder.Here(stream);
            if (prkeAt >= 0)
            {
                prkeAt -= prkeFrame.TotalLength;
                ValuePositionRecorder.ItemFrom(prkeAt, effectIndex);
                prkeAt += prkeFrame.HeaderLength;
            }
            effectIndex++;
            var type = (Perk.EffectType)prkeFrame.Content[0];
            var rank = prkeFrame.Content[1];
            var priority = prkeFrame.Content[2];
            
            var payload = ReadPayload(stream);

            APerkEffect effect;
            if (payload.DATA != null)
            {
                switch (type)
                {
                    case Perk.EffectType.Quest:
                        effect = new PerkQuestEffect()
                        {
                            Quest = FormLinkBinaryTranslation.Instance.Factory<IQuestGetter>(stream.MetaData, payload.DATA.Value.Content),
                            Stage = BinaryPrimitives.ReadUInt16LittleEndian(payload.DATA.Value.Content.Slice(4)),
                        };
                        if (ValuePositionRecorder.On) ValuePositionRecorder.List(stream, "Conditions");
                        effect.Conditions.SetTo(
                            Plugins.Binary.Translations.ListBinaryTranslation<PerkCondition>.Instance.Parse(
                                reader: new MutagenFrame(stream),
                                transl: (MutagenFrame r, [MaybeNullWhen(false)] out PerkCondition listSubItem) =>
                                {
                                    return LoquiBinaryTranslation<PerkCondition>.Instance.Parse(
                                        frame: r,
                                        item: out listSubItem!);
                                }));
                        if (ValuePositionRecorder.On) ValuePositionRecorder.End(stream);
                        break;
                    case Perk.EffectType.Ability:
                        effect = new PerkAbilityEffect()
                        {
                            Ability = FormLinkBinaryTranslation.Instance.Factory<ISpellGetter>(stream.MetaData, payload.DATA.Value.Content),
                        };
                        if (ValuePositionRecorder.On) ValuePositionRecorder.List(stream, "Conditions");
                        effect.Conditions.SetTo(
                            Plugins.Binary.Translations.ListBinaryTranslation<PerkCondition>.Instance.Parse(
                                reader: new MutagenFrame(stream),
                                transl: (MutagenFrame r, [MaybeNullWhen(false)] out PerkCondition listSubItem) =>
                                {
                                    return LoquiBinaryTranslation<PerkCondition>.Instance.Parse(
                                        frame: r,
                                        item: out listSubItem!);
                                }));
                        if (ValuePositionRecorder.On) ValuePositionRecorder.End(stream);
                        break;
                    case Perk.EffectType.EntryPoint:
                        var entryPt = (APerkEntryPointEffect.EntryType)payload.DATA.Value.Content[0];
                        var func = (APerkEntryPointEffect.FunctionType)payload.DATA.Value.Content[1];
                        var tabCount = payload.DATA.Value.Content[2];
                        APerkEntryPointEffect entryPointEffect;
                        switch (func)
                        {
                            case APerkEntryPointEffect.FunctionType.SetValue:
                            case APerkEntryPointEffect.FunctionType.AddValue:
                            case APerkEntryPointEffect.FunctionType.MultiplyValue:
                                if (payload.EPFT == null && payload.EPFD == null)
                                {
                                    entryPointEffect = new PerkEntryPointModifyValue()
                                    {
                                        Value = null,
                                        Modification = func switch
                                        {
                                            APerkEntryPointEffect.FunctionType.SetValue => PerkEntryPointModifyValue.ModificationType.Set,
                                            APerkEntryPointEffect.FunctionType.MultiplyValue => PerkEntryPointModifyValue.ModificationType.Multiply,
                                            APerkEntryPointEffect.FunctionType.AddValue => PerkEntryPointModifyValue.ModificationType.Add,
                                            _ => throw new MalformedDataException(),
                                        }
                                    };
                                }
                                else
                                {
                                    if (!payload.EPFT.HasValue) throw new MalformedDataException($"{nameof(PerkEntryPointModifyValue)} did not have expected EPFT record");
                                    if (!payload.EPFD.HasValue) throw new MalformedDataException($"{nameof(PerkEntryPointModifyValue)} did not have expected EPFD record");
                                    var epftType = (APerkEntryPointEffect.ParameterType)payload.EPFT.Value.Content[0];
                                    if (epftType != APerkEntryPointEffect.ParameterType.Float
                                        && epftType != APerkEntryPointEffect.ParameterType.FloatFloat)
                                    {
                                        throw new MalformedDataException($"{nameof(PerkEntryPointModifyValue)} did not have expected parameter type flag: {(APerkEntryPointEffect.ParameterType)payload.EPFT.Value.Content[0]}");
                                    }

                                    if (epftType == APerkEntryPointEffect.ParameterType.Float)
                                    {
                                        entryPointEffect = new PerkEntryPointModifyValue()
                                        {
                                            Value = payload.EPFD.Value.Content.Float(),
                                            Modification = func switch
                                            {
                                                APerkEntryPointEffect.FunctionType.SetValue => PerkEntryPointModifyValue.ModificationType.Set,
                                                APerkEntryPointEffect.FunctionType.MultiplyValue => PerkEntryPointModifyValue.ModificationType.Multiply,
                                                APerkEntryPointEffect.FunctionType.AddValue => PerkEntryPointModifyValue.ModificationType.Add,
                                                _ => throw new MalformedDataException(),
                                            }
                                        };
                                    }
                                    else
                                    {
                                        entryPointEffect = new PerkEntryPointModifyValues()
                                        {
                                            Value = payload.EPFD.Value.Content.Float(),
                                            Value2 = payload.EPFD.Value.Content.Slice(4).Float(),
                                            Modification = func switch
                                            {
                                                APerkEntryPointEffect.FunctionType.SetValue => PerkEntryPointModifyValue.ModificationType.Set,
                                                APerkEntryPointEffect.FunctionType.MultiplyValue => PerkEntryPointModifyValue.ModificationType.Multiply,
                                                APerkEntryPointEffect.FunctionType.AddValue => PerkEntryPointModifyValue.ModificationType.Add,
                                                _ => throw new MalformedDataException(),
                                            }
                                        };
                                    }
                                }
                                break;
                            case APerkEntryPointEffect.FunctionType.AddRangeToValue:
                                if (!payload.EPFT.HasValue) throw new MalformedDataException($"{nameof(PerkEntryPointAddRangeToValue)} did not have expected EPFT record");
                                if (!payload.EPFD.HasValue) throw new MalformedDataException($"{nameof(PerkEntryPointAddRangeToValue)} did not have expected EPFD record");
                                if (payload.EPFT.Value.Content[0] != (byte)APerkEntryPointEffect.ParameterType.FloatFloat)
                                {
                                    throw new MalformedDataException($"{nameof(PerkEntryPointAddRangeToValue)} did not have expected parameter type flag: {(APerkEntryPointEffect.ParameterType)payload.EPFT.Value.Content[0]}");
                                }
                                entryPointEffect = new PerkEntryPointAddRangeToValue()
                                {
                                    From = payload.EPFD.Value.Content.Float(),
                                    To = payload.EPFD.Value.Content.Slice(4).Float(),
                                };
                                break;
                            case APerkEntryPointEffect.FunctionType.SetToActorValueMult:
                            case APerkEntryPointEffect.FunctionType.MultiplyActorValueMult:
                            case APerkEntryPointEffect.FunctionType.MultiplyOnePlusActorValueMult:
                            case APerkEntryPointEffect.FunctionType.AddActorValueMult:
                                if (!payload.EPFT.HasValue) throw new MalformedDataException($"{nameof(PerkEntryPointModifyActorValue)} did not have expected EPFT record");
                                if (!payload.EPFD.HasValue) throw new MalformedDataException($"{nameof(PerkEntryPointModifyActorValue)} did not have expected EPFD record");
                                if (payload.EPFT.Value.Content[0] != (byte)APerkEntryPointEffect.ParameterType.ActorValue)
                                {
                                    throw new MalformedDataException($"{nameof(PerkEntryPointModifyActorValue)} did not have expected parameter type flag: {(APerkEntryPointEffect.ParameterType)payload.EPFT.Value.Content[0]}");
                                }
                                entryPointEffect = new PerkEntryPointModifyActorValue()
                                {
                                    ActorValue = FormLinkBinaryTranslation.Instance.Factory<IActorValueInformationGetter>(stream.MetaData, payload.EPFD.Value.Content),
                                    Value = payload.EPFD.Value.Content.Slice(4).Float(),
                                    Modification = func switch
                                    {
                                        APerkEntryPointEffect.FunctionType.SetToActorValueMult => PerkEntryPointModifyActorValue.ModificationType.SetToAVMult,
                                        APerkEntryPointEffect.FunctionType.AddActorValueMult => PerkEntryPointModifyActorValue.ModificationType.AddAVMult,
                                        APerkEntryPointEffect.FunctionType.MultiplyActorValueMult => PerkEntryPointModifyActorValue.ModificationType.MultiplyAVMult,
                                        APerkEntryPointEffect.FunctionType.MultiplyOnePlusActorValueMult => PerkEntryPointModifyActorValue.ModificationType.MultiplyOnePlusAVMult,
                                        _ => throw new MalformedDataException(),
                                    }
                                };
                                break;
                            case APerkEntryPointEffect.FunctionType.AbsoluteValue:
                            case APerkEntryPointEffect.FunctionType.NegativeAbsoluteValue:
                                if (payload.EPFT.HasValue && payload.EPFT.Value.Content[0] != (byte)APerkEntryPointEffect.ParameterType.None)
                                {
                                    throw new MalformedDataException($"{nameof(PerkEntryPointAbsoluteValue)} did not have expected parameter type flag: {(APerkEntryPointEffect.ParameterType)payload.EPFT.Value.Content[0]}");
                                }
                                entryPointEffect = new PerkEntryPointAbsoluteValue()
                                {
                                    Negative = func == APerkEntryPointEffect.FunctionType.NegativeAbsoluteValue
                                };
                                break;
                            case APerkEntryPointEffect.FunctionType.AddLeveledList:
                                if (!payload.EPFT.HasValue) throw new MalformedDataException($"{nameof(PerkEntryPointModifyActorValue)} did not have expected EPFT record");
                                if (payload.EPFT.Value.Content[0] != (byte)APerkEntryPointEffect.ParameterType.LeveledItem)
                                {
                                    throw new MalformedDataException($"{nameof(PerkEntryPointAddLeveledItem)} did not have expected parameter type flag: {(APerkEntryPointEffect.ParameterType)payload.EPFT.Value.Content[0]}");
                                }
                                entryPointEffect = new PerkEntryPointAddLeveledItem()
                                {
                                    Item = FormLinkBinaryTranslation.Instance.Factory<ILeveledItemGetter>(stream.MetaData, payload.EPFD?.Content)
                                };
                                break;
                            case APerkEntryPointEffect.FunctionType.AddActivateChoice:
                                if (!payload.EPFT.HasValue) throw new MalformedDataException($"{nameof(PerkEntryPointModifyActorValue)} did not have expected EPFT record");
                                if (payload.EPFT.Value.Content[0] != (byte)APerkEntryPointEffect.ParameterType.SpellWithStrings)
                                {
                                    throw new MalformedDataException($"{nameof(PerkEntryPointAddActivateChoice)} did not have expected parameter type flag: {(APerkEntryPointEffect.ParameterType)payload.EPFT.Value.Content[0]}");
                                }
                                entryPointEffect = new PerkEntryPointAddActivateChoice()
                                {
                                    // ButtonLabel (EPF2) and Flags (EPF3) are read below for every effect
                                    Spell = FormLinkBinaryTranslation.Instance.FactoryNullable<ISpellGetter>(stream.MetaData, payload.EPFD?.Content),
                                };
                                break;
                            case APerkEntryPointEffect.FunctionType.SelectSpell:
                                if (!payload.EPFT.HasValue) throw new MalformedDataException($"{nameof(PerkEntryPointModifyActorValue)} did not have expected EPFT record");
                                if (payload.EPFT.Value.Content[0] != (byte)APerkEntryPointEffect.ParameterType.Spell)
                                {
                                    throw new MalformedDataException($"{nameof(PerkEntryPointSelectSpell)} did not have expected parameter type flag: {(APerkEntryPointEffect.ParameterType)payload.EPFT.Value.Content[0]}");
                                }
                                entryPointEffect = new PerkEntryPointSelectSpell()
                                {
                                    Spell = FormLinkBinaryTranslation.Instance.Factory<ISpellGetter>(stream.MetaData, payload.EPFD?.Content),
                                };
                                break;
                            case APerkEntryPointEffect.FunctionType.SelectText:
                                if (!payload.EPFT.HasValue) throw new MalformedDataException($"{nameof(PerkEntryPointModifyActorValue)} did not have expected EPFT record");
                                if (payload.EPFT.Value.Content[0] != (byte)APerkEntryPointEffect.ParameterType.String)
                                {
                                    throw new MalformedDataException($"{nameof(PerkEntryPointSelectText)} did not have expected parameter type flag: {(APerkEntryPointEffect.ParameterType)payload.EPFT.Value.Content[0]}");
                                }
                                entryPointEffect = new PerkEntryPointSelectText()
                                {
                                    Text = payload.EPFD.HasValue ? BinaryStringUtility.ProcessWholeToZString(payload.EPFD.Value.Content, stream.MetaData.Encodings.NonTranslated) : string.Empty
                                };
                                break;
                            case APerkEntryPointEffect.FunctionType.SetText:
                                if (!payload.EPFT.HasValue) throw new MalformedDataException($"{nameof(PerkEntryPointModifyActorValue)} did not have expected EPFT record");
                                if (payload.EPFT.Value.Content[0] != (byte)APerkEntryPointEffect.ParameterType.LString)
                                {
                                    throw new MalformedDataException($"{nameof(PerkEntryPointSetText)} did not have expected parameter type flag: {(APerkEntryPointEffect.ParameterType)payload.EPFT.Value.Content[0]}");
                                }
                                entryPointEffect = new PerkEntryPointSetText()
                                {
                                    Text = payload.EPFD.HasValue ? StringBinaryTranslation.Instance.Parse(payload.EPFD.Value.Content, StringsSource.Normal, stream.MetaData, eager: true) : (TranslatedString)string.Empty,
                                };
                                break;
                            case APerkEntryPointEffect.FunctionType.LegendaryMagicEffectEvent:
                                if (!payload.EPFT.HasValue) throw new MalformedDataException($"{nameof(PerkEntryPointModifyActorValue)} did not have expected EPFT record");
                                if (payload.EPFT.Value.Content[0] != (byte)APerkEntryPointEffect.ParameterType.ReplacementProjectile)
                                {
                                    throw new MalformedDataException($"{nameof(PerkEntryPointLegendaryMagicEffectEvent)} did not have expected parameter type flag: {(APerkEntryPointEffect.ParameterType)payload.EPFT.Value.Content[0]}");
                                }

                                entryPointEffect = new PerkEntryPointLegendaryMagicEffectEvent()
                                {
                                    Projectile = FormLinkBinaryTranslation.Instance.Factory<IProjectileGetter>(stream.MetaData, payload.EPFD?.Content),
                                };
                                break;
                            case APerkEntryPointEffect.FunctionType.LegendaryGravitational:
                                if (!payload.EPFT.HasValue) throw new MalformedDataException($"{nameof(PerkEntryPointLegendaryGravitational)} did not have expected EPFT record");
                                if (payload.EPFT.Value.Content[0] != (byte)APerkEntryPointEffect.ParameterType.Spell)
                                {
                                    throw new MalformedDataException($"{nameof(PerkEntryPointLegendaryGravitational)} did not have expected parameter type flag: {(APerkEntryPointEffect.ParameterType)payload.EPFT.Value.Content[0]}");
                                }

                                entryPointEffect = new PerkEntryPointLegendaryGravitational()
                                {
                                    Spell = FormLinkBinaryTranslation.Instance.Factory<ISpellGetter>(stream.MetaData, payload.EPFD?.Content),
                                };
                                break;
                            default:
                                throw new NotImplementedException();
                        }
                        entryPointEffect.EntryPoint = entryPt;
                        entryPointEffect.PerkConditionTabCount = tabCount;
                        effect = entryPointEffect;
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
            else
            {
                effect = type switch
                {
                    Perk.EffectType.Quest => new PerkQuestEffect(),
                    Perk.EffectType.Ability => new PerkAbilityEffect(),
                    _ => throw new MalformedDataException($"Expected DATA subrecord that did not exist."),
                };
                PerkEffectData.MarkWithoutData(effect);
            }
            effect.Rank = rank;
            effect.Priority = priority;
            if (payload.Conditions != null)
            {
                effect.Conditions.SetTo(payload.Conditions);
            }
            if (payload.EPF2 != null)
            {
                effect.ButtonLabel = StringBinaryTranslation.Instance.Parse(payload.EPF2.Value.Content, StringsSource.Normal, stream.MetaData, eager: true);
            }
            if (payload.EPF3 != null)
            {
                effect.Flags = (APerkEffect.Flag)payload.EPF3.Value.AsUInt16();
            }
            if (payload.EPFB != null)
            {
                effect.PerkEntryID = payload.EPFB.Value.AsUInt16();
            }

            if (stream.TryReadSubrecord(RecordTypes.EPFT, out var epftFrame)
                && epftFrame.ContentLength != 1
                && epftFrame.Content[0] != (byte)APerkEntryPointEffect.ParameterType.None)
            {
                throw new MalformedDataException($"Encountered an unexpected epft frame.");
            }
            stream.TryReadSubrecord(RecordTypes.PRKF, out var _);
            if (prkeAt >= 0)
            {
                NoteEffect(effect, prkeAt, payload);
                ValuePositionRecorder.End(stream);
            }
            yield return effect;
        }
    }

    /// <summary>C60: where an effect's values sit (PRKE's content at <paramref name="prkeAt"/>, the payload's subrecords)</summary>
    private static void NoteEffect(APerkEffect effect, long prkeAt, Payload payload)
    {
        ValuePositionRecorder.At(nameof(effect.Rank), prkeAt + 1, 1);
        ValuePositionRecorder.At(nameof(effect.Priority), prkeAt + 2, 1);
        if (payload.EPF2 is { } epf2 && payload.Epf2At >= 0) ValuePositionRecorder.At(nameof(effect.ButtonLabel), payload.Epf2At, epf2.ContentLength);
        if (payload.EPF3 is { } epf3 && payload.Epf3At >= 0) ValuePositionRecorder.At(nameof(effect.Flags), payload.Epf3At, epf3.ContentLength);
        if (payload.EPFB is { } epfb && payload.EpfbAt >= 0) ValuePositionRecorder.At(nameof(effect.PerkEntryID), payload.EpfbAt, epfb.ContentLength);
        var data = payload.DataAt;
        var epfd = payload.EpfdAt;
        var epfdLength = payload.EPFD?.ContentLength ?? 0;
        switch (effect)
        {
            case PerkQuestEffect quest when data >= 0:
                ValuePositionRecorder.At(nameof(quest.Quest), data, 4);
                ValuePositionRecorder.At(nameof(quest.Stage), data + 4, 2);
                break;
            case PerkAbilityEffect ability when data >= 0:
                ValuePositionRecorder.At(nameof(ability.Ability), data, 4);
                break;
            case APerkEntryPointEffect entryPoint when data >= 0:
                // DATA 1 is the function, which picked the class (and Modification, where there is one)
                ValuePositionRecorder.At(nameof(entryPoint.EntryPoint), data, 1);
                ValuePositionRecorder.At(nameof(entryPoint.PerkConditionTabCount), data + 2, 1);
                // C83: the values taken from the function (DATA 1): converted, the function's number isn't theirs
                switch (entryPoint)
                {
                    case PerkEntryPointModifyValue value:
                        ValuePositionRecorder.At(nameof(value.Modification), data + 1, 1, converted: true);
                        break;
                    case PerkEntryPointModifyValues values:
                        ValuePositionRecorder.At(nameof(values.Modification), data + 1, 1, converted: true);
                        break;
                    case PerkEntryPointModifyActorValue av:
                        ValuePositionRecorder.At(nameof(av.Modification), data + 1, 1, converted: true);
                        break;
                    case PerkEntryPointAbsoluteValue absolute:
                        ValuePositionRecorder.At(nameof(absolute.Negative), data + 1, 1, converted: true);
                        break;
                }
                if (epfd < 0) break;
                switch (entryPoint)
                {
                    case PerkEntryPointModifyValues values:
                        ValuePositionRecorder.At(nameof(values.Value), epfd, 4);
                        ValuePositionRecorder.At(nameof(values.Value2), epfd + 4, 4);
                        break;
                    case PerkEntryPointModifyValue value:
                        ValuePositionRecorder.At(nameof(value.Value), epfd, 4);
                        break;
                    case PerkEntryPointAddRangeToValue range:
                        ValuePositionRecorder.At(nameof(range.From), epfd, 4);
                        ValuePositionRecorder.At(nameof(range.To), epfd + 4, 4);
                        break;
                    case PerkEntryPointModifyActorValue av:
                        ValuePositionRecorder.At(nameof(av.ActorValue), epfd, 4);
                        ValuePositionRecorder.At(nameof(av.Value), epfd + 4, 4);
                        break;
                    case PerkEntryPointAddLeveledItem leveled:
                        ValuePositionRecorder.At(nameof(leveled.Item), epfd, 4);
                        break;
                    case PerkEntryPointAddActivateChoice choice:
                        ValuePositionRecorder.At(nameof(choice.Spell), epfd, 4);
                        break;
                    case PerkEntryPointSelectSpell spell:
                        ValuePositionRecorder.At(nameof(spell.Spell), epfd, 4);
                        break;
                    case PerkEntryPointSelectText selectText:
                        ValuePositionRecorder.At(nameof(selectText.Text), epfd, epfdLength);
                        break;
                    case PerkEntryPointSetText setText:
                        ValuePositionRecorder.At(nameof(setText.Text), epfd, epfdLength);
                        break;
                    case PerkEntryPointLegendaryMagicEffectEvent legendary:
                        ValuePositionRecorder.At(nameof(legendary.Projectile), epfd, 4);
                        break;
                    case PerkEntryPointLegendaryGravitational gravitational:
                        ValuePositionRecorder.At(nameof(gravitational.Spell), epfd, 4);
                        break;
                }
                break;
        }
    }

    public static partial void FillBinaryEffectsCustom(MutagenFrame frame, IPerkRank item, PreviousParse lastParsed)
    {
        if (ValuePositionRecorder.On) ValuePositionRecorder.List(frame, nameof(item.Effects));
        item.Effects.SetTo(ParseEffects(frame.Reader));
        if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
    }
}

/// <summary>
/// Quest and ability effects that were read without a DATA subrecord (s1n_excursion.esm, realfuel_creations.esm): the
/// reader gives them an empty effect, and the writer leaves the DATA out again while the effect is still empty, so the
/// record is written back as it was read instead of gaining a DATA of zeros
/// </summary>
internal static class PerkEffectData
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> _withoutData = new();

    public static void MarkWithoutData(object effect) => _withoutData.AddOrUpdate(effect, effect);

    public static bool StillWithoutData(object effect)
    {
        if (!_withoutData.TryGetValue(effect, out _)) return false;
        return effect switch
        {
            IPerkQuestEffectGetter quest => quest.Quest.IsNull && quest.Stage == 0,
            IPerkAbilityEffectGetter ability => ability.Ability.IsNull,
            _ => false,
        };
    }
}

partial class PerkRankBinaryWriteTranslation
{
    public static partial void WriteBinaryEffectsCustom(MutagenWriter writer, IPerkRankGetter item)
    {
        foreach (var effect in item.Effects)
        {
            using (HeaderExport.Subrecord(writer, RecordTypes.PRKE))
            {
                writer.Write((byte)(effect switch
                {
                    PerkQuestEffect quest => Perk.EffectType.Quest,
                    PerkAbilityEffect ab => Perk.EffectType.Ability,
                    APerkEntryPointEffect ep => Perk.EffectType.EntryPoint,
                    _ => throw new NotImplementedException()
                }));
                writer.Write(effect.Rank);
                writer.Write(effect.Priority);
            }
            // A quest or ability effect read without a DATA and still empty is written without one (PerkEffectData)
            if (PerkEffectData.StillWithoutData(effect)) { }
            else using (HeaderExport.Subrecord(writer, RecordTypes.DATA))
            {
                switch (effect)
                {
                    case PerkQuestEffect quest:
                        FormKeyBinaryTranslation.Instance.Write(writer, quest.Quest);
                        writer.Write(quest.Stage);
                        break;
                    case PerkAbilityEffect ability:
                        FormKeyBinaryTranslation.Instance.Write(writer, ability.Ability);
                        break;
                    case APerkEntryPointEffect entryPt:
                        writer.Write((byte)entryPt.EntryPoint);
                        writer.Write((byte)(entryPt switch
                        {
                            PerkEntryPointModifyValue modVal => modVal.Modification switch
                            {
                                PerkEntryPointModifyValue.ModificationType.Add => APerkEntryPointEffect.FunctionType.AddValue,
                                PerkEntryPointModifyValue.ModificationType.Set => APerkEntryPointEffect.FunctionType.SetValue,
                                PerkEntryPointModifyValue.ModificationType.Multiply => APerkEntryPointEffect.FunctionType.MultiplyValue,
                                _ => throw new NotImplementedException()
                            },
                            PerkEntryPointModifyValues modVals => modVals.Modification switch
                            {
                                PerkEntryPointModifyValue.ModificationType.Add => APerkEntryPointEffect.FunctionType.AddValue,
                                PerkEntryPointModifyValue.ModificationType.Set => APerkEntryPointEffect.FunctionType.SetValue,
                                PerkEntryPointModifyValue.ModificationType.Multiply => APerkEntryPointEffect.FunctionType.MultiplyValue,
                                _ => throw new NotImplementedException()
                            },
                            PerkEntryPointAddRangeToValue addRange => APerkEntryPointEffect.FunctionType.AddRangeToValue,
                            PerkEntryPointModifyActorValue modActorVal => modActorVal.Modification switch
                            {
                                PerkEntryPointModifyActorValue.ModificationType.AddAVMult => APerkEntryPointEffect.FunctionType.AddActorValueMult,
                                PerkEntryPointModifyActorValue.ModificationType.MultiplyAVMult => APerkEntryPointEffect.FunctionType.MultiplyActorValueMult,
                                PerkEntryPointModifyActorValue.ModificationType.MultiplyOnePlusAVMult => APerkEntryPointEffect.FunctionType.MultiplyOnePlusActorValueMult,
                                PerkEntryPointModifyActorValue.ModificationType.SetToAVMult => APerkEntryPointEffect.FunctionType.SetToActorValueMult,
                                _ => throw new NotImplementedException()
                            },
                            PerkEntryPointAbsoluteValue absVal => absVal.Negative ? APerkEntryPointEffect.FunctionType.NegativeAbsoluteValue : APerkEntryPointEffect.FunctionType.AbsoluteValue,
                            PerkEntryPointAddLeveledItem levItem => APerkEntryPointEffect.FunctionType.AddLeveledList,
                            PerkEntryPointAddActivateChoice activeChoice => APerkEntryPointEffect.FunctionType.AddActivateChoice,
                            PerkEntryPointSelectSpell spell => APerkEntryPointEffect.FunctionType.SelectSpell,
                            PerkEntryPointSelectText text => APerkEntryPointEffect.FunctionType.SelectText,
                            PerkEntryPointSetText ltext => APerkEntryPointEffect.FunctionType.SetText,
                            PerkEntryPointLegendaryMagicEffectEvent leg => APerkEntryPointEffect.FunctionType.LegendaryMagicEffectEvent,
                            PerkEntryPointLegendaryGravitational leg => APerkEntryPointEffect.FunctionType.LegendaryGravitational,
                            _ => throw new NotImplementedException()
                        }));
                        writer.Write(entryPt.PerkConditionTabCount);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
            Plugins.Binary.Translations.ListBinaryTranslation<IPerkConditionGetter>.Instance.Write(
                writer,
                effect.Conditions,
                (w, i) => i.WriteToBinary(w));
            if (effect is APerkEntryPointEffect)
            {
                var paramType = effect switch
                {
                    PerkEntryPointModifyValue _ => APerkEntryPointEffect.ParameterType.Float,
                    PerkEntryPointModifyValues _ => APerkEntryPointEffect.ParameterType.FloatFloat,
                    PerkEntryPointAddRangeToValue _ => APerkEntryPointEffect.ParameterType.FloatFloat,
                    PerkEntryPointModifyActorValue _ => APerkEntryPointEffect.ParameterType.ActorValue,
                    PerkEntryPointAbsoluteValue _ => APerkEntryPointEffect.ParameterType.None,
                    PerkEntryPointAddLeveledItem _ => APerkEntryPointEffect.ParameterType.LeveledItem,
                    PerkEntryPointAddActivateChoice _ => APerkEntryPointEffect.ParameterType.SpellWithStrings,
                    PerkEntryPointSelectSpell _ => APerkEntryPointEffect.ParameterType.Spell,
                    PerkEntryPointSelectText _ => APerkEntryPointEffect.ParameterType.String,
                    PerkEntryPointSetText _ => APerkEntryPointEffect.ParameterType.LString,
                    PerkEntryPointLegendaryMagicEffectEvent _ => APerkEntryPointEffect.ParameterType.ReplacementProjectile,
                    PerkEntryPointLegendaryGravitational _ => APerkEntryPointEffect.ParameterType.Spell,
                    _ => throw new NotImplementedException()
                };
                if ((effect is not PerkEntryPointModifyValue modValEpft
                    || modValEpft.Value.HasValue)
                    && paramType != APerkEntryPointEffect.ParameterType.None)
                {
                    using (HeaderExport.Subrecord(writer, RecordTypes.EPFT))
                    {
                        writer.Write((byte)paramType);
                    }
                }

                WriteFunctionParameters(writer, effect);
                switch (effect)
                {
                    case PerkEntryPointModifyValue modVal:
                    {
                        if (modVal.Value is {} f)
                        {
                            using (HeaderExport.Subrecord(writer, RecordTypes.EPFD))
                            {
                                writer.Write(f);
                            }
                        }
                    }
                        break;
                    case PerkEntryPointModifyValues modVal:
                    {
                        if (modVal.Value is not null || modVal.Value2 is not null)
                        {
                            using (HeaderExport.Subrecord(writer, RecordTypes.EPFD))
                            {
                                writer.Write(modVal.Value ?? 0f);
                                writer.Write(modVal.Value2 ?? 0f);
                            }
                        }
                    }
                        break;
                    case PerkEntryPointAddRangeToValue range:
                        using (HeaderExport.Subrecord(writer, RecordTypes.EPFD))
                        {
                            writer.Write(range.From);
                            writer.Write(range.To);
                        }
                        break;
                    case PerkEntryPointModifyActorValue actorVal:
                        using (HeaderExport.Subrecord(writer, RecordTypes.EPFD))
                        {
                            FormLinkBinaryTranslation.Instance.Write(writer, actorVal.ActorValue);
                            writer.Write(actorVal.Value);
                        }
                        break;
                    case PerkEntryPointAddLeveledItem lev:
                        FormKeyBinaryTranslation.Instance.Write(writer, lev.Item, RecordTypes.EPFD);
                        break;
                    case PerkEntryPointAddActivateChoice activateChoice:
                        FormKeyBinaryTranslation.Instance.Write(writer, activateChoice.Spell, RecordTypes.EPFD);
                        break;
                    case PerkEntryPointSelectSpell spell:
                        FormKeyBinaryTranslation.Instance.Write(writer, spell.Spell, RecordTypes.EPFD);
                        break;
                    case PerkEntryPointSelectText text:
                        using (HeaderExport.Subrecord(writer, RecordTypes.EPFD))
                        {
                            writer.Write(text.Text, StringBinaryType.NullTerminate, writer.MetaData.Encodings.NonTranslated);
                        }
                        break;
                    case PerkEntryPointSetText ltext:
                        using (HeaderExport.Subrecord(writer, RecordTypes.EPFD))
                        {
                            StringBinaryTranslation.Instance.Write(writer, ltext.Text, StringBinaryType.NullTerminate, StringsSource.Normal);
                        }
                        break;
                    case PerkEntryPointAbsoluteValue abs:
                        break;
                    case PerkEntryPointLegendaryMagicEffectEvent leg:
                        FormKeyBinaryTranslation.Instance.Write(writer, leg.Projectile, RecordTypes.EPFD);

                        break;
                    case PerkEntryPointLegendaryGravitational leg:
                        FormKeyBinaryTranslation.Instance.Write(writer, leg.Spell, RecordTypes.EPFD);

                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
            else
            {
                WriteFunctionParameters(writer, effect);
            }
            using (HeaderExport.Subrecord(writer, RecordTypes.PRKF)) { }
        }
    }

    // EPFB, EPF2 and EPF3 are read on every kind of effect, so they are written on every kind,
    // each only when it is set (an activate choice without EPF3 stays without it)
    private static void WriteFunctionParameters(MutagenWriter writer, IAPerkEffectGetter effect)
    {
        if (effect.PerkEntryID is { } id)
        {
            using (HeaderExport.Subrecord(writer, RecordTypes.EPFB))
            {
                writer.Write(id);
            }
        }
        if (effect.ButtonLabel != null)
        {
            using (HeaderExport.Subrecord(writer, RecordTypes.EPF2))
            {
                StringBinaryTranslation.Instance.Write(writer, effect.ButtonLabel, StringBinaryType.NullTerminate, StringsSource.Normal);
            }
        }
        if (effect.Flags is { } flags)
        {
            using (HeaderExport.Subrecord(writer, RecordTypes.EPF3))
            {
                EnumBinaryTranslation<APerkEffect.Flag, MutagenFrame, MutagenWriter>.Instance.Write(
                    writer,
                    flags,
                    2);
            }
        }
    }
}

partial class PerkRankBinaryOverlay
{
    public IReadOnlyList<IAPerkEffectGetter> Effects { get; private set; } = [];

    partial void EffectsCustomParse(
        OverlayStream stream,
        int finalPos,
        int offset,
        RecordType type,
        PreviousParse lastParsed)
    {
        Effects = PerkRankBinaryCreateTranslation.ParseEffects(stream)
            .ToList();
    }
}