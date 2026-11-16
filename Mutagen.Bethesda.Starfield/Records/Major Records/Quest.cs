using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Internals;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield.Internals;
using Noggog;

namespace Mutagen.Bethesda.Starfield;

public partial class Quest
{
    [Flags]
    public enum MajorFlag
    {
        PartialForm = 0x04000
    }

    [Flags]
    public enum Flag
    {
        StartGameEnabled = 0x0001,
        Completed = 0x0002,
        AddIdleTopicToHello = 0x0004,
        AllowRepeatedStages = 0x0008,
        StartsEnabled = 0x0010,
        DisplaysInHud = 0x0020,
        Failed = 0x0040,
        StageWait = 0x0080,
        RunOnce = 0x0100,
        ExcludeFromDialogExport = 0x0200,
        WarnOnAliasFillFailure = 0x0400,
        Active = 0x0800,
        RepeatsConditions = 0x1000,
        KeepInstance = 0x2000,
        WantDormant = 0x4000,
        HasDialogueData = 0x8000,
        Unknown16 = 0x1_0000,
        Unknown17 = 0x2_0000,
        ShipDialogueOnly = 0x4_0000,
        CanBeRejected = 0x8_0000,
        AudioLogOnly = 0x10_0000,
        DoNotStopInterruptPackagesOnAliasFill = 0x20_0000,
        DoNotDumpOldLocationEventParameter = 0x40_0000,
    }

    [Flags]
    public enum TargetFlag
    {
        CompassMarkerIgnoresLocks = 0x1,
        Hostile = 0x2,
        UseStraightLinePathing = 0x4
    }


    public enum TypeEnum
    {
        None = 0,
        MainQuest = 1,
        BrotherhoodOfSteel = 2,
        Institute = 3,
        Minutemen = 4,
        Railroad = 5,
        Misc = 6,
        SideQuests = 7,
        DLC01 = 8,
        DLC02 = 9,
        DLC03 = 10,
        DLC04 = 11,
        DLC05 = 12,
        DLC06 = 13,
        DLC07 = 14,
    }
}

partial class QuestBinaryCreateTranslation
{
    public static partial void FillBinaryDialogConditionsCustom(MutagenFrame frame, IQuestInternal item,
        PreviousParse lastParsed)
    {
        if (ValuePositionRecorder.On) ValuePositionRecorder.List(frame, nameof(item.DialogConditions));
        ConditionBinaryCreateTranslation.FillConditionsList(item.DialogConditions, frame);
        if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
    }

    public static partial ParseResult FillBinaryUnusedConditionsLogicCustom(MutagenFrame frame, IQuestInternal item,
        PreviousParse lastParsed)
    {
        var nextHeader = frame.ReadSubrecord(RecordTypes.NEXT);
        if (nextHeader.Content.Length != 0)
        {
            throw new ArgumentException("Unexpected NEXT header");
        }

        item.UnusedConditions = new();
        if (ValuePositionRecorder.On) ValuePositionRecorder.List(frame, nameof(item.UnusedConditions));
        ConditionBinaryCreateTranslation.FillConditionsList(item.UnusedConditions, frame);
        if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
        return null;
    }

    private static readonly HashSet<RecordType> _expectedAliasRecords = new()
    {
        RecordTypes.ALST,
        RecordTypes.ALLS,
        RecordTypes.ALCS,
        RecordTypes.ALMI,
        RecordTypes.ALED,
    };

