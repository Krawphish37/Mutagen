using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Noggog;

namespace Mutagen.Bethesda.Starfield;

partial class PerkScriptFragmentsBinaryCreateTranslation
{
    public static PerkScriptFragments ReadFragments(MutagenFrame frame, ushort objectFormat)
    {
        var ret = new PerkScriptFragments();
        FillFragments(frame, objectFormat, ret);
        return ret;
    }

    public static void FillFragments(MutagenFrame frame, ushort objectFormat, IPerkScriptFragments item)
    {
        var at = ValuePositionRecorder.Here(frame);
        item.ExtraBindDataVersion = frame.ReadUInt8();
        ValuePositionRecorder.Value(frame, nameof(item.ExtraBindDataVersion), at);
        if (ValuePositionRecorder.On) ValuePositionRecorder.Field(frame, nameof(item.Script));
        item.Script = AVirtualMachineAdapterBinaryCreateTranslation.ReadEntry(frame, objectFormat);
        if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
        if (ValuePositionRecorder.On) ValuePositionRecorder.List(frame, nameof(item.Fragments));
        var fragmentsCountAt = ValuePositionRecorder.Here(frame);
        var fragmentsCount = frame.ReadUInt16();
        if (fragmentsCountAt >= 0) ValuePositionRecorder.Count(frame, fragmentsCountAt);
        item.Fragments.SetTo(
            Mutagen.Bethesda.Plugins.Binary.Translations.ListBinaryTranslation<PerkScriptFragment>.Instance.Parse(
                amount: fragmentsCount,
                reader: frame,
                transl: PerkScriptFragment.TryCreateFromBinary));
        if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
    }
}

partial class PerkScriptFragmentsBinaryWriteTranslation
{
    public static void WriteFragments(MutagenWriter writer, IPerkScriptFragmentsGetter item, ushort objectFormat)
    {
        writer.Write(item.ExtraBindDataVersion);
        AVirtualMachineAdapterBinaryWriteTranslation.WriteEntry(writer, item.Script, objectFormat);
        Mutagen.Bethesda.Plugins.Binary.Translations.ListBinaryTranslation<IPerkScriptFragmentGetter>.Instance.Write(
            writer: writer,
            items: item.Fragments,
            countLengthLength: 2,
            transl: (MutagenWriter subWriter, IPerkScriptFragmentGetter subItem, TypedWriteParams conv) =>
            {
                var Item = subItem;
                ((PerkScriptFragmentBinaryWriteTranslation)((IBinaryItem)Item).BinaryWriteTranslator).Write(
                    item: Item,
                    writer: subWriter,
                    translationParams: conv);
            });
    }
}

partial class PerkScriptFragmentsBinaryOverlay
{
    public IReadOnlyList<IPerkScriptFragmentGetter> Fragments { get; private set; } = null!;

    public byte ExtraBindDataVersion => throw new NotImplementedException();

    public IScriptEntryGetter Script => throw new NotImplementedException();

    public string FileName => throw new NotImplementedException();
}