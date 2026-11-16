using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Noggog;

namespace Mutagen.Bethesda.Starfield;

partial class PackageEventBinaryCreateTranslation
{
    public static partial void FillBinaryTopicsCustom(MutagenFrame frame, IPackageEvent item, PreviousParse lastParsed)
    {
        if (ValuePositionRecorder.On) ValuePositionRecorder.List(frame, nameof(item.Topics));
        item.Topics.SetTo(ATopicReferenceBinaryCreateTranslation.Factory(frame));
        if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
    }
}

partial class PackageEventBinaryWriteTranslation
{
    public static partial void WriteBinaryTopicsCustom(MutagenWriter writer, IPackageEventGetter item)
    {
        ATopicReferenceBinaryWriteTranslation.Write(writer, item.Topics);
    }
}

partial class PackageEventBinaryOverlay
{
    public IReadOnlyList<IATopicReferenceGetter> Topics { get; private set; } = [];

    partial void TopicsCustomParse(
        OverlayStream stream,
        int finalPos,
        int offset,
        RecordType type,
        PreviousParse lastParsed)
    {
        Topics = new List<IATopicReferenceGetter>(
            ATopicReferenceBinaryCreateTranslation.Factory(
                new MutagenFrame(stream)));
    }
}