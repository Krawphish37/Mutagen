using System.Buffers.Binary;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Starfield.Internals;
using Noggog;

namespace Mutagen.Bethesda.Starfield;

// CDNS (xEdit "Populations Count") counts the crowd's population entries; each entry is PRPS, CITC + CTDA conditions,
// STRV and FLTV. Read by hand because the generated counted list doesn't read these entries: the overlay finds an
// entry's start by subrecord type order (BinaryOverlayList.FactoryByCountPerItem -> ParseRecordLocationsByCount), and
// the CTDA after CITC, and every repeated CTDA, look like the start of a new entry (2 entries became 8).
partial class CrowdComponentBinaryCreateTranslation
{
    public static partial void FillBinaryItemsCustom(
        MutagenFrame frame,
        ICrowdComponent item,
        PreviousParse lastParsed)
    {
        // The frame given holds only the first subrecord; the entries follow it
        var reader = new MutagenFrame(frame.Reader);
        item.Items = ListBinaryTranslation<CrowdComponentItem>.Instance.ParsePerItem(
                reader: reader,
                countRecord: RecordTypes.CDNS,
                countLengthLength: 4,
                transl: CrowdComponentItem.TryCreateFromBinary,
                triggeringRecord: CrowdComponentItem_Registration.TriggerSpecs,
                nullIfZero: false)
            .ToExtendedList();
    }
}

partial class CrowdComponentBinaryWriteTranslation
{
    public static partial void WriteBinaryItemsCustom(
        MutagenWriter writer,
        ICrowdComponentGetter item)
    {
        if (item.Items is not { } items) return;
        using (HeaderExport.Subrecord(writer, RecordTypes.CDNS))
        {
            writer.Write(checked((uint)items.Count));
        }
        foreach (var entry in items)
        {
            entry.WriteToBinary(writer);
        }
    }
}

partial class CrowdComponentBinaryOverlay
{
    public IReadOnlyList<ICrowdComponentItemGetter>? Items { get; private set; }

    partial void ItemsCustomParse(
        OverlayStream stream,
        int finalPos,
        int offset,
        RecordType type,
        PreviousParse lastParsed)
    {
        uint? count = null;
        if (type == RecordTypes.CDNS)
        {
            count = BinaryPrimitives.ReadUInt32LittleEndian(stream.ReadSubrecord(RecordTypes.CDNS).Content);
        }
        var items = new List<ICrowdComponentItemGetter>();
        // Each entry reads its own subrecords and stops at the next entry's first one
        while ((count == null || items.Count < count) && !stream.Complete)
        {
            var header = stream.GetSubrecordHeader();
            if (!CrowdComponentItem_Registration.TriggerSpecs.AllRecordTypes.Contains(header.RecordType)) break;
            var start = stream.Position;
            items.Add(CrowdComponentItemBinaryOverlay.CrowdComponentItemFactory(stream, _package));
            if (stream.Position == start) break;
        }
        Items = items;
    }
}
