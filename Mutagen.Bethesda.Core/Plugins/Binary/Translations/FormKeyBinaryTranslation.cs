using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Translations.Binary;
using System.Buffers.Binary;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Binary.Translations;

/// <summary>
/// Reads and writes a FormID as a FormKey. A master index past the plugin's master list reads as the plugin itself, as
/// the game reads it; the FormKey can't hold the stored index, so it is written as the plugin's own index. Within a
/// record read from a plugin the stored bytes of an unchanged link are put back when the record is written (C189,
/// decision 6.1 revised); an object read or written on its own, outside a record, gets the plugin's own index (C64).
/// </summary>
public sealed class FormKeyBinaryTranslation
{
    public static readonly FormKeyBinaryTranslation Instance = new();

    public FormKey Parse(
        ReadOnlySpan<byte> span,
        IReadOnlySeparatedMasterPackage masterReferences,
        bool maxIsNone = false,
        bool reference = true)
    {
        var id = BinaryPrimitives.ReadUInt32LittleEndian(span);
        if (maxIsNone && id == uint.MaxValue)
        {
            return FormKey.None;
        }

        return FormKey.Factory(masterReferences, new FormID(id), reference);
    }

    public FormKey Parse(
        SubrecordFrame frame,
        IReadOnlySeparatedMasterPackage masterReferences,
        bool maxIsNone = false,
        bool reference = true)
    {
        var id = frame.AsUInt32();
        if (maxIsNone && id == uint.MaxValue)
        {
            return FormKey.None;
        }

        return FormKey.Factory(masterReferences, new FormID(id), reference: reference);
    }

    public bool Parse<TReader>(
        TReader reader,
        out FormKey item,
        bool maxIsNone = false)
        where TReader : IMutagenReadStream
    {
        item = Parse(
            reader.ReadSpan(4),
            reader.MetaData.MasterReferences,
            maxIsNone: maxIsNone);
        return true;
    }

    public FormKey Parse<TReader>(TReader reader, bool reference = true)
        where TReader : IMutagenReadStream
    {
        return Parse(
            reader.ReadSpan(4),
            reader.MetaData.MasterReferences);
    }

    public void Write(
        MutagenWriter writer,
        IFormLinkIdentifier item,
        bool nullable = false,
        bool reference = true)
    {
        if (item.FormKey == FormKey.None)
        {
            UInt32BinaryTranslation<MutagenFrame, MutagenWriter>.Instance.Write(
                writer: writer,
                item: uint.MaxValue);
            return;
        }

        if (writer.MetaData.CleanNulls && item.FormKey.IsNull)
        {
            item = FormLinkInformation.Null;
        }

        var formID = writer.MetaData.SeparatedMasterPackage!.GetFormID(item.FormKey);

        // StoredFormIdMixIn.GetStoredFormIds: where this FormID goes (C203; nothing unless a record is being looked at)
        FormIdPositionRecorder.Note(writer, item.FormKey);
        UInt32BinaryTranslation<MutagenFrame, MutagenWriter>.Instance.Write(
            writer: writer,
            item: formID.Raw);
    }

    public void Write(
        MutagenWriter writer,
        IFormLinkIdentifier item,
        RecordType header)
    {
        try
        {
            using (HeaderExport.Header(writer, header, ObjectType.Subrecord))
            {
                Write(
                    writer,
                    item);
            }
        }
        catch (Exception ex)
        {
            SubrecordException.EnrichAndThrow(ex, header);
            throw;
        }
    }

    public void Write<TMajor>(
        MutagenWriter writer,
        IFormLinkNullableGetter<TMajor> item,
        RecordType header)
        where TMajor : class, IMajorRecordGetter
    {
        if (item.FormKeyNullable == null) return;
        Write(writer, item.ToStandardizedIdentifier(), header);
    }
}