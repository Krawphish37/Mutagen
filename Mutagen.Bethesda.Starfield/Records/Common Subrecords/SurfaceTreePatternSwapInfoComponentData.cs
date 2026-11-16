using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;

namespace Mutagen.Bethesda.Starfield;

// The surface tree pattern swap list (DAT2), per xEdit: a UInt32 count, then for each item a Surface Pattern link and
// one byte, and on every item but the last 4 more bytes ("usually an index to the pattern array"). Those 4 bytes are
// Unknown2: read when present and written back only when set, so the stored layout round-trips.
partial class SurfaceTreePatternSwapInfoComponentDataBinaryCreateTranslation
{
    public static partial void FillBinaryDataLogicCustom(MutagenFrame frame, ISurfaceTreePatternSwapInfoComponentData item)
    {
        if (ValuePositionRecorder.On) ValuePositionRecorder.List(frame, nameof(item.Data));
        var at = ValuePositionRecorder.Here(frame);
        var count = checked((int)frame.ReadUInt32());
        if (at >= 0) ValuePositionRecorder.Count(frame, at);
        for (int i = 0; i < count; i++)
        {
            var entry = new SurfaceTreePatternSwapInfoItem();
            if (ValuePositionRecorder.On) ValuePositionRecorder.Item(frame, i);
            at = ValuePositionRecorder.Here(frame);
            entry.SurfacePattern.SetTo(FormKeyBinaryTranslation.Instance.Parse(frame));
            ValuePositionRecorder.Value(frame, nameof(entry.SurfacePattern), at);
            at = ValuePositionRecorder.Here(frame);
            entry.Unknown = (sbyte)frame.ReadUInt8();
            ValuePositionRecorder.Value(frame, nameof(entry.Unknown), at);
            if (i < count - 1)
            {
                at = ValuePositionRecorder.Here(frame);
                entry.Unknown2 = frame.ReadUInt32();
                ValuePositionRecorder.Value(frame, nameof(entry.Unknown2), at);
            }
            if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
            item.Data.Add(entry);
        }
        if (ValuePositionRecorder.On) ValuePositionRecorder.End(frame);
    }
}

partial class SurfaceTreePatternSwapInfoComponentDataBinaryWriteTranslation
{
    public static partial void WriteBinaryDataLogicCustom(MutagenWriter writer, ISurfaceTreePatternSwapInfoComponentDataGetter item)
    {
        writer.Write(item.Data.Count);
        foreach (var entry in item.Data)
        {
            FormLinkBinaryTranslation.Instance.Write(writer, entry.SurfacePattern);
            writer.Write(entry.Unknown);
            if (entry.Unknown2 is { } unknown2)
            {
                writer.Write(unknown2);
            }
        }
    }
}

partial class SurfaceTreePatternSwapInfoComponentDataBinaryOverlay
{
    public IReadOnlyList<ISurfaceTreePatternSwapInfoItemGetter> Data { get; private set; } = Array.Empty<ISurfaceTreePatternSwapInfoItemGetter>();

    partial void CustomFactoryEnd(OverlayStream stream, int finalPos, int offset)
    {
        var frame = new MutagenFrame(new MutagenMemoryReadStream(_structData, _package.MetaData));
        var parsed = new SurfaceTreePatternSwapInfoComponentData();
        SurfaceTreePatternSwapInfoComponentDataBinaryCreateTranslation.FillBinaryDataLogicCustom(frame, parsed);
        Data = parsed.Data;
        DataLogicEndingPos = checked((int)frame.Position);
    }
}

partial class SurfaceTreePatternSwapInfoItemBinaryOverlay
{
    // Items are only read through SurfaceTreePatternSwapInfoComponentData (above), which builds them fully;
    // a lone item overlay has no way to know whether it is the last item.
    public UInt32? Unknown2 => null;
}
