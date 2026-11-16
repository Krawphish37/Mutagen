using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;

namespace Mutagen.Bethesda.Starfield;

partial class PerkAdapterBinaryCreateTranslation
{
    public static partial void FillBinaryScriptFragmentsCustom(MutagenFrame frame, IPerkAdapter item)
    {
        if (ValuePositionRecorder.On) ValuePositionRecorder.Field(frame, nameof(item.ScriptFragments));
        item.ScriptFragments = PerkScriptFragmentsBinaryCreateTranslation.ReadFragments(frame: frame, objectFormat: item.ObjectFormat);
        if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
    }
}

partial class PerkAdapterBinaryWriteTranslation
{
    public static partial void WriteBinaryScriptFragmentsCustom(MutagenWriter writer, IPerkAdapterGetter item)
    {
        if (item.ScriptFragments is not { } frags) return;
        PerkScriptFragmentsBinaryWriteTranslation.WriteFragments(writer, frags, item.ObjectFormat);
    }
}

partial class PerkAdapterBinaryOverlay
{
    public partial IPerkScriptFragmentsGetter? GetScriptFragmentsCustom(int location)
    {
        if (this.ScriptsEndingPos == _structData.Length) return null;
        return PerkScriptFragmentsBinaryCreateTranslation.ReadFragments(frame: new MutagenFrame(
            new OverlayStream(_structData.Slice(ScriptsEndingPos), _package)), objectFormat: ObjectFormat);
    }
}