    public static partial ParseResult FillBinaryAliasParseCustom(MutagenFrame frame, IQuestInternal item,
        PreviousParse lastParsed)
    {
        frame = frame.SpawnAll();
        var nextAliasAt = ValuePositionRecorder.Here(frame);
        if (frame.TryReadSubrecord(RecordTypes.ANAM, out var nextAliasId))
        {
            item.NextAliasID = nextAliasId.AsUInt32();
            if (nextAliasAt >= 0) ValuePositionRecorder.At(nameof(item.NextAliasID), nextAliasAt + nextAliasId.HeaderLength, nextAliasId.ContentLength);
        }
        item.Aliases = new();
        if (ValuePositionRecorder.On) ValuePositionRecorder.List(frame, nameof(item.Aliases));
        while (frame.TryGetSubrecord(_expectedAliasRecords, out var subRec))
        {
            switch (subRec.RecordTypeInt)
            {
                case RecordTypeInts.ALST:
                {
                    if (ValuePositionRecorder.On) ValuePositionRecorder.Item(frame, item.Aliases.Count);
                    var ret = QuestReferenceAlias.CreateFromBinary(frame);
                    if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
                    item.Aliases.Add(ret);
                    break;
                }
                case RecordTypeInts.ALLS:
                {
                    if (ValuePositionRecorder.On) ValuePositionRecorder.Item(frame, item.Aliases.Count);
                    var ret = QuestLocationAlias.CreateFromBinary(frame);
                    if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
                    item.Aliases.Add(ret);
                    break;
                }
                case RecordTypeInts.ALCS:
                case RecordTypeInts.ALMI:
                {
                    var ret = new QuestCollectionAlias();
                    if (ValuePositionRecorder.On)
                    {
                        ValuePositionRecorder.Item(frame, item.Aliases.Count);
                        ValuePositionRecorder.List(frame, nameof(ret.Collection));
                    }
                    ret.Collection.SetTo(
                        ListBinaryTranslation<CollectionAlias>.Instance.Parse(
                            frame,
                            CollectionAlias.TryCreateFromBinary));
                    if (ValuePositionRecorder.On)
                    {
                        ValuePositionRecorder.End(frame);
                        ValuePositionRecorder.End(frame);
                    }
                    item.Aliases.Add(ret);
                    break;
                }
                case RecordTypeInts.ALED:
                    frame.Position += subRec.TotalLength;
                    break;
                default:
                    if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
                    QuestAliasCounter.Remember(item, item.Aliases);
                    return (int)Quest_FieldIndex.Aliases;
            }
        }
        if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);

        QuestAliasCounter.Remember(item, item.Aliases);
        return (int)Quest_FieldIndex.Aliases;
    }
    
    public static partial void ParseSubgroupsLogic(MutagenFrame frame, IQuestInternal obj)
    {
        try
        {
            if (frame.Reader.Complete) return;
            if (!frame.TryGetGroupHeader(out var groupMeta)) return;
            if (groupMeta.GroupType == (int)GroupTypeEnum.QuestChildren)
            {
                // A label stored in another form than its parent's header is written back as stored (GroupLabelForms, C188)
                Mutagen.Bethesda.Plugins.Records.GroupLabelForms.Remember(obj, groupMeta.GroupType, BinaryPrimitives.ReadUInt32LittleEndian(groupMeta.ContainedRecordTypeData), frame.MetaData.MasterReferences);
                obj.Timestamp = BinaryPrimitives.ReadInt32LittleEndian(groupMeta.LastModifiedData);
                obj.Unknown = frame.GetInt32(offset: 20);
                if (FormKey.Factory(
                        frame.MetaData.MasterReferences, 
                        new FormID(BinaryPrimitives.ReadUInt32LittleEndian(groupMeta.ContainedRecordTypeData)),
                        reference: true) != obj.FormKey)
                {
                    RecordException.EnrichAndThrow(
                        new ArgumentException("Quest children group did not match the FormID of the parent."),
                        obj);
                }
            }
            else
            {
                return;
            }
            frame.Reader.Position += groupMeta.HeaderLength;
            frame = frame.SpawnWithLength(groupMeta.ContentLength);
            var records = ListBinaryTranslation<IMajorRecord>.Instance.Parse(
                reader: frame,
                transl: (MutagenFrame r, RecordType header, [MaybeNullWhen(false)] out IMajorRecord rec) =>
                {
                    switch (header.TypeInt)
                    {
                        case RecordTypeInts.DIAL:
                            rec = DialogTopic.CreateFromBinary(r);
                            return true;
                        case RecordTypeInts.SCEN:
                            rec = Scene.CreateFromBinary(r);
                            return true;
                        case RecordTypeInts.DLBR:
                            rec = DialogBranch.CreateFromBinary(r);
                            return true;
                        default:
                            throw new NotImplementedException();
                    }
                });
            var children = records as IReadOnlyList<IMajorRecord> ?? records.ToList();
            obj.Scenes.SetTo(children.WhereCastable<IMajorRecord, Scene>());
            obj.DialogTopics.SetTo(children.WhereCastable<IMajorRecord, DialogTopic>());
            obj.DialogBranches.SetTo(children.WhereCastable<IMajorRecord, DialogBranch>());
            QuestChildrenOrder.Remember(obj, children.Select(QuestChildrenOrder.Kind));
        }
        catch (Exception ex)
        {
            RecordException.EnrichAndThrow(ex, obj);
            throw;
        }
    }
}

