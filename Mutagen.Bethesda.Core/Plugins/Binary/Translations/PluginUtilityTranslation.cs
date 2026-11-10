using Loqui;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Internals;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using Mutagen.Bethesda.Assets;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records.Internals;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Strings.DI;

namespace Mutagen.Bethesda.Plugins.Binary.Translations;

internal static class PluginUtilityTranslation
{
    internal delegate bool BinaryMasterParseDelegate<TItem>(
        MutagenFrame reader,
        [MaybeNullWhen(false)] out TItem item,
        TypedParseParams translationParams);

    internal delegate void BinaryMasterFillDelegate<TItem>(
        MutagenFrame reader,
        TItem item,
        TypedParseParams translationParams);

    internal delegate bool BinarySubParseRecordDelegate<TItem>(
        MutagenFrame reader,
        RecordType header,
        [MaybeNullWhen(false)] out TItem item);

    internal delegate bool BinaryMasterParseRecordDelegate<TItem>(
        MutagenFrame reader,
        RecordType header,
        [MaybeNullWhen(false)] out TItem item,
        TypedParseParams translationParams);

    internal delegate void BinaryMasterWriteDelegate<TItem>(
        MutagenWriter writer,
        TItem item,
        TypedWriteParams translationParams);

    internal delegate void RecordStructFill<R>(
        R record,
        MutagenFrame frame);

    internal delegate ParseResult RecordTypeFill<R>(
        R record,
        MutagenFrame frame,
        Dictionary<RecordType, int>? recordParseCount,
        RecordType nextRecordType,
        int contentLength,
        TypedParseParams translationParams);

    internal delegate ParseResult MajorRecordFill<R>(
        R record,
        MutagenFrame frame,
        PreviousParse lastParsed,
        Dictionary<RecordType, int>? recordParseCount,
        RecordType nextRecordType,
        int contentLength,
        TypedParseParams translationParams);

    internal delegate ParseResult SubrecordFill<R>(
        R record,
        MutagenFrame frame,
        PreviousParse previousSubrecordParse,
        Dictionary<RecordType, int>? recordParseCount,
        RecordType nextRecordType,
        int contentLength,
        TypedParseParams translationParams);

    internal delegate ParseResult ModRecordTypeFill<TRecord, TImportMask>(
        TRecord record,
        MutagenFrame frame,
        RecordType nextRecordType,
        int contentLength,
        TImportMask importMask,
        TypedParseParams translationParams);

