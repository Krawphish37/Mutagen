using ICSharpCode.SharpZipLib.Zip.Compression;
using K4os.Compression.LZ4;
using Noggog;
using Noggog.Streams;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Text;

namespace Mutagen.Bethesda.Archives.Ba2;

class Ba2Reader : IArchiveReader
{
    private readonly ulong _nameTableOffset;
    private readonly uint _numFiles;
    private readonly Ba2EntryType _entryType;
    private readonly long _entriesOffset;
    private readonly Lazy<List<IArchiveFile>> _files;
    private int _nameTableScanned;
    internal Func<Stream> _streamFactory;
    public bool UseATIFourCC { get; set; } = false;

    public bool HasNameTable => _nameTableOffset > 0;
    
    public uint Version { get; }

    /// <summary>
    /// Starfield version 3 archives name their chunk compression: 0 is zlib, 3 is LZ4 (raw blocks)
    /// </summary>
    internal uint Compression { get; }

    internal const uint Lz4Compression = 3;

    /// <summary>
    /// How this archive's compressed files and chunks are compressed: LZ4 blocks when version 3 names compression 3,
    /// zlib otherwise (as <see cref="Decompress"/> and the entries' streams read them) (C42)
    /// </summary>
    internal ArchiveCompression CompressionKind =>
        Compression == Lz4Compression ? ArchiveCompression.Lz4Block : ArchiveCompression.Zlib;

    /// <summary>
    /// Starfield's archives (versions 2 and 3) keep Fallout 4's file and texture records, with a longer header
    /// </summary>
    internal static bool IsStarfieldVersion(uint version) => version is 2 or 3;

    internal static void Decompress(Ba2Reader archive, byte[] compressed, byte[] full)
    {
        if (archive.Compression == Lz4Compression)
        {
            var written = LZ4Codec.Decode(compressed, full);
            if (written != full.Length)
            {
                throw new InvalidDataException($"LZ4 chunk unpacked to {written} bytes, expected {full.Length}");
            }
            return;
        }
        var inflater = new Inflater();
        inflater.SetInput(compressed);
        inflater.Inflate(full);
    }