/// <summary>
/// What a quest's next alias ID (ANAM) would be by its aliases alone, as it was when the quest was read from a plugin: the
/// writer keeps the stored counter while this hasn't changed, so a quest read and written back keeps its ANAM byte for byte
/// </summary>
internal static class QuestAliasCounter
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, System.Runtime.CompilerServices.StrongBox<uint>> _whenRead = new();

    public static uint NextFromAliases(IEnumerable<object>? aliases)
    {
        if (aliases == null) return 0;
        uint next = 0;
        foreach (var alias in aliases)
        {
            uint id = alias switch
            {
                IQuestReferenceAliasGetter refAlias => refAlias.ID,
                IQuestLocationAliasGetter locAlias => locAlias.ID,
                IQuestCollectionAliasGetter collAlias => collAlias.Collection
                    .Select(x => Math.Max(x.ReferenceAlias.ID, x.ID ?? 0))
                    .StartWith((uint)0)
                    .Max(),
                _ => throw new NotImplementedException(),
            };
            if (id + 1 > next) next = id + 1;
        }
        return next;
    }

    public static void Remember(object quest, IEnumerable<object>? aliases)
    {
        _whenRead.AddOrUpdate(quest, new System.Runtime.CompilerServices.StrongBox<uint>(NextFromAliases(aliases)));
    }

    public static bool SameAsRead(object quest, uint next)
    {
        return _whenRead.TryGetValue(quest, out var box) && box.Value == next;
    }
}

/// <summary>
/// The order a quest's children (dialog branches, topics, scenes) were read in, kept only when it isn't the order the
/// writer uses (branches, then topics, then scenes): which kind came at each place. The writer follows it while the
/// quest still holds as many of each kind, so a plugin that mixes topics and scenes (Aurie_UpgradedCompanions.esm) is
/// written back in its own order. Both readers keep it (the full import and the overlay).
/// </summary>
internal static class QuestChildrenOrder
{
    public const byte Branch = 0, Topic = 1, Scene = 2;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, byte[]> _asRead = new();

    public static byte Kind(object child) => child switch
    {
        IDialogBranchGetter => Branch,
        IDialogTopicGetter => Topic,
        _ => Scene,
    };

    public static byte Kind(RecordType type) =>
        type == RecordTypes.DLBR ? Branch : type == RecordTypes.DIAL ? Topic : Scene;

    public static void Remember(object quest, IEnumerable<byte> kinds)
    {
        var asRead = kinds.ToArray();
        bool writersOrder = true;
        for (int i = 1; i < asRead.Length; i++)
        {
            if (asRead[i] < asRead[i - 1]) { writersOrder = false; break; }
        }
        if (writersOrder)
        {
            _asRead.Remove(quest);
            return;
        }
        _asRead.AddOrUpdate(quest, asRead);
    }

    public static byte[]? TryGet(object quest, int scenes, int topics, int branches)
    {
        if (!_asRead.TryGetValue(quest, out var asRead)) return null;
        if (asRead.Length != scenes + topics + branches) return null;
        int s = 0, t = 0, b = 0;
        foreach (var kind in asRead)
        {
            if (kind == Branch) b++; else if (kind == Topic) t++; else s++;
        }
        return s == scenes && t == topics && b == branches ? asRead : null;
    }
}

partial class QuestBinaryWriteTranslation
{
    public static partial void WriteBinaryDialogConditionsCustom(MutagenWriter writer, IQuestGetter item)
    {
        ConditionBinaryWriteTranslation.WriteConditionsList(item.DialogConditions, writer);
    }

    public static partial void WriteBinaryUnusedConditionsLogicCustom(MutagenWriter writer, IQuestGetter item)
    {
        var unusedConditions = item.UnusedConditions;
        if (unusedConditions == null) return;
        using (HeaderExport.Subrecord(writer, RecordTypes.NEXT))
        {
        }

        ConditionBinaryWriteTranslation.WriteConditionsList(unusedConditions, writer);
    }