    internal static M MajorRecordParse<M>(
        M record,
        MutagenFrame frame,
        TypedParseParams translationParams,
        RecordStructFill<M> fillStructs,
        MajorRecordFill<M> fillTyped)
        where M : IMajorRecordGetter
    {
        frame.MetaData.FormVersion = (ushort?)frame.Reader.GetMajorRecordHeader().FormVersion;
        frame = frame.SpawnWithFinalPosition(HeaderTranslation.ParseRecord(frame.Reader));
        fillStructs(
            record: record,
            frame: frame);
        // Where each field's subrecords start, to note records whose subrecords come in an order Mutagen does not write
        var fieldStarts = frame.MetaData.Constants.KeepSubrecordOrder ? SubrecordOrder.FieldStarts : null;
        var firstField = fieldStarts?.Count ?? 0;
        // Where the record held FormIDs Mutagen writes differently (matched by type and field only, see SubrecordOrder)
        var rewritten = fieldStarts != null ? SubrecordOrder.Rewritten : null;
        var firstRewritten = rewritten?.Count ?? 0;
        try
        {
            MutagenFrame targetFrame = frame;
            if (record.IsCompressed)
            {
                targetFrame = frame.Decompress();
            }

            Dictionary<RecordType, int>? recordParseCount = null;
            var lastParsed = new PreviousParse();
            var contentStart = targetFrame.Position;
            var highestIndex = -1;
            var outOfOrder = false;
            while (!targetFrame.Complete)
            {
                var subMeta = targetFrame.GetSubrecordHeader();
                var finalPos = targetFrame.Position + subMeta.TotalLength;
                var subrecordStart = targetFrame.Position;
                ParseResult parsed;
                try
                {
                    if (rewritten != null) SubrecordOrder.NonCanonicalFormIdRead = false;
                    parsed = fillTyped(
                        record: record,
                        frame: targetFrame,
                        lastParsed: lastParsed,
                        recordParseCount: recordParseCount,
                        nextRecordType: subMeta.RecordType,
                        contentLength: lastParsed.LengthOverride ?? subMeta.ContentLength,
                        translationParams: translationParams);
                }
                catch (Exception ex)
                    when (ex is not SubrecordException)
                {
                    throw new SubrecordException(
                        subMeta.RecordType,
                        record.FormKey,
                        majorRecordType: record.Registration.ClassType,
                        modKey: frame.Reader.MetaData.ModKey,
                        edid: record.EditorID,
                        innerException: ex);
                }

                if (!parsed.KeepParsing) break;
                if (rewritten != null && SubrecordOrder.NonCanonicalFormIdRead)
                {
                    rewritten.Add((checked((int)(subrecordStart - contentStart)), checked((int)(Math.Max(targetFrame.Position, finalPos) - contentStart))));
                }
                if (parsed.ParsedIndex is { } parsedIndex)
                {
                    fieldStarts?.Add((checked((int)(subrecordStart - contentStart)), parsedIndex));
                    if (parsedIndex < highestIndex)
                    {
                        outOfOrder = true;
                    }
                    else
                    {
                        highestIndex = parsedIndex;
                    }
                }
                if (parsed.DuplicateParseMarker != null)
                {
                    if (recordParseCount == null)
                    {
                        recordParseCount = new Dictionary<RecordType, int>();
                    }

                    recordParseCount[parsed.DuplicateParseMarker!.Value] =
                        recordParseCount.GetOrAdd(parsed.DuplicateParseMarker!.Value) + 1;
                }

                if (targetFrame.Position < finalPos)
                {
                    targetFrame.Position = finalPos;
                }

                lastParsed = parsed;
            }

            if (outOfOrder && fieldStarts != null)
            {
                targetFrame.Position = contentStart;
                SubrecordOrder.Remember(
                    record,
                    targetFrame.ReadSpan(checked((int)(targetFrame.FinalLocation - contentStart))),
                    frame.MetaData.Constants,
                    fieldStarts,
                    firstField,
                    rewritten,
                    firstRewritten);
            }

            frame.SetToFinalPosition();
            frame.MetaData.FormVersion = null;
            return record;
        }
        catch (Exception ex)
        {
            RecordException.EnrichAndThrow(ex, record);
            throw;
        }
        finally
        {
            fieldStarts?.RemoveRange(firstField, fieldStarts.Count - firstField);
            rewritten?.RemoveRange(firstRewritten, rewritten.Count - firstRewritten);
        }
    }

    internal static M RecordParse<M>(
        M record,
        MutagenFrame frame,
        RecordTypeConverter? recordTypeConverter,
        RecordStructFill<M> fillStructs)
    {
        fillStructs?.Invoke(
            record: record,
            frame: frame);
        return record;
    }

    internal static M RecordParse<M>(
        M record,
        MutagenFrame frame,
        TypedParseParams translationParams,
        RecordStructFill<M> fillStructs,
        MajorRecordFill<M> fillTyped)
    {
        fillStructs?.Invoke(
            record: record,
            frame: frame);
        Dictionary<RecordType, int>? recordParseCount = null;
        var previousParse = new PreviousParse();
        while (!frame.Complete)
        {
            var subMeta = frame.GetSubrecordHeader();
            var finalPos = frame.Position + subMeta.TotalLength;
            var parsed = fillTyped(
                record: record,
                frame: frame,
                lastParsed: previousParse,
                recordParseCount: recordParseCount,
                nextRecordType: subMeta.RecordType,
                contentLength: previousParse.LengthOverride ?? subMeta.ContentLength,
                translationParams: translationParams);
            if (!parsed.KeepParsing) break;
            if (parsed.DuplicateParseMarker != null)
            {
                if (recordParseCount == null)
                {
                    recordParseCount = new Dictionary<RecordType, int>();
                }

                recordParseCount[parsed.DuplicateParseMarker!.Value] =
                    recordParseCount.GetOrAdd(parsed.DuplicateParseMarker!.Value) + 1;
            }

            if (frame.Position < finalPos)
            {
                frame.Position = finalPos;
            }

            previousParse = parsed;
        }

        return record;
    }

    internal static M SubrecordParse<M>(
        M record,
        MutagenFrame frame,
        TypedParseParams translationParams,
        RecordStructFill<M> fillStructs)
    {
        fillStructs?.Invoke(
            record: record,
            frame: frame);
        return record;
    }

    internal static M SubrecordParse<M>(
        M record,
        MutagenFrame frame,
        TypedParseParams translationParams,
        SubrecordFill<M> fillTyped)
    {
        return SubrecordParse(
            record,
            frame,
            translationParams,
            fillStructs: null,
            fillTyped: fillTyped);
    }