    public Ba2Reader(FilePath filename, IFileSystem? fileSystem = null)
        : this(() => fileSystem.GetOrDefault().File.Open(filename.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
    }

    public Ba2Reader(Func<Stream> streamGetter)
    {
        _streamFactory = streamGetter;
        using var reader = new BinaryReader(_streamFactory(), Encoding.UTF8);

        var headerMagic = Encoding.ASCII.GetString(reader.ReadBytes(4));

        if (headerMagic != "BTDX")
            throw new InvalidDataException($"Unknown header type: {headerMagic}");

        Version = reader.ReadUInt32();

        string fourcc = Encoding.ASCII.GetString(reader.ReadBytes(4));

        if (!Enum.TryParse(fourcc, out _entryType))
        {
            throw new InvalidDataException($"Can't parse entry types of {fourcc}");
        }

        _numFiles = reader.ReadUInt32();
        _nameTableOffset = reader.ReadUInt64();

        if (IsStarfieldVersion(Version))
        {
            // Starfield: two more uints (1 and 0 in every archive seen), and in version 3 the compression method
            reader.ReadUInt32();
            reader.ReadUInt32();
            if (Version == 3)
            {
                Compression = reader.ReadUInt32();
            }
        }
        else if (Version > 1 && Version <= 7)
        {
            var unknown = reader.ReadUInt32();
        }

        _entriesOffset = reader.BaseStream.Position;
        _files = new Lazy<List<IArchiveFile>>(
            isThreadSafe: true,
            valueFactory: LoadFiles);
        _nameTable = new Lazy<NameTable?>(
            isThreadSafe: true,
            valueFactory: ReadNameTable);
    }

    private List<IArchiveFile> LoadFiles()
    {
        using var reader = new BinaryReader(_streamFactory(), Encoding.UTF8);
        reader.BaseStream.Seek(_entriesOffset, SeekOrigin.Begin);

        var files = new List<IArchiveFile>();
        for (var idx = 0; idx < _numFiles; idx += 1)
        {
            switch (_entryType)
            {
                case Ba2EntryType.GNRL:
                    files.Add(new BA2FileEntry(this, Version, idx, reader));
                    break;
                case Ba2EntryType.DX10:
                    files.Add(new BA2DX10Entry(this, Version, idx, reader));
                    break;
                case Ba2EntryType.GNMF:
                    break;

            }
        }

        if (HasNameTable)
        {
            reader.BaseStream.Seek((long)_nameTableOffset, SeekOrigin.Begin);
            foreach (var file in files)
            {
                switch (file)
                {
                    case BA2FileEntry ba2:
                        ba2.Path = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt16()));
                        break;
                    case BA2DX10Entry dx:
                        dx.Path = Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt16()));
                        break;
                    default:
                        throw new NotImplementedException();
                }

            }
        }
        return files;
    }

    public IEnumerable<IArchiveFile> Files => _files.Value;

    /// <summary>
    /// The name table: every file's path, as UTF-8 bytes, in file record order (C201)
    /// </summary>
    private sealed record NameTable(byte[] Bytes, int[] Starts, int[] Lengths);

    private readonly Lazy<NameTable?> _nameTable;
    private long _gnrlRecordLength = -1;

    /// <summary>
    /// Reads the name table in one read from its offset to the end of the file. Null when the archive has none, its
    /// files aren't the ones the name table lists one by one (GNMF), or the table doesn't add up: then
    /// <see cref="Files"/> decides.
    /// </summary>
    private NameTable? ReadNameTable()
    {
        if (!HasNameTable || _entryType == Ba2EntryType.GNMF) return null;
        using var stream = _streamFactory();
        long length = stream.Length - (long)_nameTableOffset;
        if (length < 2L * _numFiles || length > int.MaxValue) return null;
        var bytes = new byte[length];
        stream.Position = (long)_nameTableOffset;
        stream.ReadExactly(bytes);
        var starts = new int[_numFiles];
        var lengths = new int[_numFiles];
        int pos = 0;
        for (int i = 0; i < _numFiles; i++)
        {
            if (pos + 2 > bytes.Length) return null;
            int count = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(pos));
            if (count < 0 || pos + 2 + count > bytes.Length) return null;
            starts[i] = pos + 2;
            lengths[i] = count;
            pos += 2 + count;
        }
        return new NameTable(bytes, starts, lengths);
    }

    /// <summary>
    /// Every file's path from the name table alone (no file record is read), in the order and spelling of
    /// <see cref="Files"/>
    /// </summary>
    public IEnumerable<string> EnumerateFilePaths()
    {
        if (_files.IsValueCreated || _nameTable.Value is not { } table)
        {
            return Files.Select(f => f.Path);
        }
        return Enumerate(table);

        static IEnumerable<string> Enumerate(NameTable table)
        {
            for (int i = 0; i < table.Starts.Length; i++)
            {
                yield return Encoding.UTF8.GetString(table.Bytes, table.Starts[i], table.Lengths[i]);
            }
        }
    }

    /// <summary>
    /// The paths <see cref="TryGetFolder"/> gives for a folder (every file whose path starts with it, ignoring case), from
    /// the name table alone (C58/W10)
    /// </summary>
    public IEnumerable<string> EnumerateFolderFilePaths(string path)
    {
        return EnumerateFilePaths().Where(p => p.StartsWith(path, StringComparison.OrdinalIgnoreCase));
    }

    private int IndexOf(NameTable table, string path)
    {
        var query = Encoding.UTF8.GetBytes(path);
        for (int i = 0; i < table.Starts.Length; i++)
        {
            if (table.Lengths[i] != query.Length) continue;
            if (ArchivePaths.AreSame(table.Bytes.AsSpan(table.Starts[i], table.Lengths[i]), query)) return i;
        }
        // A path with more than ASCII: compare as Files spells it, decoded. A name that isn't valid UTF-8 (e.g. a
        // Windows-1252 "xi bo\xF6tis.dds", ProjectRework-Akila - Textures.ba2) decodes to U+FFFD, so bytes never match.
        if (path.Any(c => c >= 128))
        {
            for (int i = 0; i < table.Starts.Length; i++)
            {
                var name = table.Bytes.AsSpan(table.Starts[i], table.Lengths[i]);
                if (!name.ContainsAnyExceptInRange((byte)0, (byte)127)) continue;
                if (ArchivePaths.AreSame(Encoding.UTF8.GetString(name), path)) return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Whether the archive holds a file with this path, from the name table alone
    /// </summary>
    public bool ContainsFile(string path)
    {
        if (_nameTable.Value is not { } table)
        {
            return Files.Any(f => ArchivePaths.AreSame(f.Path, path));
        }
        return IndexOf(table, path) >= 0;
    }

    /// <summary>
    /// One file by its path: the name table finds it, and only its file record is read
    /// </summary>
    public bool TryGetFile(string path, [MaybeNullWhen(false)] out IArchiveFile file)
    {
        if (_files.IsValueCreated || _nameTable.Value is not { } table)
        {
            file = Files.FirstOrDefault(f => ArchivePaths.AreSame(f.Path, path));
            return file != null;
        }
        var index = IndexOf(table, path);
        if (index < 0)
        {
            file = default;
            return false;
        }
        file = ReadFile(index, Encoding.UTF8.GetString(table.Bytes, table.Starts[index], table.Lengths[index]));
        return true;
    }

    /// <summary>
    /// Builds the file of one file record, as <see cref="LoadFiles"/> builds it
    /// </summary>
    private IArchiveFile ReadFile(int index, string path)
    {
        using var reader = new BinaryReader(_streamFactory(), Encoding.UTF8);
        if (_entryType == Ba2EntryType.GNRL)
        {
            // Every general file record has the same length: measure it on the first
            if (Interlocked.Read(ref _gnrlRecordLength) < 0)
            {
                reader.BaseStream.Seek(_entriesOffset, SeekOrigin.Begin);
                _ = new BA2FileEntry(this, Version, 0, reader);
                Interlocked.Exchange(ref _gnrlRecordLength, reader.BaseStream.Position - _entriesOffset);
            }
            reader.BaseStream.Seek(_entriesOffset + index * Interlocked.Read(ref _gnrlRecordLength), SeekOrigin.Begin);
            return new BA2FileEntry(this, Version, index, reader) { Path = path };
        }
        // Texture records have their chunks inline, so the ones before it are read to find it
        reader.BaseStream.Seek(_entriesOffset, SeekOrigin.Begin);
        for (int i = 0; i < index; i++)
        {
            _ = new BA2DX10Entry(this, Version, i, reader);
        }
        return new BA2DX10Entry(this, Version, index, reader) { Path = path };
    }

    public bool TryGetFolder(string path, [MaybeNullWhen(false)] out IArchiveFolder folder)
    {
        // Only the first lookup on a reader scans the name table.  Later ones parse the records once
        // and filter in memory, so a reader that is kept and reused never rescans.
        if (!_files.IsValueCreated
            && Interlocked.Exchange(ref _nameTableScanned, 1) == 0
            && !NameTableMightContain(path))
        {
            folder = new Ba2FolderWrapper(path, Array.Empty<IArchiveFile>());
            return false;
        }
        folder = new Ba2FolderWrapper(
            path,
            Files.Where(f => f.Path.ToString().StartsWith(path, StringComparison.OrdinalIgnoreCase)));
        return folder.Files.Count > 0;
    }

    /// <summary>
    /// Scans only the name table for a file path starting with the given prefix, without
    /// constructing any file entries.  Lets a lookup skip archives that cannot contain the folder
    /// (e.g. the texture archives when looking for strings) without parsing all of their records.
    /// </summary>
    /// <returns>False only if no file path starts with the prefix.  True if one does, or if the
    /// name table cannot be scanned, in which case the full parse decides.</returns>
    private bool NameTableMightContain(string prefix)
    {
        if (!HasNameTable) return true;
        try
        {
            using var reader = new BinaryReader(_streamFactory(), Encoding.UTF8);
            reader.BaseStream.Seek((long)_nameTableOffset, SeekOrigin.Begin);
            var bytes = new byte[256];
            var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
            for (var idx = 0; idx < _numFiles; idx += 1)
            {
                var length = reader.ReadInt16();
                if (length < 0) return true;
                if (length > bytes.Length)
                {
                    bytes = new byte[length];
                    chars = new char[Encoding.UTF8.GetMaxCharCount(length)];
                }
                reader.BaseStream.ReadExactly(bytes, 0, length);
                var charCount = Encoding.UTF8.GetChars(bytes, 0, length, chars, 0);
                if (chars.AsSpan(0, charCount).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        catch (EndOfStreamException)
        {
            return true;
        }
    }
}

class BA2DX10Entry : IArchiveFile
{
    internal uint _nameHash;
    internal string _extension;
    internal uint _dirHash;
    internal byte _unk8;
    internal byte _numChunks;
    internal ushort _chunkHdrLen;
    internal ushort _height;
    internal ushort _width;
    internal byte _numMips;
    internal byte _format;
    internal ushort _unk16;
    internal List<BA2TextureChunk> _chunks;
    private Ba2Reader _bsa;
    internal int _index;

    public BA2DX10Entry(Ba2Reader ba2Reader, uint version, int idx, BinaryReader reader)
    {
        _bsa = ba2Reader;
        _nameHash = reader.ReadUInt32();
        Path = _nameHash.ToString("X");
        var starfield = Ba2Reader.IsStarfieldVersion(version);
        if (version > 1 && !starfield)
        {
            var unknown = reader.ReadUInt32();
        }
        _extension = Encoding.UTF8.GetString(reader.ReadBytes(4));
        _dirHash = reader.ReadUInt32();
        _unk8 = reader.ReadByte();
        _numChunks = reader.ReadByte();
        _chunkHdrLen = reader.ReadUInt16();
        _height = reader.ReadUInt16();
        _width = reader.ReadUInt16();
        _numMips = reader.ReadByte();
        _format = reader.ReadByte();
        _unk16 = reader.ReadUInt16();
        _index = idx;
        if (version > 1 && !starfield)
        {
            var unknown = reader.ReadUInt32();
        }

        _chunks = Enumerable.Range(0, _numChunks)
            .Select(_ => new BA2TextureChunk(reader, version))
            .ToList();
    }

    public string Path { get; internal set; }

    public uint Size => (uint)_chunks.Sum(f => f._fullSz) + HeaderSize + sizeof(uint);

    public uint HeaderSize => DDS.HeaderSizeForFormat((DXGI_FORMAT)_format);

    /// <summary>
    /// The texture's chunks as the archive stores them, read as <see cref="CopyDataTo"/> reads them (a packed size of 0
    /// is a chunk stored as is); its DDS magic and header are the reader's own (C42)
    /// </summary>
    public ArchiveFileStorage GetStorage()
    {
        var kind = _bsa.CompressionKind;
        var chunks = new ArchiveFileChunk[_chunks.Count];
        long stored = 0;
        var anyCompressed = false;
        for (int i = 0; i < chunks.Length; i++)
        {
            var chunk = _chunks[i];
            var isCompressed = chunk._packSz != 0;
            anyCompressed |= isCompressed;
            chunks[i] = new ArchiveFileChunk(
                Offset: checked((long)chunk._offset),
                StoredSize: isCompressed ? chunk._packSz : chunk._fullSz,
                Size: chunk._fullSz,
                Compression: isCompressed ? kind : ArchiveCompression.None);
            stored += chunks[i].StoredSize;
        }
        return new ArchiveFileStorage(
            offset: chunks.Length == 0 ? 0 : chunks.Min(c => c.Offset),
            storedSize: stored,
            compression: anyCompressed ? kind : ArchiveCompression.None,
            chunks: chunks,
            synthesizedHeaderLength: HeaderSize + sizeof(uint));
    }

    public Stream AsStream()
    {
        // ToDo
        // Optimize to be more streamy, rather than frontload into memory
        var ret = new byte[Size];
        var memStream = new MemoryStream(ret);
        CopyDataTo(memStream);
        memStream.Position = 0;
        return memStream;
    }

    public void CopyDataTo(Stream output)
    {
        var bw = new BinaryWriter(output);

        WriteHeader(bw);

        using var fs = _bsa._streamFactory();
        using var br = new BinaryReader(fs);
        foreach (var chunk in _chunks)
        {
            var full = new byte[chunk._fullSz];
            var isCompressed = chunk._packSz != 0;

            br.BaseStream.Seek((long)chunk._offset, SeekOrigin.Begin);

            if (!isCompressed)
            {
                br.BaseStream.ReadExactly(full);
            }
            else
            {
                byte[] compressed = new byte[chunk._packSz];
                br.BaseStream.ReadExactly(compressed);
                Ba2Reader.Decompress(_bsa, compressed, full);
            }

            bw.BaseStream.Write(full, 0, full.Length);
        }
    }

    public byte[] GetBytes()
    {
        var ret = new byte[Size];
        var memStream = new MemoryStream(ret);
        CopyDataTo(memStream);
        return ret;
    }

    public ReadOnlySpan<byte> GetSpan()
    {
        return GetBytes();
    }

    public ReadOnlyMemorySlice<byte> GetMemorySlice()
    {
        return GetBytes();
    }

    private void WriteHeader(BinaryWriter bw)
    {
        var ddsHeader = new DDS_HEADER();

        ddsHeader.dwSize = ddsHeader.GetSize();
        ddsHeader.dwHeaderFlags = DDS.DDS_HEADER_FLAGS_TEXTURE | DDS.DDS_HEADER_FLAGS_LINEARSIZE | DDS.DDS_HEADER_FLAGS_MIPMAP;
        ddsHeader.dwHeight = _height;
        ddsHeader.dwWidth = _width;
        ddsHeader.dwMipMapCount = _numMips;
        ddsHeader.PixelFormat.dwSize = ddsHeader.PixelFormat.GetSize();
        ddsHeader.dwDepth = 1;
        ddsHeader.dwSurfaceFlags = DDS.DDS_SURFACE_FLAGS_TEXTURE | DDS.DDS_SURFACE_FLAGS_MIPMAP;

        switch ((DXGI_FORMAT)_format)
        {
            case DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM:
                ddsHeader.PixelFormat.dwFlags = DDS.DDS_FOURCC;
                ddsHeader.PixelFormat.dwFourCC = DDS.MAKEFOURCC('D', 'X', 'T', '1');
                ddsHeader.dwPitchOrLinearSize = (uint)(_width * _height / 2); // 4bpp
                break;
            case DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM:
                ddsHeader.PixelFormat.dwFlags = DDS.DDS_FOURCC;
                ddsHeader.PixelFormat.dwFourCC = DDS.MAKEFOURCC('D', 'X', 'T', '3');
                ddsHeader.dwPitchOrLinearSize = (uint)(_width * _height); // 8bpp
                break;
            case DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM:
                ddsHeader.PixelFormat.dwFlags = DDS.DDS_FOURCC;
                ddsHeader.PixelFormat.dwFourCC = DDS.MAKEFOURCC('D', 'X', 'T', '5');
                ddsHeader.dwPitchOrLinearSize = (uint)(_width * _height); // 8bpp
                break;
            case DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM:
                ddsHeader.PixelFormat.dwFlags = DDS.DDS_FOURCC;
                if (_bsa.UseATIFourCC)
                    ddsHeader.PixelFormat.dwFourCC = DDS.MAKEFOURCC('A', 'T', 'I', '2'); // this is more correct but the only thing I have found that supports it is the nvidia photoshop plugin
                else
                    ddsHeader.PixelFormat.dwFourCC = DDS.MAKEFOURCC('B', 'C', '5', 'U');
                ddsHeader.dwPitchOrLinearSize = (uint)(_width * _height); // 8bpp
                break;
            case DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM_SRGB:
                ddsHeader.PixelFormat.dwFlags = DDS.DDS_FOURCC;
                ddsHeader.PixelFormat.dwFourCC = DDS.MAKEFOURCC('D', 'X', '1', '0');
                ddsHeader.dwPitchOrLinearSize = (uint)(_width * _height / 2); // 4bpp
                break;
            case DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM_SRGB:
            case DXGI_FORMAT.DXGI_FORMAT_BC6H_UF16:
            case DXGI_FORMAT.DXGI_FORMAT_BC4_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_BC5_SNORM:
            case DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM_SRGB:
                ddsHeader.PixelFormat.dwFlags = DDS.DDS_FOURCC;
                ddsHeader.PixelFormat.dwFourCC = DDS.MAKEFOURCC('D', 'X', '1', '0');
                ddsHeader.dwPitchOrLinearSize = (uint)(_width * _height); // 8bpp
                break;
            case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
                ddsHeader.PixelFormat.dwFlags = DDS.DDS_RGBA;
                ddsHeader.PixelFormat.dwRGBBitCount = 32;
                ddsHeader.PixelFormat.dwRBitMask = 0x000000FF;
                ddsHeader.PixelFormat.dwGBitMask = 0x0000FF00;
                ddsHeader.PixelFormat.dwBBitMask = 0x00FF0000;
                ddsHeader.PixelFormat.dwABitMask = 0xFF000000;
                ddsHeader.dwPitchOrLinearSize = (uint)(_width * _height * 4); // 32bpp
                break;
            case DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_B8G8R8X8_UNORM:
                ddsHeader.PixelFormat.dwFlags = DDS.DDS_RGBA;
                ddsHeader.PixelFormat.dwRGBBitCount = 32;
                ddsHeader.PixelFormat.dwRBitMask = 0x00FF0000;
                ddsHeader.PixelFormat.dwGBitMask = 0x0000FF00;
                ddsHeader.PixelFormat.dwBBitMask = 0x000000FF;
                ddsHeader.PixelFormat.dwABitMask = 0xFF000000;
                ddsHeader.dwPitchOrLinearSize = (uint)(_width * _height * 4); // 32bpp
                break;
            case DXGI_FORMAT.DXGI_FORMAT_R8_UNORM:
                ddsHeader.PixelFormat.dwFlags = DDS.DDS_RGB;
                ddsHeader.PixelFormat.dwRGBBitCount = 8;
                ddsHeader.PixelFormat.dwRBitMask = 0xFF;
                ddsHeader.dwPitchOrLinearSize = (uint)(_width * _height); // 8bpp
                break;
            default:
                // Any other format (Starfield: R8G8B8A8_SNORM, R16G16B16A16_FLOAT and _UNORM, 78 textures): the DX10
                // extended header names it, as texconv writes it (C202)
                ddsHeader.PixelFormat.dwFlags = DDS.DDS_FOURCC;
                ddsHeader.PixelFormat.dwFourCC = DDS.MAKEFOURCC('D', 'X', '1', '0');
                ddsHeader.dwPitchOrLinearSize = DDS.LinearSize(_format, _width, _height);
                break;
        }

        bw.Write((uint)DDS.DDS_MAGIC);
        ddsHeader.Write(bw);

        if (DDS.UsesDxt10Header((DXGI_FORMAT)_format))
        {
            var dxt10 = new DDS_HEADER_DXT10()
            {
                dxgiFormat = _format,
                resourceDimension = (uint)DXT10_RESOURCE_DIMENSION.DIMENSION_TEXTURE2D,
                miscFlag = 0,
                arraySize = 1,
                miscFlags2 = DDS.DDS_ALPHA_MODE_UNKNOWN
            };
            dxt10.Write(bw);
        }
    }
}

class BA2TextureChunk
{
    internal readonly ulong _offset;
    internal readonly uint _packSz;
    internal readonly uint _fullSz;
    internal readonly ushort _startMip;
    internal readonly ushort _endMip;
    internal readonly uint _align;

    public BA2TextureChunk(BinaryReader rdr, uint version)
    {
        _offset = rdr.ReadUInt64();
        _packSz = rdr.ReadUInt32();
        _fullSz = rdr.ReadUInt32();
        // Starfield (versions 2 and 3) has all of these too: 24 bytes per chunk, ending in 0xBAADF00D
        if (version <= 1 || Ba2Reader.IsStarfieldVersion(version))
        {
            _startMip = rdr.ReadUInt16();
            _endMip = rdr.ReadUInt16();
            _align = rdr.ReadUInt32();
        }
    }
}

class BA2FileEntry : IArchiveFile
{
    internal readonly uint _nameHash;
    internal readonly string _extension;
    internal readonly uint _dirHash;
    internal readonly uint _flags;
    internal readonly ulong _offset;
    internal readonly uint _size;
    internal readonly uint _realSize;
    internal readonly uint _align;
    internal readonly Ba2Reader _bsa;
    internal readonly int _index;
    internal readonly uint _version;

    /// <summary>
    /// The record's packed size as read: the stored size when <see cref="Compressed"/> (C42)
    /// </summary>
    internal readonly uint _packedSize;

    public bool Compressed { get; }

    public BA2FileEntry(Ba2Reader ba2Reader, uint version, int index, BinaryReader reader)
    {
        _index = index;
        _bsa = ba2Reader;
        _version = version;
        _nameHash = reader.ReadUInt32();
        Path = _nameHash.ToString("X");
        var starfield = Ba2Reader.IsStarfieldVersion(version);
        if (version > 1 && version <= 7 && !starfield)
        {
            var unknown = reader.ReadUInt32();
        }

        _extension = Encoding.UTF8.GetString(reader.ReadBytes(4));
        _dirHash = reader.ReadUInt32();
        _flags = reader.ReadUInt32();
        _offset = reader.ReadUInt64();
        if (version > 7)
        {
            _packedSize = reader.ReadUInt32();
        }
        _size = reader.ReadUInt32();
        if (version <= 7)
        {
            _packedSize = _size;
        }
        _realSize = reader.ReadUInt32();
        if (version <= 1 || starfield)
        {
            _align = reader.ReadUInt32();
        }

        if (version < 7)
        {
            Compressed = _size != 0;
        }
        else
        {
            Compressed = _realSize != 0xBAADF00D;
            _realSize = _size;
        }
    }
        
    public string Path { get; internal set; }

    public uint Size => _realSize;

    /// <summary>
    /// The file's data as the archive stores it, from its file record alone: <see cref="Size"/> bytes as is, or its
    /// packed size compressed as the archive says (zlib, or LZ4 blocks in a version 3 archive naming compression 3)
    /// (C42)
    /// </summary>
    public ArchiveFileStorage GetStorage()
    {
        var compression = Compressed ? _bsa.CompressionKind : ArchiveCompression.None;
        var stored = Compressed ? _packedSize : _realSize;
        var offset = checked((long)_offset);
        return new ArchiveFileStorage(
            offset: offset,
            storedSize: stored,
            compression: compression,
            chunks: [new ArchiveFileChunk(offset, stored, _realSize, compression)]);
    }

    public Stream AsStream()
    {
        var fs = _bsa._streamFactory();
        fs.Seek((long)_offset, SeekOrigin.Begin);

        if (!Compressed)
        {
            return new FramedStream(fs, _realSize);
        }
        else if (_bsa.Compression == Ba2Reader.Lz4Compression)
        {
            using (fs)
            {
                var compressed = new byte[_size];
                fs.ReadExactly(compressed);
                var full = new byte[_realSize];
                Ba2Reader.Decompress(_bsa, compressed, full);
                return new MemoryStream(full, writable: false);
            }
        }
        else
        {
            // .NET's zlib (native zlib-ng): its state lives outside the managed heap, where SharpZipLib's
            // InflaterInputStream allocated about 45 KB per entry (a 32 KB window and its buffers), and it inflates
            // faster. Reading the first 2 KB of every mesh of Starfield's two mesh archives took 2.9 s, of which about
            // 1.1 s is opening the archive per entry (C16).
            return new DecompressedEntryStream(
                new ZLibStream(fs, CompressionMode.Decompress, leaveOpen: false),
                _realSize);
        }
    }

    public byte[] GetBytes()
    {
        using var s = AsStream();
        byte[] ret = new byte[s.Remaining()];
        s.ReadExactly(ret);
        return ret;
    }

    public ReadOnlySpan<byte> GetSpan()
    {
        return GetBytes();
    }

    public ReadOnlyMemorySlice<byte> GetMemorySlice()
    {
        return GetBytes();
    }
}