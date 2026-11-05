using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Translations.Binary;
using Noggog;

namespace Mutagen.Bethesda.Starfield;

partial class VirtualMachineAdapterIndexedBinaryCreateTranslation
{
    public static ScriptFragmentsIndexed ParseScriptFragments(MutagenFrame frame, ushort objFormat)
    {
        var ret = new ScriptFragmentsIndexed()
        {
            ExtraBindDataVersion = frame.ReadUInt8()
        };

        ret.Script = ReadEntry(frame, objFormat);
        // Flags (1), zero property count (2) and zero fragment count (2) left after an empty script name
        if (ret.Script.Name.IsNullOrEmpty() && frame.Remaining == 5 && frame.GetUInt16(offset: 1) == 0)
        {
            ret.EmptyScriptHasFlagsAndProperties = true;
            ret.Script.Flags = (ScriptEntry.Flag)frame.ReadUInt8();
            frame.Position += 2;
        }
        ret.Fragments.SetTo(
            Mutagen.Bethesda.Plugins.Binary.Translations.ListBinaryTranslation<ScriptFragmentIndexed>.Instance.Parse(
                amount: frame.ReadUInt16(),
                reader: frame,
                transl: ScriptFragmentIndexed.TryCreateFromBinary));
        return ret;
    }

    public static partial void FillBinaryScriptFragmentsCustom(MutagenFrame frame, IVirtualMachineAdapterIndexed item)
    {
        item.ScriptFragments = ParseScriptFragments(frame, item.ObjectFormat);
    }
}

partial class VirtualMachineAdapterIndexedBinaryWriteTranslation
{
    public static partial void WriteBinaryScriptFragmentsCustom(MutagenWriter writer, IVirtualMachineAdapterIndexedGetter item)
    {
        if (item.ScriptFragments is not { } frags) return;
        writer.Write(frags.ExtraBindDataVersion);
        WriteEntry(writer, frags.Script, item.ObjectFormat);
        if (frags.EmptyScriptHasFlagsAndProperties && frags.Script.Name.IsNullOrWhitespace())
        {
            writer.Write((byte)frags.Script.Flags);
            writer.Write((ushort)0);
        }
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