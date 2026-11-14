using System.Buffers.Binary;
using System.IO.Compression;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Internals;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records.Internals;

namespace Mutagen.Bethesda.Plugins.Records;

/// <summary>
/// Finds records of one plugin that read as the same FormKey, from the record headers only, when a plugin is opened
/// (C204; DuplicateFormKeyOption.Refuse). Two stored FormIDs read as one FormKey when they are equal, or when both read as
/// the plugin itself in different forms (a full index and its FE/FD form, or a master index past the list). Clashes
/// across groups are found too (MissionsGalore.esm: GLOB 03000869 and TMLM 02000869).
/// </summary>
internal static class DuplicateFormKeyScan
{
    /// <summary>
    /// Throws DuplicateFormKeyException naming every clashing pair, if the plugin has any
    /// </summary>
    /// <param name="stream">The plugin, positioned anywhere; its position is restored</param>
    public static void ThrowIfAny(Stream stream, GameConstants constants, IReadOnlySeparatedMasterPackage package, ModKey modKey)
    {
        var start = stream.Position;
        try
        {
            var keys = new List<ulong>(1024);
            Walk(stream, constants, (raw, _, _, _) => keys.Add(Key(package, raw)));
            if (keys.Count < 2) return;
            var sorted = keys.ToArray();
            Array.Sort(sorted);
            HashSet<ulong>? clashing = null;
            for (int i = 1; i < sorted.Length; i++)
            {
                if (sorted[i] == sorted[i - 1]) (clashing ??= new()).Add(sorted[i]);
            }
            if (clashing == null) return;

            // Second pass, only for the clashing records: type, stored FormID, EditorID
            var found = new List<(ulong Key, RecordType Type, uint Raw, string? EditorId)>();
            Walk(stream, constants, (raw, type, recordStart, header) =>
            {
                var key = Key(package, raw);
                if (!clashing.Contains(key)) return;
                found.Add((key, type, raw, EditorId(stream, constants, recordStart, header)));
            });
            var pairs = new List<DuplicateFormKeyPair>();
            foreach (var group in found.GroupBy(f => f.Key))
            {
                var items = group.ToList();
                for (int i = 1; i < items.Count; i++)
                {
                    var a = items[i - 1];
                    var b = items[i];
                    pairs.Add(new DuplicateFormKeyPair(
                        package.GetFormKey(new FormID(b.Raw), reference: false),
                        a.Type, a.EditorId, a.Raw,
                        b.Type, b.EditorId, b.Raw));
                }
            }
            throw new DuplicateFormKeyException(modKey, pairs);
        }
        finally
        {
            stream.Position = start;
        }
    }

    // The FormKey a stored FormID reads as, as a number: a master's FormID as stored, the plugin's own as 1 << 32 + its ID
    private static ulong Key(IReadOnlySeparatedMasterPackage package, uint raw)
    {
        if (package is SeparatedMasterPackage separated) return separated.DuplicateKey(raw);
        var masters = (uint)package.Raw.Masters.Count;
        return (raw >> 24) < masters ? raw : (1UL << 32) | (raw & FormID.FullIdMask);
    }

    /// <summary>
    /// Every record header after the mod header: its stored FormID, type, position and header length
    /// </summary>
    private static void Walk(Stream stream, GameConstants constants, Action<uint, RecordType, long, int> onRecord)
    {
        var headerLength = constants.MajorConstants.HeaderLength;
        var groupHeaderLength = constants.GroupConstants.HeaderLength;
        var formIdOffset = constants.MajorConstants.FormIDLocationOffset;
        var header = new byte[headerLength];
        stream.Position = 0;
        var first = true;
        var length = stream.Length;
        while (stream.Position + headerLength <= length)
        {
            var recordStart = stream.Position;
            if (stream.Read(header, 0, headerLength) != headerLength) break;
            var type = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (type == RecordTypes.GRUP.TypeInt)
            {
                stream.Position = recordStart + groupHeaderLength;
                continue;
            }
            var size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
            var next = recordStart + headerLength + size;
            if (first)
            {
                // The mod header
                first = false;
                stream.Position = next;
                continue;
            }
            onRecord(BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(formIdOffset)), new RecordType(type), recordStart, headerLength);
            stream.Position = next;
        }
    }

    private static string? EditorId(Stream stream, GameConstants constants, long recordStart, int headerLength)
    {
        try
        {
            var back = stream.Position;
            stream.Position = recordStart;
            var header = new byte[headerLength];
            stream.ReadExactly(header);
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)));
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
            var content = new byte[size];
            stream.ReadExactly(content);
            stream.Position = back;
            if ((flags & Constants.CompressedFlag) != 0 && content.Length >= 4)
            {
                using var z = new ZLibStream(new MemoryStream(content, 4, content.Length - 4), CompressionMode.Decompress);
                using var ms = new MemoryStream();
                z.CopyTo(ms);
                content = ms.ToArray();
            }
            var sub = constants.SubConstants.HeaderLength;
            if (content.Length >= sub && BinaryPrimitives.ReadInt32LittleEndian(content) == RecordTypes.EDID.TypeInt)
            {
                int len = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(4));
                return System.Text.Encoding.ASCII.GetString(content, sub, Math.Max(0, Math.Min(len, content.Length - sub))).TrimEnd('\0');
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
