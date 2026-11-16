using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Translations.Binary;
using Noggog;

namespace Mutagen.Bethesda.Starfield;

partial class VirtualMachineAdapterIndexedBinaryCreateTranslation
{
    public static ScriptFragmentsIndexed ParseScriptFragments(MutagenFrame frame, ushort objFormat)
    {
        var at = ValuePositionRecorder.Here(frame);
        var ret = new ScriptFragmentsIndexed()
        {
            ExtraBindDataVersion = frame.ReadUInt8()
        };
        ValuePositionRecorder.Value(frame, nameof(ret.ExtraBindDataVersion), at);

        if (ValuePositionRecorder.On) ValuePositionRecorder.Field(frame, nameof(ret.Script));
        ret.Script = ReadEntry(frame, objFormat);
        if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
        if (ValuePositionRecorder.On) ValuePositionRecorder.List(frame, nameof(ret.Fragments));
        var fragmentsCountAt = ValuePositionRecorder.Here(frame);
        var fragmentsCount = frame.ReadUInt16();
        if (fragmentsCountAt >= 0) ValuePositionRecorder.Count(frame, fragmentsCountAt);
        ret.Fragments.SetTo(
            Mutagen.Bethesda.Plugins.Binary.Translations.ListBinaryTranslation<ScriptFragmentIndexed>.Instance.Parse(
                amount: fragmentsCount,
                reader: frame,
                transl: ScriptFragmentIndexed.TryCreateFromBinary));
        if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
        return ret;
    }

    public static partial void FillBinaryScriptFragmentsCustom(MutagenFrame frame, IVirtualMachineAdapterIndexed item)
    {
        if (ValuePositionRecorder.On) ValuePositionRecorder.Field(frame, nameof(item.ScriptFragments));
        item.ScriptFragments = ParseScriptFragments(frame, item.ObjectFormat);
        if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
    }
}

partial class VirtualMachineAdapterIndexedBinaryWriteTranslation
{
    public static partial void WriteBinaryScriptFragmentsCustom(MutagenWriter writer, IVirtualMachineAdapterIndexedGetter item)
    {
        if (item.ScriptFragments is not { } frags) return;
        writer.Write(frags.ExtraBindDataVersion);
        WriteEntry(writer, frags.Script, item.ObjectFormat);
        ListBinaryTranslation<MutagenWriter, MutagenFrame, IScriptFragmentIndexedGetter>.Instance.Write(
            writer,
            frags.Fragments, 
            countLengthLength: 2,
            (w, i) =>
            {
                i.WriteToBinary(w);
            });
    }
}

partial class VirtualMachineAdapterIndexedBinaryOverlay
{
    public partial IScriptFragmentsIndexedGetter? GetScriptFragmentsCustom(int location)
    {
        if (this.ScriptsEndingPos == _structData.Length) return null;

        var frame = new MutagenFrame(new MutagenMemoryReadStream(_structData, _package.MetaData))
        {
            Position = ScriptsEndingPos
        };
        return VirtualMachineAdapterIndexedBinaryCreateTranslation.ParseScriptFragments(frame, this.ObjectFormat);
    }
}