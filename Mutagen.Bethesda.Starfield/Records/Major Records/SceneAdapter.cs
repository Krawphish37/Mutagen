using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;

namespace Mutagen.Bethesda.Starfield;

partial class SceneAdapterBinaryCreateTranslation
{
    public static partial void FillBinaryScriptFragmentsCustom(
        MutagenFrame frame,
        ISceneAdapter item)
    {
        if (ValuePositionRecorder.On) ValuePositionRecorder.Field(frame, nameof(item.ScriptFragments));
        item.ScriptFragments = SceneScriptFragmentsBinaryCreateTranslation.ReadFragments(frame: frame, objectFormat: item.ObjectFormat);
        if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
    }
}

partial class SceneAdapterBinaryWriteTranslation
{
    public static partial void WriteBinaryScriptFragmentsCustom(MutagenWriter writer, ISceneAdapterGetter item)
    {
        if (item.ScriptFragments is not { } frags) return;
        SceneScriptFragmentsBinaryWriteTranslation.WriteFragments(writer: writer, objectFormat: item.ObjectFormat, item: item.ScriptFragments);
    }
}

partial class SceneAdapterBinaryOverlay
{
    public partial ISceneScriptFragmentsGetter? GetScriptFragmentsCustom(int location)
    {
        if (this.ScriptsEndingPos == _structData.Length) return null;
        return SceneScriptFragmentsBinaryCreateTranslation.ReadFragments(frame: new MutagenFrame(
            new OverlayStream(_structData.Slice(ScriptsEndingPos), _package)), objectFormat: ObjectFormat);
    }
}