    internal static M SubrecordParse<M>(
        M record,
        MutagenFrame frame,
        TypedParseParams translationParams,
        RecordStructFill<M>? fillStructs,
        SubrecordFill<M> fillTyped)
    {
        fillStructs?.Invoke(
            record: record,
            frame: frame);
        // A parent that read an overflow (XXXX) hands the real length of our first subrecord over in LengthOverride
        // (that subrecord's own header says 0). Apply it to the first subrecord only, like the parent's own loop does.
        var lastParsed = new PreviousParse(parsedIndex: null, lengthOverride: translationParams.LengthOverride);
        Dictionary<RecordType, int>? recordParseCount = null;

        // Keep going past the frame, as subrecord frames might not contain followup subrecords
        // when bundled.  Rely on Stop commands to break accordingly
        while (!frame.Reader.Complete)
        {
            var subMeta = frame.GetSubrecordHeader();
            var finalPos = frame.Position + subMeta.TotalLength;
            var parsed = fillTyped(
                record: record,
                frame: frame,
                previousSubrecordParse: lastParsed,
                recordParseCount: recordParseCount,
                nextRecordType: subMeta.RecordType,
                contentLength: lastParsed.LengthOverride ?? subMeta.ContentLength,
                translationParams: translationParams);
            if (!parsed.KeepParsing)
            {
                if (lastParsed.LengthOverride.HasValue)
                {
                    frame.Position -= frame.MetaData.Constants.SubConstants.HeaderLength + 4;
                }
                break;
            }
            if (frame.Position < finalPos)
            {
                frame.Position = finalPos;
            }

            if (parsed.DuplicateParseMarker != null)
            {
                if (recordParseCount == null)
                {
                    recordParseCount = new Dictionary<RecordType, int>();
                }

                recordParseCount[parsed.DuplicateParseMarker!.Value] =
                    recordParseCount.GetOrAdd(parsed.DuplicateParseMarker!.Value) + 1;
            }

            lastParsed = parsed;
        }

        return record;
    }

    internal static G GroupParse<G>(
        G record,
        MutagenFrame frame,
        TypedParseParams translationParams,
        RecordStructFill<G> fillStructs,
        RecordTypeFill<G> fillTyped)
    {
        var groupMeta = frame.GetGroupHeader();
        if (!groupMeta.IsGroup)
        {
            throw new ArgumentException($"Expected GRUP header was not read in: {frame.Position}");
        }

        frame.Position += groupMeta.TypeAndLengthLength;
        frame = frame.ReadAndReframe(checked((int)(groupMeta.TotalLength - groupMeta.TypeAndLengthLength)));

        fillStructs?.Invoke(
            record: record,
            frame: frame);
        while (!frame.Complete)
        {
            // Group contents are major records and subgroups, whose lengths are 4 bytes wide
            var nextHeader = frame.Reader.GetVariableHeader(subRecords: false);
            var nextRecordType = nextHeader.RecordType;
            var contentLength = checked((int)nextHeader.ContentLength);
            var finalPos = frame.Position + nextHeader.TotalLength;
            var parsed = fillTyped(
                record: record,
                frame: frame,
                recordParseCount: null,
                nextRecordType: nextRecordType,
                contentLength: contentLength,
                translationParams: translationParams);
            if (!parsed.KeepParsing) break;
            if (frame.Position < finalPos)
            {
                frame.Position = finalPos;
            }
        }

        frame.SetToFinalPosition();
        return record;
    }

    internal static TGroup GroupParse<TGroup, TRecord>(
        TGroup record,
        MutagenFrame frame,
        TypedParseParams translationParams,
        RecordType expectedRecordType,
        RecordStructFill<TGroup> fillStructs)
        where TGroup : IGroup<TRecord>
        where TRecord : class, IMajorRecord
    {
        return GroupParse(
            record,
            frame,
            translationParams,
            fillStructs,
            (rec, frame, rpCount, next, contLen, translParam) =>
            {
                if (next.Equals(expectedRecordType))
                {
                    Mutagen.Bethesda.Plugins.Binary.Translations.ListBinaryTranslation<TRecord>.Instance.Parse(
                        reader: frame,
                        triggeringRecord: expectedRecordType,
                        item: rec.RecordCache,
                        transl: LoquiBinaryTranslation<TRecord>.Instance.Parse);
                }
                else if (SubgroupsBinaryTranslation<TRecord>.TryReadOrphanedSubgroups(frame, out var record))
                {
                    rec.RecordCache.Add(record);
                }
                else
                {
                    frame.Position += contLen + frame.MetaData.Constants.MajorConstants.HeaderLength;
                }
                return default(int?);
            });
    }

