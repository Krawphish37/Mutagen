using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using Microsoft.Win32.SafeHandles;
using Mutagen.Bethesda.Archives.Ba2;
using Mutagen.Bethesda.Archives.Bsa;
using Noggog;

namespace Mutagen.Bethesda.Archives;

/// <summary>
/// An archive reader that keeps its archive file open until it is disposed (C199). Every entry read goes through that
/// one open file with positional reads, instead of opening the archive again for each entry as
/// <see cref="Archive.CreateReader"/>'s reader does. Several threads can read entries at once.<br/>
/// While it is open the archive can be read and deleted by others (FileShare.Read | FileShare.Delete), but not
/// overwritten or renamed over: dispose it when done. Entry streams handed out stop working once it is disposed.
/// </summary>
public interface IDisposableArchiveReader : IArchiveReader, IDisposable
{
}

/// <summary>
/// One open archive file, read by position. On the real file system it is a handle read with RandomAccess, which has
/// no shared position, so concurrent reads need no lock; on another file system (tests) it is one stream read under a
/// lock.
/// </summary>
internal sealed class KeptOpenArchiveFile : IDisposable
{
    public const FileShare Share = FileShare.Read | FileShare.Delete;

    private readonly SafeFileHandle? _handle;
    private readonly Stream? _stream;
    private readonly object _streamLock = new();
    private volatile bool _disposed;

    public long Length { get; }

    public KeptOpenArchiveFile(FilePath path, IFileSystem? fileSystem)
    {
        var fs = fileSystem.GetOrDefault();
        if (fs is FileSystem)
        {
            _handle = File.OpenHandle(path.Path, FileMode.Open, FileAccess.Read, Share, FileOptions.RandomAccess);
            Length = RandomAccess.GetLength(_handle);
        }
        else
        {
            _stream = fs.File.Open(path.Path, FileMode.Open, FileAccess.Read, Share);
            Length = _stream.Length;
        }
    }

    public int Read(long offset, Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (offset >= Length || buffer.Length == 0) return 0;
        if (_handle != null) return RandomAccess.Read(_handle, buffer, offset);
        lock (_streamLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _stream!.Position = offset;
            return _stream.Read(buffer);
        }
    }

    /// <summary>
    /// A stream over the whole file with a position of its own; disposing it leaves the file open
    /// </summary>
    public Stream OpenView() => new View(this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle?.Dispose();
        lock (_streamLock)
        {
            _stream?.Dispose();
        }
    }

    private sealed class View : Stream
    {
        private readonly KeptOpenArchiveFile _file;
        private long _position;

        public View(KeptOpenArchiveFile file)
        {
            _file = file;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _file.Length;

        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (buffer.Length == 0) return 0;
            // Small reads (a BinaryReader walking the entry table, a header) come from a 4 KB window, as a FileStream's
            // buffer would serve them: one positional read per window instead of one per field
            if (buffer.Length < BufferSize)
            {
                if (_position < _bufferStart || _position >= _bufferStart + _bufferLength)
                {
                    _buffer ??= new byte[BufferSize];
                    _bufferStart = _position;
                    _bufferLength = _file.Read(_position, _buffer);
                    if (_bufferLength == 0) return 0;
                }
                var from = (int)(_position - _bufferStart);
                var n = Math.Min(buffer.Length, _bufferLength - from);
                _buffer!.AsSpan(from, n).CopyTo(buffer);
                _position += n;
                return n;
            }
            var read = _file.Read(_position, buffer);
            _position += read;
            return read;
        }

        private const int BufferSize = 4096;
        private byte[]? _buffer;
        private long _bufferStart;
        private int _bufferLength;

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _file.Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>
/// An archive reader whose entry reads all go through one <see cref="KeptOpenArchiveFile"/>
/// </summary>
internal sealed class KeptOpenArchiveReader : IDisposableArchiveReader
{
    private readonly IArchiveReader _reader;
    private readonly KeptOpenArchiveFile _file;

    private KeptOpenArchiveReader(IArchiveReader reader, KeptOpenArchiveFile file)
    {
        _reader = reader;
        _file = file;
    }

    public static IDisposableArchiveReader Create(GameRelease release, FilePath path, IFileSystem? fileSystem)
    {
        var file = new KeptOpenArchiveFile(path, fileSystem);
        try
        {
            // The same choice as ArchiveReaderProvider.Create
            IArchiveReader reader = release switch
            {
                GameRelease.Oblivion or GameRelease.OblivionRE or GameRelease.Fallout3 or GameRelease.FalloutNV
                    or GameRelease.SkyrimLE or GameRelease.SkyrimSE or GameRelease.SkyrimSEGog or GameRelease.SkyrimVR
                    or GameRelease.EnderalLE or GameRelease.EnderalSE or GameRelease.EnderalSEGog
                    => new BsaReader(file.OpenView, path),
                GameRelease.Starfield or GameRelease.Fallout4 or GameRelease.Fallout4VR => new Ba2Reader(file.OpenView),
                _ => throw new NotImplementedException(),
            };
            return new KeptOpenArchiveReader(reader, file);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public bool TryGetFolder(string path, [MaybeNullWhen(false)] out IArchiveFolder folder) => _reader.TryGetFolder(path, out folder);

    public IEnumerable<IArchiveFile> Files => _reader.Files;

    public IEnumerable<string> EnumerateFilePaths() => _reader.EnumerateFilePaths();

    public IEnumerable<string> EnumerateFolderFilePaths(string path) => _reader.EnumerateFolderFilePaths(path);

    public bool TryGetFile(string path, [MaybeNullWhen(false)] out IArchiveFile file) => _reader.TryGetFile(path, out file);

    public bool ContainsFile(string path) => _reader.ContainsFile(path);

    public void Dispose() => _file.Dispose();
}