    public static partial void WriteBinaryAliasParseCustom(MutagenWriter writer, IQuestGetter item)
    {
        var aliases = item.Aliases;
        var nextAliasId = item.NextAliasID;
        if (aliases == null)
        {
            if (nextAliasId.HasValue)
            {
                using (HeaderExport.Subrecord(writer, RecordTypes.ANAM))
                {
                    writer.Write(nextAliasId.Value);
                }
            }
            return;
        }
        using (HeaderExport.Subrecord(writer, RecordTypes.ANAM))
        {
            uint next = QuestAliasCounter.NextFromAliases(aliases);
            // The next alias ID is a counter that can sit above the existing aliases (IDs of deleted aliases are not
            // reused) and, in real plugins, below them too (SFBGS050.esm's SFTER_INC_Dialogue stores 0 with an alias 0).
            // A quest whose aliases are as they were read keeps the counter it was read with, whatever it is; once the
            // aliases' IDs changed, the stored counter is kept unless it would collide
            if (nextAliasId is not { } stored)
            {
                writer.Write(next);
            }
            else if (item is PluginBinaryOverlay || QuestAliasCounter.SameAsRead(item, next))
            {
                writer.Write(stored);
            }
            else
            {
                writer.Write(Math.Max(next, stored));
            }
        }

        foreach (var alias in aliases)
        {
            switch (alias)
            {
                case IQuestReferenceAliasGetter refAlias:
                    refAlias.WriteToBinary(writer);
                    break;
                case IQuestLocationAliasGetter locAlias:
                    locAlias.WriteToBinary(writer);
                    break;
                case IQuestCollectionAliasGetter collAlias:
                    collAlias.WriteToBinary(writer);
                    break;
                default:
                    throw new NotImplementedException();
            }
            using (HeaderExport.Subrecord(writer, RecordTypes.ALED)) { }
        }
    }

    public static partial void WriteSubgroupsLogic(MutagenWriter writer, IQuestGetter obj)
    {
        try
        {
            var scenes = obj.Scenes;
            var dialogTopics = obj.DialogTopics;
            var dialogBranches = obj.DialogBranches;
            if (scenes.Count == 0
                && dialogTopics.Count == 0
                && dialogBranches.Count == 0)
            {
                return;
            }
            using (HeaderExport.Header(writer, RecordTypes.GRUP, ObjectType.Group))
            {
                Mutagen.Bethesda.Plugins.Records.RawHeaderFormIds.WriteGroupLabel(writer, obj, (int)GroupTypeEnum.QuestChildren);
                writer.Write((int)GroupTypeEnum.QuestChildren);
                writer.Write(obj.Timestamp);
                writer.Write(obj.Unknown);
                // Children read in another order than branches, topics, scenes (Aurie_UpgradedCompanions.esm mixes
                // topics and scenes) are written in the order they were read while they are the same records
                if (QuestChildrenOrder.TryGet(obj, scenes.Count, dialogTopics.Count, dialogBranches.Count) is { } asRead)
                {
                    int nextScene = 0, nextTopic = 0, nextBranch = 0;
                    foreach (var kind in asRead)
                    {
                        switch (kind)
                        {
                            case QuestChildrenOrder.Branch: dialogBranches[nextBranch++].WriteToBinary(writer); break;
                            case QuestChildrenOrder.Topic: dialogTopics[nextTopic++].WriteToBinary(writer); break;
                            default: scenes[nextScene++].WriteToBinary(writer); break;
                        }
                    }
                    return;
                }
                ListBinaryTranslation<IDialogBranchGetter>.Instance.Write(
                    writer: writer,
                    items: dialogBranches,
                    transl: (MutagenWriter subWriter, IDialogBranchGetter subItem) =>
                    {
                        subItem.WriteToBinary(subWriter);
                    });
                ListBinaryTranslation<IDialogTopicGetter>.Instance.Write(
                    writer: writer,
                    items: dialogTopics,
                    transl: (MutagenWriter subWriter, IDialogTopicGetter subItem) =>
                    {
                        subItem.WriteToBinary(subWriter);
                    });
                ListBinaryTranslation<ISceneGetter>.Instance.Write(
                    writer: writer,
                    items: scenes,
                    transl: (MutagenWriter subWriter, ISceneGetter subItem) =>
                    {
                        subItem.WriteToBinary(subWriter);
                    });
            }
        }
        catch (Exception ex)
        {
            RecordException.EnrichAndThrow(ex, obj);
            throw;
        }
    }
}