    internal static TMod ModParse<TMod, TImportMask>(
        TMod record,
        MutagenFrame frame,
        TImportMask importMask,
        ModRecordTypeFill<TMod, TImportMask> fillTyped)
        where TMod : IMod, IClearable
    {
        record.Clear();
        var modHeader = frame.Reader.GetModHeader();
        // How the mod stores its own FormIDs, if read from its file, to write it back the same way
        Mutagen.Bethesda.Plugins.Masters.OwnFormIdStyle.Set(record,
            (frame.MetaData.MasterReferences as Mutagen.Bethesda.Plugins.Masters.SeparatedMasterPackage)?.OwnFormIdsStyled);
        fillTyped(
            record: record,
            frame: frame,
            importMask: importMask,
            nextRecordType: modHeader.RecordType,
            contentLength: checked((int)modHeader.ContentLength),
            translationParams: null);
        while (!frame.Complete)
        {
            var groupHeader = frame.GetGroupHeader();
            if (!groupHeader.IsGroup)
            {
                throw new ArgumentException("Did not see GRUP header as expected.");
            }

            var len = checked((int)groupHeader.ContentLength);
            var finalPos = frame.Position + groupHeader.TotalLength;

            var parsed = fillTyped(
                record: record,
                frame: frame,
                importMask: importMask,
                nextRecordType: groupHeader.ContainedRecordType,
                contentLength: len,
                translationParams: null);
            if (!parsed.KeepParsing) break;
            if (frame.Position < finalPos)
            {
                frame.Position = finalPos;
            }
        }

        frame.SetToFinalPosition();
        return record;
    }

    internal static async Task CompileStreamsInto(IEnumerable<Task<IEnumerable<Stream>>> inStreams, Stream outStream)
    {
        var streams = await Task.WhenAll(inStreams).ConfigureAwait(false);
        foreach (var s in streams.SelectMany(s => s))
        {
            s.Position = 0;
            s.CopyTo(outStream);
        }
    }

    internal static async Task CompileStreamsInto(Task<IEnumerable<Stream>> inStreams, Stream outStream)
    {
        var streams = await inStreams.ConfigureAwait(false);
        foreach (var s in streams)
        {
            s.Position = 0;
            s.CopyTo(outStream);
        }
    }

    internal static async Task CompileStreamsInto(IEnumerable<Task<Stream>> inStreams, Stream outStream)
    {
        foreach (var sTask in inStreams)
        {
            var s = await sTask.ConfigureAwait(false);
            s.Position = 0;
            s.CopyTo(outStream);
        }
    }

    internal static void CompileStreamsInto(IEnumerable<Stream> inStreams, Stream outStream)
    {
        foreach (var s in inStreams)
        {
            s.Position = 0;
            s.CopyTo(outStream);
        }
    }

    internal static void SetGroupLength(
        byte[] bytes,
        uint len)
    {
        var bytesSpan = bytes.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(bytesSpan.Slice(4), len);
    }

    internal static async Task<IEnumerable<Stream>> CompileSetGroupLength(
        IEnumerable<Task<Stream>> streams,
        byte[] bytes)
    {
        var ret = await Task.WhenAll(streams).ConfigureAwait(false);
        SetGroupLength(bytes, (uint)ret.Sum(i => i.Length));
        return ret;
    }

    internal static void CompileSetGroupLength(
        IEnumerable<Stream> streams,
        byte[] bytes)
    {
        SetGroupLength(bytes, (uint)streams.WhereNotNull().Sum(i => i.Length));
    }

    internal static void SkipPastAll(IBinaryReadStream stream, GameConstants meta, RecordType recordType)
    {
        while (stream.TryReadSubrecord(meta, recordType, out var _))
        {
        }
    }

    internal static RecordType GetRecordType<T>()
    {
        return (RecordType)LoquiRegistration.GetRegister(typeof(T))!.GetType()
            .GetField(Constants.TriggeringRecordTypeMember)!.GetValue(null)!;
    }

    internal static ReadOnlyMemorySlice<byte>? ReadByteArrayWithOverflow(
        ReadOnlyMemorySlice<byte> bytes,
        GameConstants constants,
        int? loc,
        int? lengthOverride)
    {
        if (!loc.HasValue) return null;
        var header = constants.Subrecord(bytes[loc.Value..]);
        if (lengthOverride != null)
        {
            return bytes.Slice(
                loc.Value + header.HeaderLength,
                lengthOverride.Value);
        }
        else
        {
            return header.Content;
        }
    }

