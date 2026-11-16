using Noggog;
using Loqui;
using static Mutagen.Bethesda.Translations.Binary.UtilityTranslation;
using Mutagen.Bethesda.Plugins.Binary.Translations;

namespace Mutagen.Bethesda.Translations.Binary;

public class ListBinaryTranslation<TWriter, TReader, TItem>
    where TWriter : IBinaryWriteStream
    where TReader : IBinaryReadStream
{
    public static readonly ListBinaryTranslation<TWriter, TReader, TItem> Instance = new();

    public static readonly bool IsLoqui;

    static ListBinaryTranslation()
    {
        IsLoqui = typeof(TItem).InheritsFrom(typeof(ILoquiObject));
    }

    public ExtendedList<TItem> Parse(
        TReader reader,
        BinarySubParseDelegate<TReader, TItem> transl)
    {
        var ret = new ExtendedList<TItem>();
        while (!reader.Complete)
        {
            if (ValuePositionRecorder.On) ValuePositionRecorder.Item(reader, ret.Count);
            if (transl(reader, out var subItem))
            {
                ret.Add(subItem);
                if (ValuePositionRecorder.On) ValuePositionRecorder.End(reader);
            }
            else
            {
                if (ValuePositionRecorder.On) ValuePositionRecorder.Drop();
                break;
            }
        }
        return ret;
    }

    public ExtendedList<TItem> ParseTrimNullEnding(
        TReader reader,
        BinarySubParseDelegate<TReader, TItem> transl)
    {
        var ret = new ExtendedList<TItem>();
        while (reader.Remaining > 1)
        {
            if (ValuePositionRecorder.On) ValuePositionRecorder.Item(reader, ret.Count);
            if (transl(reader, out var subItem))
            {
                ret.Add(subItem);
                if (ValuePositionRecorder.On) ValuePositionRecorder.End(reader);
            }
            else
            {
                if (ValuePositionRecorder.On) ValuePositionRecorder.Drop();
                break;
            }
        }
        return ret;
    }

    public ExtendedList<TItem> Parse(
        TReader reader,
        BinarySubParseDelegate<IBinaryReadStream, TItem> transl)
    {
        var ret = new ExtendedList<TItem>();
        while (!reader.Complete)
        {
            if (ValuePositionRecorder.On) ValuePositionRecorder.Item(reader, ret.Count);
            if (transl(reader, out var subItem))
            {
                ret.Add(subItem);
                if (ValuePositionRecorder.On) ValuePositionRecorder.End(reader);
            }
            else
            {
                if (ValuePositionRecorder.On) ValuePositionRecorder.Drop();
                break;
            }
        }
        return ret;
    }

    public ExtendedList<TItem> Parse(
        TReader reader,
        int amount,
        BinarySubParseDelegate<TReader, TItem> transl)
    {
        var ret = new ExtendedList<TItem>();
        for (int i = 0; i < amount; i++)
        {
            if (ValuePositionRecorder.On) ValuePositionRecorder.Item(reader, ret.Count);
            if (transl(reader, out var subItem))
            {
                ret.Add(subItem);
                if (ValuePositionRecorder.On) ValuePositionRecorder.End(reader);
            }
            else if (ValuePositionRecorder.On) ValuePositionRecorder.Drop();
        }
        return ret;
    }

    public void Write(
        TWriter writer,
        IEnumerable<TItem>? items,
        BinarySubWriteDelegate<TWriter, TItem> transl)
    {
        if (items == null) return;
        foreach (var item in items)
        {
            transl(writer, item);
        }
    }

    public void Write(
        TWriter writer,
        IReadOnlyList<TItem>? items,
        int countLengthLength,
        BinarySubWriteDelegate<TWriter, TItem> transl)
    {
        if (items == null) return;
        try
        {
            switch (countLengthLength)
            {
                case 2:
                    writer.Write(checked((ushort)items.Count));
                    break;
                case 4:
                    writer.Write(items.Count);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }
        catch (OverflowException overflow)
        {
            throw new OverflowException(
                $"List<{typeof(TItem)}> had an overflow with {items?.Count} items.",
                overflow);
        }
        foreach (var item in items)
        {
            transl(writer, item);
        }
    }
}