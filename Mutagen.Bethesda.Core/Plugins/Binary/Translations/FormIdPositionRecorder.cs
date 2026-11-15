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

    /// <summary>Whether a record's FormIDs are being collected on this thread</summary>
    public static bool Active => _current != null;

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
    /// A buffer's content was copied into another stream at <paramref name="offset"/> (an overflow-capable subrecord)
    /// </summary>
    public static void Relocate(Stream from, Stream to, long offset)
    {
        if (_current is not { Content: null } c) return;
        for (int i = 0; i < c._noted.Count; i++)
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
        throw new Done();
    }
}
