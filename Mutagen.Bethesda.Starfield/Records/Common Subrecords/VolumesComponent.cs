using System.Buffers.Binary;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield.Internals;
using Noggog;

namespace Mutagen.Bethesda.Starfield;

// The items are read inside a view that ends with the VLMS subrecord, because the length of a
// type 11 item can only be told from how many bytes the subrecord still holds.
partial class VolumesComponentBinaryCreateTranslation
{
    public static partial void FillBinaryItemsCustom(
        MutagenFrame frame,
        IVolumesComponent item,
        PreviousParse lastParsed)
    {
        frame.Position += frame.MetaData.Constants.SubConstants.HeaderLength;
        var content = frame.RemainingSpan.Slice(0, checked((int)frame.Remaining));
        if (!VolumesComponentItemLayout.CanRead(content))
        {
            item.RawItems = frame.ReadBytes(checked((int)frame.Remaining));
            return;
        }
        item.Items =
            ListBinaryTranslation<VolumesComponentItem>.Instance.Parse(
                    amount: checked((int)frame.ReadUInt32()),
                    reader: frame,
                    transl: VolumesComponentItem.TryCreateFromBinary)
                .CastExtendedList<VolumesComponentItem>();
    }
}

partial class VolumesComponentBinaryWriteTranslation
{
    public static partial void WriteBinaryItemsCustom(
        MutagenWriter writer,
        IVolumesComponentGetter item)
    {
        if (item.RawItems is { } raw)
        {
            using (HeaderExport.Subrecord(writer, RecordTypes.VLMS))
            {
                writer.Write(raw);
            }
            return;
        }
        ListBinaryTranslation<IVolumesComponentItemGetter>.Instance.Write(
            writer: writer,
            items: item.Items,
            recordType: RecordTypes.VLMS,
            countLengthLength: 4,
            transl: (MutagenWriter subWriter, IVolumesComponentItemGetter subItem, TypedWriteParams conv) =>
            {
                subItem.WriteToBinary(subWriter, conv);
            });
    }
}

partial class VolumesComponentBinaryOverlay
{
    public IReadOnlyList<IVolumesComponentItemGetter>? Items { get; private set; }
    public ReadOnlyMemorySlice<byte>? RawItems { get; private set; }

    partial void ItemsCustomParse(
        OverlayStream stream,
        int finalPos,
        int offset,
        RecordType type,
        PreviousParse lastParsed)
    {
        var subrecord = stream.ReadSubrecord(RecordTypes.VLMS);
        if (!VolumesComponentItemLayout.CanRead(subrecord.Content))
        {
            RawItems = subrecord.Content.ToArray();
            return;
        }
        var count = BinaryPrimitives.ReadUInt32LittleEndian(subrecord.Content);
        Items = BinaryOverlayList.FactoryByCount<IVolumesComponentItemGetter>(
            stream: new OverlayStream(subrecord.Content.Slice(4), _package),
            package: _package,
            count: count,
            getter: (s, p) => VolumesComponentItemBinaryOverlay.VolumesComponentItemFactory(s, p));
    }
}
