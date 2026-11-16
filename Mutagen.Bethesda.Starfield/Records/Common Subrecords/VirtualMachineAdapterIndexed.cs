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
        // What the VMAD holds after its fragments is kept and written back (ScriptFragmentsTail)
        if (!frame.Complete)
        {
            ScriptFragmentsTail.Remember(ret, frame.ReadSpan(checked((int)frame.Remaining)).ToArray());
        }
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
        if (ScriptFragmentsTail.TryGet(frags) is { } tail)
        {
            writer.Write(tail);
        }
    }
}

/// <summary>
/// Bytes a VMAD holds after its script fragments, which no field reads: MissionsGalore.esm's four terminal menus end
/// their VMAD with three zero bytes after an unnamed fragment script and a fragment count of 0 (an unnamed script entry
/// written with its flags and property count, by the look of it). They are kept with the fragments object and written
/// back after it, so the VMAD is written as it was read.
/// </summary>
internal static class ScriptFragmentsTail
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, byte[]> _tails = new();

    public static void Remember(object fragments, byte[] tail)
    {
        if (tail.Length > 0) _tails.AddOrUpdate(fragments, tail);
    }

    public static byte[]? TryGet(object fragments) => _tails.TryGetValue(fragments, out var tail) ? tail : null;
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