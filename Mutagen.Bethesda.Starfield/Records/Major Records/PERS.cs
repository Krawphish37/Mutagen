using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Internals;
using Mutagen.Bethesda.Starfield.Internals;

namespace Mutagen.Bethesda.Starfield;

partial class PERSBinaryCreateTranslation
{
    internal static readonly IReadOnlyRecordCollection Targets = RecordCollection.Factory(RecordTypes.XXXX, RecordTypes.DAT2);
    
    public static partial ParseResult FillBinaryItemsParseCustom(
        MutagenFrame frame,
        IPERSInternal item,
        PreviousParse lastParsed)
    {
        frame = frame.SpawnAll();
        item.Items.Clear();
        // An XXXX gives the length of the DAT2 right after it only; any other DAT2 has its own length (C50: the length
        // was kept, so a later DAT2 without an XXXX was read with the earlier one's)
        int? contentLen = null;
        while (frame.TryGetSubrecord(Targets, out var subRec))
        {
            if (subRec.RecordType == RecordTypes.XXXX)
            {
                contentLen = subRec.AsInt32();
                frame.Position += subRec.TotalLength;
            }
            else
            {
                var length = contentLen ?? subRec.ContentLength;
                contentLen = null;
                frame.Position += subRec.HeaderLength;

                var items = Mutagen.Bethesda.Plugins.Binary.Translations.ListBinaryTranslation<PERSItem>.Instance.Parse(
                    reader: frame.SpawnWithLength(length),
                    transl: PERSItem.TryCreateFromBinary);
                item.Items.Add(new PERSItems()
                {
                    Items = items
                });
            }
        }
        
        return (int)PERS_FieldIndex.Items;
    }
}

partial class PERSBinaryWriteTranslation
{
    public static partial void WriteBinaryItemsParseCustom(
        MutagenWriter writer,
        IPERSGetter item)
    {
        foreach (var x in item.Items)
        {
            using (HeaderExport.Subrecord(writer, RecordTypes.DAT2,
                overflowRecord: RecordTypes.XXXX,
                out var writerToUse))
            {
                x.WriteToBinary(writerToUse);
            }
        }
    }
}

partial class PERSBinaryOverlay
{
    public IReadOnlyList<IPERSItemsGetter> Items { get; private set; } = [];

    public partial ParseResult ItemsParseCustomParse(
        OverlayStream stream,
        int offset,
        PreviousParse lastParsed)
    {
        var itemsList = new List<IPERSItemsGetter>();
        int? contentLen = null;
        while (stream.TryGetSubrecord(PERSBinaryCreateTranslation.Targets, out var subRec))
        {
            if (subRec.RecordType == RecordTypes.XXXX)
            {
                contentLen = subRec.AsInt32();
                stream.Position += subRec.TotalLength;
            }
            else
            {
                // as the full reader: an XXXX gives the length of the next DAT2 only
                var length = contentLen ?? subRec.ContentLength;
                contentLen = null;

                stream.Position += subRec.HeaderLength;
                
                itemsList.Add(PERSItemsBinaryOverlay.PERSItemsFactory(stream.ReadMemory(length), _package));
            }
        }

        Items = itemsList;
        
        return (int)PERS_FieldIndex.Items;
    }
}