    internal static int HandleOverlayRecordOverflow(
        int? existingLoc,
        OverlayStream stream,
        int offset,
        ReadOnlyMemorySlice<byte> data,
        GameConstants constants)
    {
        if (existingLoc.HasValue)
        {
            var overflowHeader = constants.Subrecord(data.Slice(existingLoc.Value));
            var len = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(overflowHeader.Content));
            // Need to skip the data record, which doesn't have a proper length
            stream.Position += constants.SubConstants.HeaderLength + len;
            return existingLoc.Value;
        }
        else
        {
            return stream.Position - offset;
        }
    }

    internal static AssetLink<TAssetType>? AssetNullableDeepCopyIn<TAssetType>(AssetLink<TAssetType>? lhs, IAssetLinkGetter<TAssetType>? rhs)
        where TAssetType : class, IAssetType
    {
        if (rhs == null) return null;
        if (lhs == null)
        {
            return new AssetLink<TAssetType>(rhs.GivenPath);
        }

        lhs.GivenPath = rhs.GivenPath;
        return lhs;
    }

    internal static BinaryWriteParameters SetStringsWriter(
        IModGetter mod,
        BinaryWriteParameters writeParameters,
        string path,
        ModKey modKey)
    {
        if (writeParameters.StringsWriter != null) return writeParameters;
        if (!mod.UsingLocalization) return writeParameters;

        return writeParameters with
        {
            StringsWriter = new StringsWriter(
                release: mod.GameRelease,
                modKey: modKey,
                writeDirectory: Path.Combine(Path.GetDirectoryName(path)!, "Strings"),
                encodingProvider: MutagenEncoding.Default,
                fileSystem: writeParameters.FileSystem.GetOrDefault())
        };
    }

    internal static void WriteMajorRecord<TMajor>(
        MutagenWriter writer,
        TypedWriteParams translationParams,
        RecordType type,
        TMajor item,
        Action<TMajor, MutagenWriter> writeEmbedded,
        Action<TMajor, MutagenWriter, TypedWriteParams> writeRecordTypes,
        RecordType? endMarker = null)
        where TMajor : IMajorRecordGetter
    {
        try
        {
            var headerPos = writer.Position;
            using (HeaderExport.Record(
                       writer: writer,
                       record: translationParams.ConvertToCustom(type)))
            {
                writeEmbedded(item, writer);
                // Starfield keeps what a deleted record holds. Writing the header only would lose it:
                // a placed reference's base object, a global's value, a cell's data.
                if (item.IsDeleted && writer.MetaData.Constants.Release.ToCategory() != GameCategory.Starfield)
                {
                    if (item.IsCompressed)
                    {
                        // Deleted records are written without content, so there is nothing to compress.
                        // Leaving the flag on would mark an empty body as compressed, which cannot be read back
                        ClearCompressedFlag(writer, headerPos, item.MajorRecordFlagsRaw);
                    }
                    return;
                }

                writer.MetaData.FormVersion = item.FormVersion;
                using (CompressionExport.Compression(item.IsCompressed, writer, out var writerToUse))
                {
                    if (writer.MetaData.Constants.KeepSubrecordOrder
                        && SubrecordOrder.TryGet(item, out var order))
                    {
                        // Written as usual into a buffer, then in the order the record was read in, if it is unchanged
                        using var buffer = new MemoryStream();
                        using (var bufferWriter = new MutagenWriter(buffer, writerToUse.MetaData, dispose: false))
                        {
                            writeRecordTypes(item, bufferWriter, translationParams);
                            if (endMarker != null)
                            {
                                using (HeaderExport.Subrecord(bufferWriter, endMarker.Value)) {}
                            }
                        }
                        var written = buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length));
                        if (!SubrecordOrder.TryWriteInOrder(written, order, writerToUse,
                                unchangeable: item is Mutagen.Bethesda.Plugins.Binary.Overlay.PluginBinaryOverlay))
                        {
                            writerToUse.Write(written);
                        }
                    }
                    else
                    {
                        writeRecordTypes(item, writerToUse, translationParams);
                        if (endMarker != null)
                        {
                            using (HeaderExport.Subrecord(writerToUse, endMarker.Value)) {}
                        }
                    }
                }
                writer.MetaData.FormVersion = null;
            }
        }
        catch (Exception ex)
        {
            RecordException.EnrichAndThrow(ex, item);
            throw;
        }
    }

    private static void ClearCompressedFlag(MutagenWriter writer, long headerPos, int flags)
    {
        var endPos = writer.Position;
        writer.Position = headerPos + writer.MetaData.Constants.MajorConstants.FlagLocationOffset;
        writer.Write(flags & ~Constants.CompressedFlag);
        writer.Position = endPos;
    }
}