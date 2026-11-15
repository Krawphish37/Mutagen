using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Binary.Translations;

/// <summary>
/// For StoredFormIdMixIn.GetStoredFormIds (C203): while one record is written into a scratch buffer, notes where each
/// FormID the writer puts in it sits. The writer passes every link and FormKey through FormKeyBinaryTranslation.Write,
/// so the positions come from the record definitions themselves, not from looking at the bytes. Nothing is noted unless
/// a record is being looked at (one thread-static check per FormID written).
/// </summary>
internal sealed class FormIdPositionRecorder
{
    [ThreadStatic] private static FormIdPositionRecorder? _current;

    private readonly object _target;
    private readonly List<(Stream Stream, long Position, FormKey FormKey)> _noted = new();

    /// <summary>The record's subrecords as written back, once its write reached them</summary>
    public byte[]? Content { get; private set; }

    /// <summary>Each FormID written into the record's subrecords: its position in <see cref="Content"/> (null: written
    /// through a buffer the recorder couldn't follow) and the FormKey written</summary>
    public List<(int? Position, FormKey FormKey)> FormIds { get; } = new();

    private FormIdPositionRecorder(object target) => _target = target;

    /// <summary>Thrown once the record's subrecords are captured, so its child groups aren't written for nothing</summary>
    internal sealed class Done : Exception
    {
    }

    public static FormIdPositionRecorder Begin(object target)
    {
        var ret = new FormIdPositionRecorder(target);
        _current = ret;
        return ret;
    }

    public static void End() => _current = null;

    public static bool IsTarget(object record) => _current is { } c && ReferenceEquals(c._target, record) && c.Content == null;

    // Set once the record's subrecords start being written (its header's FormID is not one of them)
    private bool _started;

    public static void StartContent(object record)
    {
        if (_current is { } c && ReferenceEquals(c._target, record)) c._started = true;
    }

    public static void Note(MutagenWriter writer, FormKey formKey)
    {
        if (_current is not { Content: null, _started: true } c) return;
        c._noted.Add((writer.BaseStream, writer.Position, formKey));
    }

    /// <summary>
    /// Where the FormIDs noted from now on start: taken when a buffer is made whose content is later copied into another
    /// stream (<see cref="Relocate"/>). Nothing can be noted in that buffer before it exists, so its relocation only looks
    /// at the FormIDs noted after this mark.
    /// </summary>
    public static (object? Recorder, int Count) Mark() => _current is { } c ? (c, c._noted.Count) : (null, 0);

    /// <summary>
    /// A buffer's content was copied into another stream at <paramref name="offset"/> (an overflow-capable subrecord).
    /// <paramref name="since"/> is the <see cref="Mark"/> taken when the buffer was made: only the FormIDs noted after it
    /// can be in the buffer. Looking at every FormID noted so far made a record with many such subrecords quadratic (a
    /// navmesh info map: 56,631 NVMI and 2.5 million FormIDs, 50 s; perf-world).
    /// </summary>
    public static void Relocate(Stream from, Stream to, long offset, (object? Recorder, int Count) since = default)
    {
        if (_current is not { Content: null } c) return;
        // A mark from another recorder (or none) says nothing about this one's list: look at all of it
        int start = ReferenceEquals(since.Recorder, c) ? Math.Min(since.Count, c._noted.Count) : 0;
        for (int i = start; i < c._noted.Count; i++)
        {
            var n = c._noted[i];
            if (ReferenceEquals(n.Stream, from)) c._noted[i] = (to, n.Position + offset, n.FormKey);
        }
    }

    public static void Capture(object record, Stream buffer, ReadOnlySpan<byte> written)
    {
        if (_current is not { } c || !ReferenceEquals(c._target, record) || c.Content != null) return;
        c.Content = written.ToArray();
        foreach (var n in c._noted)
        {
            c.FormIds.Add((ReferenceEquals(n.Stream, buffer) ? checked((int)n.Position) : null, n.FormKey));
        }
        // Only a record that holds others (a cell, a worldspace, a quest, a topic) writes child groups after its
        // subrecords. Any other record's write is let finish (what is left is its header's length): an exception for
        // every record was most of what finding a placed object's FormIDs cost (perf-world). Notes and relocations
        // after this are ignored, since Content is set.
        if (record is IMajorRecordGetterEnumerable) throw new Done();
    }
}