partial class QuestBinaryOverlay
{
    public IReadOnlyList<IConditionGetter> DialogConditions { get; private set; } = [];
    public IReadOnlyList<IConditionGetter>? UnusedConditions { get; private set; }
    public UInt32? NextAliasID { get; private set; }
    public IReadOnlyList<IAQuestAliasGetter>? Aliases { get; private set; }

    private ReadOnlyMemorySlice<byte>? _grupData;

    public int Timestamp => _grupData != null
        ? BinaryPrimitives.ReadInt32LittleEndian(_package.MetaData.Constants.GroupHeader(_grupData.Value)
            .LastModifiedData)
        : 0;

    public int Unknown =>
        _grupData.HasValue ? BinaryPrimitives.ReadInt32LittleEndian(_grupData.Value.Slice(20)) : default;

    public IReadOnlyList<ISceneGetter> Scenes { get; private set; } = [];

    public IReadOnlyList<IDialogTopicGetter> DialogTopics { get; private set; } = [];

    public IReadOnlyList<IDialogBranchGetter> DialogBranches { get; private set; } =
        [];

    partial void DialogConditionsCustomParse(OverlayStream stream, int finalPos, int offset, RecordType type,
        PreviousParse lastParsed)
    {
        DialogConditions = ConditionBinaryOverlay.ConstructBinayOverlayList(stream, _package);
    }

    public partial ParseResult UnusedConditionsLogicCustomParse(OverlayStream stream, int offset,
        PreviousParse lastParsed)
    {
        var nextHeader = stream.ReadSubrecord(RecordTypes.NEXT);
        if (nextHeader.Content.Length != 0)
        {
            throw new ArgumentException("Unexpected NEXT header");
        }

        UnusedConditions = ConditionBinaryOverlay.ConstructBinayOverlayList(stream, _package);

        return null;
    }

    private static readonly IReadOnlyRecordCollection _aliasEndTriggers = RecordCollection.Factory(RecordTypes.ALED);
    private static readonly IReadOnlyRecordCollection _aliasStartTriggers = RecordCollection.Factory(
        RecordTypes.ALST,
        RecordTypes.ALLS,
        RecordTypes.ALCS);
    
    public partial ParseResult AliasParseCustomParse(OverlayStream stream, int offset, PreviousParse lastParsed)
    {
        if (stream.TryReadSubrecord(RecordTypes.ANAM, out var nextAliasId))
        {
            NextAliasID = nextAliasId.AsUInt32();
        }
        var mem = stream.RemainingMemory;
        var locs = ParseRecordLocationsEnder(
            stream,
            startTriggers: _aliasStartTriggers,
            endTriggers: _aliasEndTriggers,
            _package.MetaData.Constants.SubConstants,
            skipHeader: false);
        Aliases = BinaryOverlayList.FactoryByArray<IAQuestAliasGetter>(
            mem,
            _package,
            locs: locs,
            getter: (s, p) =>
            {
                var subRec = p.MetaData.Constants.Subrecord(s);
                switch (subRec.RecordTypeInt)
                {
                    case RecordTypeInts.ALST:
                    {
                        return QuestReferenceAliasBinaryOverlay
                            .QuestReferenceAliasFactory(s, p);
                    }
                    case RecordTypeInts.ALLS:
                    {
                        return QuestLocationAliasBinaryOverlay
                            .QuestLocationAliasFactory(s, p);
                    }
                    case RecordTypeInts.ALCS:
                    {
                        return QuestCollectionAliasBinaryOverlay.QuestCollectionAliasFactory(s, p);
                    }
                    default:
                        throw new NotImplementedException();
                }
            });
        return (int)Quest_FieldIndex.Aliases;
    }

    public static RecordTriggerSpecs QuestSubGroupTriggerSpecs => _subGroupTriggerSpecs.Value;

