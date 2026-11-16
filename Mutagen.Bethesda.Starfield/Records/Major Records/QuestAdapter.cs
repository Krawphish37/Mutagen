using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Noggog;
using System.Diagnostics.CodeAnalysis;

namespace Mutagen.Bethesda.Starfield;

partial class QuestAdapterBinaryCreateTranslation
{
    public static partial void FillBinaryFragmentCountCustom(MutagenFrame frame, IQuestAdapter item)
    {
        // The fragment count comes before the script, the list after it
        var countAt = ValuePositionRecorder.Here(frame);
        var count = frame.ReadUInt16();
        if (ValuePositionRecorder.On) ValuePositionRecorder.Field(frame, nameof(item.Script));
        item.Script = AVirtualMachineAdapterBinaryCreateTranslation.ReadEntry(frame, item.ObjectFormat);
        if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
        if (ValuePositionRecorder.On) ValuePositionRecorder.List(frame, nameof(item.Fragments));
        if (countAt >= 0) ValuePositionRecorder.CountAt(countAt, 2);
        item.Fragments.SetTo(
            ListBinaryTranslation<QuestScriptFragment>.Instance.Parse(
                frame,
                amount: count,
                transl: (MutagenFrame r, [MaybeNullWhen(false)] out QuestScriptFragment listSubItem) =>
                {
                    listSubItem = QuestScriptFragment.CreateFromBinary(frame);
                    return true;
                }));
        if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
        if (ValuePositionRecorder.On) ValuePositionRecorder.List(frame, nameof(item.Aliases));
        var aliasCountAt = ValuePositionRecorder.Here(frame);
        var aliasCount = frame.ReadUInt16();
        if (aliasCountAt >= 0) ValuePositionRecorder.Count(frame, aliasCountAt);
        item.Aliases.SetTo(
            ListBinaryTranslation<QuestFragmentAlias>.Instance.Parse(
                frame,
                amount: aliasCount,
                transl: (MutagenFrame r, [MaybeNullWhen(false)] out QuestFragmentAlias listSubItem) =>
                {
                    listSubItem = QuestFragmentAlias.CreateFromBinary(frame);
                    return true;
                }));
        if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
    }

    public static partial void FillBinaryScriptCustom(MutagenFrame frame, IQuestAdapter item)
    {
    }

    public static partial void FillBinaryAliasesCustom(MutagenFrame frame, IQuestAdapter item)
    {
    }

    public static partial void FillBinaryFragmentsCustom(MutagenFrame frame, IQuestAdapter item)
    {
    }
}

partial class QuestAdapterBinaryWriteTranslation
{
    public static partial void WriteBinaryFragmentCountCustom(MutagenWriter writer, IQuestAdapterGetter item)
    {
        var frags = item.Fragments;
        writer.Write(checked((ushort)frags.Count));
        AVirtualMachineAdapterBinaryWriteTranslation.WriteEntry(writer, item.Script, item.ObjectFormat);
        ListBinaryTranslation<IQuestScriptFragmentGetter>.Instance.Write(
            writer,
            frags,
            transl: (MutagenWriter subWriter, IQuestScriptFragmentGetter subItem) =>
            {
                subItem.WriteToBinary(subWriter);
            });
        var alias = item.Aliases;
        writer.Write((ushort)alias.Count);
        ListBinaryTranslation<IQuestFragmentAliasGetter>.Instance.Write(
            writer,
            alias,
            transl: (MutagenWriter subWriter, IQuestFragmentAliasGetter subItem) =>
            {
                subItem.WriteToBinary(subWriter);
            });
    }

    public static partial void WriteBinaryScriptCustom(MutagenWriter writer, IQuestAdapterGetter item)
    {
    }

    public static partial void WriteBinaryAliasesCustom(MutagenWriter writer, IQuestAdapterGetter item)
    {
    }

    public static partial void WriteBinaryFragmentsCustom(MutagenWriter writer, IQuestAdapterGetter item)
    {
    }
}

partial class QuestAdapterBinaryOverlay
{
    public IReadOnlyList<IQuestScriptFragmentGetter> Fragments { get; private set; } = [];

    public IReadOnlyList<IQuestFragmentAliasGetter> Aliases { get; private set; } = [];

    private IScriptEntryGetter? _scriptEntry;

    partial void CustomFactoryEnd(
            OverlayStream stream,
            int finalPos,
            int offset)
    {
        var frame = new MutagenFrame(
            new MutagenInterfaceReadStream(
                BinaryMemoryReadStream.LittleEndian(_structData.Slice(ScriptsEndingPos + 1)),
                _package.MetaData));
        if (frame.Complete) return;
        var count = frame.ReadUInt16();
        _scriptEntry = AVirtualMachineAdapterBinaryCreateTranslation.ReadEntry(frame, this.ObjectFormat);
        Fragments =
            ListBinaryTranslation<QuestScriptFragment>.Instance.Parse(
                    frame,
                    amount: count,
                    transl: (MutagenFrame r, [MaybeNullWhen(false)] out QuestScriptFragment listSubItem) =>
                    {
                        listSubItem = QuestScriptFragment.CreateFromBinary(frame);
                        return true;
                    })
                .ToList();
        var aliasCount = frame.ReadUInt16();
        Aliases =
            ListBinaryTranslation<QuestFragmentAlias>.Instance.Parse(
                    frame,
                    amount: aliasCount,
                    transl: (MutagenFrame r, [MaybeNullWhen(false)] out QuestFragmentAlias listSubItem) =>
                    {
                        listSubItem = QuestFragmentAlias.CreateFromBinary(frame);
                        return true;
                    })
                .ToList();
    }

    public partial IScriptEntryGetter GetScriptCustom(int location)
    {
        return _scriptEntry ?? new ScriptEntry();
    }
}
