using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records.Internals;

namespace Mutagen.Bethesda.Plugins.Binary.Translations;

/// <summary>
/// Keeps the empty top-level groups a plugin had when it was read (C136), for games whose files should come back as
/// they were (<see cref="GameConstants.KeepSubrecordOrder"/>; Starfield). The Creation Kit writes an empty 24-byte
/// group for some record types (Starfield.esm: NAVM, DIAL, INFO, DLBR, SCEN); Mutagen writes only groups with records.
/// <br/>
/// Each empty group is kept with the label of the non-empty top-level group it followed in the file, and is written
/// right after that group again. A group whose anchor isn't written any more is left out. A new mod has none.
/// </summary>
internal static class EmptyTopLevelGroups
{
    /// <param name="Anchor">The label of the non-empty top-level group it followed (0: the start of the file)</param>
    /// <param name="Header">The 24-byte group header as read</param>
    internal sealed record Entry(uint Anchor, byte[] Header);

    private static readonly ConditionalWeakTable<object, Entry[]> Groups = new();

    /// <summary>
    /// Collects a mod's empty top-level groups while it is read
    /// </summary>
    internal sealed class Collector
    {
        private readonly GameConstants _constants;
        private uint _anchor;
        private List<Entry>? _entries;

        public Collector(GameConstants constants) => _constants = constants;

        public void Saw(GroupHeader group)
        {
            if (!_constants.KeepSubrecordOrder) return;
            var label = BinaryPrimitives.ReadUInt32LittleEndian(group.HeaderData.Span.Slice(8));
            if (group.ContentLength == 0)
            {
                (_entries ??= new()).Add(new Entry(_anchor, group.HeaderData.Span.ToArray()));
            }
            else
            {
                _anchor = label;
            }
        }

        public void Finish(object mod)
        {
            if (_entries == null) Groups.Remove(mod);
            else Groups.AddOrUpdate(mod, _entries.ToArray());
        }
    }

    internal static bool TryGet(object mod, out Entry[] entries) => Groups.TryGetValue(mod, out entries!);

    internal static void Copy(object from, object to)
    {
        if (ReferenceEquals(from, to)) return;
        if (Groups.TryGetValue(from, out var entries)) Groups.AddOrUpdate(to, entries);
        else Groups.Remove(to);
    }

    /// <summary>
    /// Writes the mod's top-level group streams in order, with its kept empty groups in their places
    /// </summary>
    internal static void Compile(object mod, IEnumerable<Stream> groupStreams, Stream outStream)
    {
        if (!Groups.TryGetValue(mod, out var entries))
        {
            PluginUtilityTranslation.CompileStreamsInto(groupStreams, outStream);
            return;
        }
        void WriteAnchoredTo(uint anchor)
        {
            foreach (var e in entries)
            {
                if (e.Anchor == anchor) outStream.Write(e.Header);
            }
        }
        WriteAnchoredTo(0);
        var head = new byte[12];
        foreach (var s in groupStreams)
        {
            // Read the group's head (type, size, label) first, then copy it and the rest without seeking back
            s.Position = 0;
            var read = 0;
            while (read < head.Length)
            {
                var n = s.Read(head, read, head.Length - read);
                if (n <= 0) break;
                read += n;
            }
            outStream.Write(head, 0, read);
            s.CopyTo(outStream);
            if (read == 12) WriteAnchoredTo(BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(8)));
        }
    }

    private const string Prefix = "EG1:";

    internal static string? Export(object mod)
    {
        if (!Groups.TryGetValue(mod, out var entries)) return null;
        var bytes = new byte[entries.Length * 28];
        for (int i = 0; i < entries.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28 * i), entries[i].Anchor);
            entries[i].Header.CopyTo(bytes.AsSpan(28 * i + 4));
        }
        return Prefix + Convert.ToBase64String(bytes);
    }

    internal static void Import(object mod, string? value)
    {
        if (value == null)
        {
            Groups.Remove(mod);
            return;
        }
        if (!value.StartsWith(Prefix, StringComparison.Ordinal)) throw new FormatException($"Not a list of empty groups: {value}");
        var bytes = Convert.FromBase64String(value.Substring(Prefix.Length));
        if (bytes.Length % 28 != 0) throw new FormatException($"Not a list of empty groups: {value}");
        var entries = new Entry[bytes.Length / 28];
        for (int i = 0; i < entries.Length; i++)
        {
            var header = bytes.AsSpan(28 * i + 4, 24).ToArray();
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != RecordTypes.GRUP.TypeInt
                || BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4)) != 24)
            {
                throw new FormatException($"Not a list of empty groups: {value}");
            }
            entries[i] = new Entry(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(28 * i)), header);
        }
        if (entries.Length == 0) Groups.Remove(mod);
        else Groups.AddOrUpdate(mod, entries);
    }
}
