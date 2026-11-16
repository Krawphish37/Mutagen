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

        // A link read from one of the mod's own FormIDs in another form than the one written here, and still that
        // record, goes back as it was stored (StoredOwnLinkForms: the plugin header's TNAM lists, which no record keeps)
        if (writer.MetaData.SeparatedMasterPackage is Mutagen.Bethesda.Plugins.Masters.SeparatedMasterPackage { HasOwnFormVariants: true } own
            && StoredOwnLinkForms.TryGet(item, out var storedRaw)
            && storedRaw != formID.Raw
            && own.IsOwnFormVariant(storedRaw)
            && FormKey.Factory(own, new FormID(storedRaw), reference) == item.FormKey)
        {
            formID = new FormID(storedRaw);
        }

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
/// <summary>
/// The stored form of a link that was read from one of the mod's own FormIDs in a form Mutagen doesn't write (a Small
/// or Medium Starfield plugin's full index, where Mutagen writes FE / FD + its index), kept with the link object itself.
/// Records keep these by position (OwnFormIdForms); this covers links no record holds, the plugin header's TNAM lists
/// (ase4.esm, fleet commander.esm and eight more plugins in a 370-plugin Data folder store them in the full form).
/// FormKeyBinaryTranslation.Write puts the stored form back while the link still names that record of the mod.
/// </summary>
internal static class StoredOwnLinkForms
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, System.Runtime.CompilerServices.StrongBox<uint>> _stored = new();

    /// <summary>Notes the link's stored form when it is an own FormID in a form Mutagen doesn't write</summary>
    public static void Note(Mutagen.Bethesda.Plugins.Masters.IReadOnlySeparatedMasterPackage? package, uint raw, object link)
    {
        if (package is Mutagen.Bethesda.Plugins.Masters.SeparatedMasterPackage { HasOwnFormVariants: true } own
            && own.IsOwnFormVariant(raw))
        {
            _stored.AddOrUpdate(link, new System.Runtime.CompilerServices.StrongBox<uint>(raw));
        }
    }

    /// <summary>A copy of a link (a header copied to be written) keeps the stored form of the link it copies</summary>
    public static void Copy(object from, object to)
    {
        if (!ReferenceEquals(from, to) && _stored.TryGetValue(from, out var box))
        {
            _stored.AddOrUpdate(to, new System.Runtime.CompilerServices.StrongBox<uint>(box.Value));
        }
    }

    public static bool TryGet(object link, out uint raw)
    {
        if (_stored.TryGetValue(link, out var box))
        {
            raw = box.Value;
            return true;
        }
        raw = 0;
        return false;
    }
}