    private static readonly Lazy<RecordTriggerSpecs> _subGroupTriggerSpecs = new Lazy<RecordTriggerSpecs>(() =>
    {
        var triggers =
            RecordCollection.Factory(
                RecordTypes.DIAL,
                RecordTypes.DLBR,
                RecordTypes.SCEN);
        var all =
            RecordCollection.Factory(
                RecordTypes.DIAL,
                RecordTypes.DLBR,
                RecordTypes.SCEN,
                RecordTypes.GRUP);
        return new RecordTriggerSpecs(allRecordTypes: all, triggeringRecordTypes: triggers);
    });

    public partial void ParseSubgroupsLogic(OverlayStream stream, int finalPos, int offset)
    {
        try
        {
            if (stream.Complete) return;
            if (!stream.TryGetGroupHeader(out var groupMeta)) return;
            if (groupMeta.GroupType != (int)GroupTypeEnum.QuestChildren) return;
            this._grupData = stream.ReadMemory(checked((int)groupMeta.TotalLength));
            // A label stored in another form than its parent's header is written back as stored (GroupLabelForms, C188)
            Mutagen.Bethesda.Plugins.Records.GroupLabelForms.Remember(this, groupMeta.GroupType, BinaryPrimitives.ReadUInt32LittleEndian(groupMeta.ContainedRecordTypeData), _package.MetaData.MasterReferences);
            var formKey = FormKey.Factory(
                _package.MetaData.MasterReferences, 
                new FormID(BinaryPrimitives.ReadUInt32LittleEndian(groupMeta.ContainedRecordTypeData)),
                reference: true);
            if (formKey != this.FormKey)
            {
                RecordException.EnrichAndThrow(
                    new ArgumentException("Quest children group did not match the FormID of the parent."),
                    this);
            }
            var contentSpan = this._grupData.Value.Slice(_package.MetaData.Constants.GroupConstants.HeaderLength);
            var locs = ParseRecordLocations(
                    stream: new OverlayStream(contentSpan, _package),
                    trigger: QuestSubGroupTriggerSpecs,
                    constants: stream.MetaData.Constants.MajorConstants,
                    triggersAlwaysAreNewRecords: true,
                    skipHeader: false).Select(x => _package.MetaData.Constants.MajorRecordHeader(contentSpan.Slice(x)).Pin(x));
            QuestChildrenOrder.Remember(this, locs.Select(l => QuestChildrenOrder.Kind(l.RecordType)).ToArray());

            this.DialogBranches = BinaryOverlayList.FactoryByArray<IDialogBranchGetter>(
                contentSpan,
                _package,
                getter: (s, p) => DialogBranchBinaryOverlay.DialogBranchFactory(new OverlayStream(s, p), p),
                locs: locs.Where(s => s.RecordType == RecordTypes.DLBR).Select(x => x.Location).ToArray());

            this.DialogTopics = BinaryOverlayList.FactoryByArray<IDialogTopicGetter>(
                contentSpan,
                _package,
                getter: (s, p) => DialogTopicBinaryOverlay.DialogTopicFactory(new OverlayStream(s, p), p),
                locs: locs.Where(s => s.RecordType == RecordTypes.DIAL).Select(x => x.Location).ToArray());

            this.Scenes = BinaryOverlayList.FactoryByArray<ISceneGetter>(
                contentSpan,
                _package,
                getter: (s, p) => SceneBinaryOverlay.SceneFactory(new OverlayStream(s, p), p),
                locs: locs.Where(s => s.RecordType == RecordTypes.SCEN).Select(x => x.Location).ToArray());
        }
        catch (Exception ex)
        {
            RecordException.EnrichAndThrow(ex, this);
            throw;
        }
    }
}
public partial class QuestLocationAlias
{
    /// <summary>
    /// Location alias ALFG. Creation Kit 1.16.244.0: 2 is the check box "Force to Player location in Cruise Mode"
    /// (set on the 89 "OrbitLocation" aliases of the space encounter quests, e.g. SE_AG01 (0013DD:Starfield.esm)).
    /// Other bits are kept as stored.
    /// </summary>
    [Flags]
    public enum ExtraFlag
    {
        ForceToPlayerLocationInCruiseMode = 0x2,
    }
}
