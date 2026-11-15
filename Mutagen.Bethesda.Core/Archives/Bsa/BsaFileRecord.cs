using Noggog;
using System.Buffers.Binary;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using K4os.Compression.LZ4.Streams;
using Noggog.Streams;
using Mutagen.Bethesda.Archives.Exceptions;

namespace Mutagen.Bethesda.Archives.Bsa;

class BsaFileRecord : IArchiveFile
{
    public const int HeaderLength = 0x10;

    private readonly ReadOnlyMemorySlice<byte> _headerData;
    internal readonly int _index;
    internal readonly int _overallIndex;
    internal readonly BsaFileNameBlock? _nameBlock;
    internal readonly Lazy<FileName?> _name;
    internal Lazy<(uint Size, uint OnDisk, uint Original)> _size;

    public ulong Hash => BinaryPrimitives.ReadUInt64LittleEndian(_headerData);
    protected uint RawSize => BinaryPrimitives.ReadUInt32LittleEndian(_headerData.Slice(0x8));
    public uint Offset => BinaryPrimitives.ReadUInt32LittleEndian(_headerData.Slice(0xC));
    public FileName? Name => _name.Value;
    public uint Size => _size.Value.Size;

    public bool FlipCompression => (RawSize & (0x1 << 30)) > 0;

    internal BsaFolderRecord Folder { get; }
    internal BsaReader BSA => Folder.BSA;

    internal BsaFileRecord(
        BsaFolderRecord folderRecord,
        ReadOnlyMemorySlice<byte> data,
        int index,
        int overallIndex,
        BsaFileNameBlock? nameBlock)
    {
        _index = index;
        _overallIndex = overallIndex;
        _headerData = data;
        _nameBlock = nameBlock;
        Folder = folderRecord;
        _name = new Lazy<FileName?>(GetName, LazyThreadSafetyMode.PublicationOnly);

        // Will be replaced if CopyDataTo is called before value is created
        _size = new Lazy<(uint Size, uint OnDisk, uint Original)>(
            mode: LazyThreadSafetyMode.ExecutionAndPublication,
            valueFactory: () =>
            {
                using var rdr = BSA.GetStream();
                rdr.BaseStream.Position = Offset;
                return ReadSize(rdr);
            });
    }

    public string Path
    {
        get
        {
            if (Name == null) return string.Empty;
            return Folder.Path.IsNullOrWhitespace() ? Name.Value.String : System.IO.Path.Combine(Folder.Path, Name.Value.String);
        }
    }

    public bool Compressed
    {
        get
        {
            if (FlipCompression) return !BSA.CompressedByDefault;
            return BSA.CompressedByDefault;
        }
    }

    public Stream AsStream()
    {
        try
        {
            var rdr = BSA.GetStream();
            rdr.BaseStream.Position = Offset;

            (uint Size, uint OnDisk, uint Original) size = ReadSize(rdr);
            if (!_size.IsValueCreated)
            {
                _size = new Lazy<(uint Size, uint OnDisk, uint Original)>(value: size);
            }

            if (BSA.HeaderType == BsaVersionType.SSE)
            {
                if (Compressed && size.Size != size.OnDisk)
                {
                    return new FramedStream(
                        LZ4Stream.Decode(rdr.BaseStream),
                        size.Original);
                }
                else
                {
                    return new FramedStream(rdr.BaseStream, size.OnDisk);
                }
            }
            else
            {
                if (Compressed)
                {
                    return new DecompressedEntryStream(
                        new InflaterInputStream(rdr.BaseStream)
                        {
                            IsStreamOwner = true
                        },
                        size.Original);
                }
                else
                {
                    return new FramedStream(rdr.BaseStream, size.OnDisk);
                }
            }
        }
        catch (Exception e)
        {
            ArchiveException.EnrichAndThrow(
                e,
                BSA.FilePath,
                Folder.Path,
                Path);
            throw;
        }
    }

    /// <summary>
    /// The file's stretch from its record (offset, and size without the compression toggle bit), and its data inside it
    /// as <see cref="AsStream"/> reads it: after the embedded path (when the archive embeds names) and, when
    /// compressed, the 4-byte inflated size. Those few bytes are read once and kept with <see cref="Size"/> (C42).
    /// </summary>
    public ArchiveFileStorage GetStorage()
    {
        try
        {
            var size = _size.Value;
            uint stretch = RawSize;
            if (FlipCompression)
                stretch ^= 0x1u << 30;
            // ReadSize: on disk = the stretch less the embedded path and, when compressed, the inflated size
            var dataOffset = (long)Offset + (stretch - size.OnDisk);
            ArchiveCompression compression;
            uint inflated;
            if (BSA.HeaderType == BsaVersionType.SSE)
            {
                // As AsStream: a compressed file whose inflated size equals its stored size is read as is
                var lz4 = Compressed && size.Size != size.OnDisk;
                compression = lz4 ? ArchiveCompression.Lz4Frame : ArchiveCompression.None;
                inflated = lz4 ? size.Original : size.OnDisk;
            }
            else
            {
                compression = Compressed ? ArchiveCompression.Zlib : ArchiveCompression.None;
                inflated = Compressed ? size.Original : size.OnDisk;
            }
            return new ArchiveFileStorage(
                offset: Offset,
                storedSize: stretch,
                compression: compression,
                chunks: [new ArchiveFileChunk(dataOffset, size.OnDisk, inflated, compression)]);
        }
        catch (Exception e)
        {
            ArchiveException.EnrichAndThrow(
                e,
                BSA.FilePath,
                Folder.Path,
                Path);
            throw;
        }
    }

    private FileName? GetName()
    {
        if (_nameBlock == null) return null;
        var names = _nameBlock.Names.Value;
        return names[_overallIndex].ReadStringTerm(BSA.HeaderType);
    }

    private (uint Size, uint OnDisk, uint Original) ReadSize(BinaryReader rdr)
    {
        uint size = RawSize;
        if (FlipCompression)
            size = size ^ (0x1 << 30);

        if (Compressed)
            size -= 4;

        byte nameBlobOffset;
        if (BSA.HasNameBlobs)
        {
            nameBlobOffset = rdr.ReadByte();
            // Just skip, not using
            rdr.BaseStream.Position += nameBlobOffset;
            // Minus one more for the size of the name blob offset size
            nameBlobOffset++;
        }
        else
        {
            nameBlobOffset = 0;
        }

        uint originalSize;
        if (Compressed)
        {
            originalSize = rdr.ReadUInt32();
        }
        else
        {
            originalSize = 0;
        }

        uint onDiskSize = size - nameBlobOffset;
        if (Compressed)
        {
            return (Size: originalSize, OnDisk: onDiskSize, Original: originalSize);
        }
        else
        {
            return (Size: onDiskSize, OnDisk: onDiskSize, Original: originalSize);
        }
    }

    public byte[] GetBytes()
    {
        try
        {
            using var s = AsStream();
            var remaining = s.Remaining();
            if (remaining > int.MaxValue)
            {
                throw new ArchiveException(
                    $"File claimed it was bigger than what a byte array can hold: {remaining} > {int.MaxValue}",
                    BSA.FilePath,
                    Folder.Path,
                    Path);
            }
            byte[] ret = new byte[remaining];
            s.ReadExactly(ret);
            return ret;
        }
        catch (Exception e)
        {
            ArchiveException.EnrichAndThrow(
                e,
                BSA.FilePath,
                Folder.Path,
                Path);
            throw;
        }
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