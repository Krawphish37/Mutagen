using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield.Internals;
using Mutagen.Bethesda.Translations.Binary;
using Noggog;

namespace Mutagen.Bethesda.Starfield;

public partial class PlacedNpc
{
    [Flags]
    public enum MajorFlag : uint
    {
        StartsDead = 0x0000_0200,
        Persistent = 0x0000_0400,
        InitiallyDisabled = 0x0000_0800,
        StartsUnconscious = 0x0000_2000,
        NoAiAcquire = 0x0200_0000,
        DoNotHavokSettle = 0x2000_0000,
    }
}

partial class PlacedNpcBinaryCreateTranslation
{
    public static partial ParseResult FillBinaryPlacementParseCustom(
        MutagenFrame frame,
        IPlacedNpcInternal item,
        PreviousParse lastParsed)
    {
        frame.ReadSubrecordHeader();
        item.Position = P3FloatBinaryTranslation<MutagenFrame, MutagenWriter>.Instance.Parse(frame);
        item.Rotation = P3FloatBinaryTranslation<MutagenFrame, MutagenWriter>.Instance.Parse(frame);
        item.HasPlacementData = true;
        return (int)PlacedNpc_FieldIndex.HasPlacementData;
    }
}

partial class PlacedNpcBinaryWriteTranslation
{
    public static partial void WriteBinaryPlacementParseCustom(
        MutagenWriter writer,
        IPlacedNpcGetter item)
    {
        if (item.IsDeleted
            && !item.HasPlacementData
            && item.Position == default
            && item.Rotation == default)
        {
            return;
        }
        using (HeaderExport.Subrecord(writer, RecordTypes.DATA))
        {
            P3FloatBinaryTranslation<MutagenFrame, MutagenWriter>.Instance.Write(writer, item.Position);
            P3FloatBinaryTranslation<MutagenFrame, MutagenWriter>.Instance.Write(writer, item.Rotation);
        }
    }
}

partial class PlacedNpcBinaryOverlay
{
    private int? _placementLocation;

    public bool HasPlacementData => _placementLocation.HasValue;

    private ReadOnlyMemorySlice<byte> PlacementContent => HeaderTranslation.ExtractSubrecordMemory(
        _recordData,
        _placementLocation!.Value,
        _package.MetaData.Constants);

    public P3Float Position => _placementLocation.HasValue
        ? P3FloatBinaryTranslation<MutagenFrame, MutagenWriter>.Instance.Read(PlacementContent)
        : default;

    public P3Float Rotation => _placementLocation.HasValue
        ? P3FloatBinaryTranslation<MutagenFrame, MutagenWriter>.Instance.Read(PlacementContent.Slice(12))
        : default;

    public partial ParseResult PlacementParseCustomParse(
        OverlayStream stream,
        int offset,
        PreviousParse lastParsed)
    {
        _placementLocation = stream.Position - offset;
        return (int)PlacedNpc_FieldIndex.HasPlacementData;
    }
}
