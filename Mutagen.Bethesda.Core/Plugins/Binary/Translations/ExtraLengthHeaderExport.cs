using Mutagen.Bethesda.Plugins.Binary.Streams;

namespace Mutagen.Bethesda.Plugins.Binary.Translations;

public sealed class ExtraLengthHeaderExport : IDisposable
{
    // One per overflow-capable subrecord written, and almost all of them fit in a normal subrecord.
    // MemoryStream grows with the content, where MemoryTributary allocates a 64 KB block on first write
    private readonly MemoryStream _tributary = new();
    private readonly MutagenWriter _writer;
    private readonly RecordType _mainRecord;
    private readonly RecordType _overflowRecord;

    public MutagenWriter PrepWriter { get; }

    public ExtraLengthHeaderExport(
        MutagenWriter writer,
        RecordType mainRecord,
        RecordType overflowRecord)
    {
        _writer = writer;
        _mainRecord = mainRecord;
        _overflowRecord = overflowRecord;
        PrepWriter = new MutagenWriter(_tributary, writer.MetaData);
    }

    public void Dispose()
    {
        _tributary.Position = 0;
        if (_tributary.Length <= ushort.MaxValue)
        {
            using (HeaderExport.Subrecord(_writer, _mainRecord))
            {
                var contentStart = _writer.Position;
                FormIdPositionRecorder.Relocate(_tributary, _writer.BaseStream, contentStart);
                _tributary.CopyTo(_writer.BaseStream);
            }
        }
        else
        {
            using (HeaderExport.Subrecord(_writer, _overflowRecord))
            {
                _writer.Write(checked((uint)_tributary.Length));
            }
            _writer.Write(_mainRecord.TypeInt);
            _writer.WriteZeros(2);
            var overflowContentStart = _writer.Position;
            FormIdPositionRecorder.Relocate(_tributary, _writer.BaseStream, overflowContentStart);
            _tributary.CopyTo(_writer.BaseStream);
        }
    }
}
