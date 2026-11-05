using System.Buffers.Binary;
using Mutagen.Bethesda.Plugins.Binary.Streams;

namespace Mutagen.Bethesda.Starfield;

internal static class VolumesComponentItemLayout
{
    // Type, four matrices and three floats: everything before the ender.
    public const int FixedLength = 0x50;

    /// <summary>
    /// Type 11 is followed by nothing or by one 4 byte value, and the type doesn't say which.
    /// Both occur in form version 582 plugins, as 84 and 88 byte single item VLMS subrecords.
    /// Decides by what still fits in the subrecord after the item's fixed part.
    /// </summary>
    public static bool Type11HasValue(ReadOnlySpan<byte> rest)
    {
        return !Fits(rest) && rest.Length >= 4;
    }

    /// <summary>
    /// Whether the items can all be read, the same way the reader will read them.
    /// VLMS holds only floats and type numbers, so a list that can't be read is safe to keep as raw bytes.
    /// </summary>
    public static bool CanRead(ReadOnlySpan<byte> content)
    {
        if (content.Length < 4) return false;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(content);
        var pos = 4;
        for (uint i = 0; i < count; i++)
        {
            if (pos + FixedLength > content.Length) return false;
            var type = BinaryPrimitives.ReadInt32LittleEndian(content.Slice(pos));
            pos += FixedLength;
            switch (type)
            {
                case 1:
                    pos += 4;
                    break;
                case 3:
                    pos += 8;
                    break;
                case 5:
                    pos += 12;
                    break;
                case 11:
                    pos += Type11HasValue(content.Slice(pos)) ? 4 : 0;
                    break;
                default:
                    return false;
            }
            if (pos > content.Length) return false;
        }
        return pos == content.Length;
    }

    private static bool Fits(ReadOnlySpan<byte> rest)
    {
        if (rest.Length == 0) return true;
        if (rest.Length < FixedLength) return false;
        var type = BinaryPrimitives.ReadInt32LittleEndian(rest);
        var after = rest.Slice(FixedLength);
        switch (type)
        {
            case 1:
                return after.Length >= 4 && Fits(after.Slice(4));
            case 3:
                return after.Length >= 8 && Fits(after.Slice(8));
            case 5:
                return after.Length >= 12 && Fits(after.Slice(12));
            case 11:
                return Fits(after) || (after.Length >= 4 && Fits(after.Slice(4)));
            default:
                return false;
        }
    }
}

partial class VolumesComponentItemBinaryWriteTranslation
{
    public static partial void WriteBinaryTypeParseCustom(
        MutagenWriter writer,
        IVolumesComponentItemGetter item)
    {
        writer.Write(item.Ender switch
        {
            IVolumesUnknownEnderSingleGetter _ => 1,
            IVolumesUnknownEnderDoubleGetter _ => 3,
            IVolumesUnknownEnderTrioGetter _ => 5,
            IVolumesUnknownEnderEmptyGetter _ => 11,
            IVolumesUnknownEnderEmptyPaddedGetter _ => 11,
            _ => throw new NotImplementedException()
        });
    }
    
    public static partial void WriteBinaryEnderCustom(
        MutagenWriter writer,
        IVolumesComponentItemGetter item)
    {
        item.Ender.WriteToBinary(writer);
    }
}

partial class VolumesComponentItemBinaryCreateTranslation
{
    public static partial void FillBinaryTypeParseCustom(
        MutagenFrame frame,
        IVolumesComponentItem item)
    {
        var type = frame.ReadInt32();
        switch (type)
        {
            case 1:
                item.Ender = new VolumesUnknownEnderSingle();
                break;
            case 3:
                item.Ender = new VolumesUnknownEnderDouble();
                break;
            case 5:
                item.Ender = new VolumesUnknownEnderTrio();
                break;
            case 11:
                item.Ender = new VolumesUnknownEnderEmpty();
                break;
            default:
                throw new NotImplementedException();
        }
    }
    
    public static partial void FillBinaryEnderCustom(
        MutagenFrame frame,
        IVolumesComponentItem item)
    {
        // Type 11 comes in two lengths, so the bytes left in the subrecord decide which one this is.
        // The frame is bounded to the VLMS subrecord by FillBinaryItemsCustom.
        if (item.Ender is VolumesUnknownEnderEmpty
            && VolumesComponentItemLayout.Type11HasValue(frame.RemainingSpan.Slice(0, checked((int)frame.Remaining))))
        {
            item.Ender = new VolumesUnknownEnderEmptyPadded();
        }
        item.Ender.CopyInFromBinary(frame);
    }
}

partial class VolumesComponentItemBinaryOverlay
{
    // _structData runs to the end of the VLMS subrecord, because VolumesComponent bounds it.
    private bool Type11HasValue() => VolumesComponentItemLayout.Type11HasValue(_structData.Slice(VolumesComponentItemLayout.FixedLength));

    partial void CustomEnderEndPos()
    {
        var val = BinaryPrimitives.ReadInt32LittleEndian(_structData);
        EnderEndingPos = 0x50;
        EnderEndingPos += val switch
        {
            1 => 4,
            3 => 8,
            5 => 12,
            11 => Type11HasValue() ? 4 : 0,
            _ => throw new NotImplementedException()
        };
    }
    
    public partial IAVolumesUnknownEnderGetter GetEnderCustom(int location)
    {
        var val = BinaryPrimitives.ReadInt32LittleEndian(_structData);
        return val switch
        {
            1 => VolumesUnknownEnderSingleBinaryOverlay.VolumesUnknownEnderSingleFactory(_structData.Slice(location),
                _package),
            3 => VolumesUnknownEnderDoubleBinaryOverlay.VolumesUnknownEnderDoubleFactory(_structData.Slice(location),
                _package),
            5 => VolumesUnknownEnderTrioBinaryOverlay.VolumesUnknownEnderTrioFactory(_structData.Slice(location),
                _package),
            11 when Type11HasValue() => VolumesUnknownEnderEmptyPaddedBinaryOverlay.VolumesUnknownEnderEmptyPaddedFactory(_structData.Slice(location),
                _package),
            11 => VolumesUnknownEnderEmptyBinaryOverlay.VolumesUnknownEnderEmptyFactory(_structData.Slice(location),
                _package),
            _ => throw new NotImplementedException()
        };
    }
}