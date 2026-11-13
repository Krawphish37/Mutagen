using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Strings;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Binary.Parameters;

/// <summary>
/// Parameter object for customizing binary import job instructions
/// </summary>
public record BinaryReadParameters
{
    public static BinaryReadParameters Default => new();
    
    public StringsReadParameters? StringsParam { get; init; }
    
    /// <summary>
    /// Required for games with Separated Load Order lists per master type
    /// </summary>
    public IReadOnlyCache<IModMasterStyledGetter, ModKey>? MasterFlagsLookup { get; init; }

    /// <summary>
    /// Whether to use multithreading when possible
    /// </summary>
    public bool Parallel { get; init; } = true;
    
    /// <summary>
    /// Whether to treat unknown records as errors and throw an exception
    /// </summary>
    public bool ThrowOnUnknownSubrecord { get; init; } = false;

    /// <summary>
    /// Full import only: keep each compressed record's zlib stream as read, so an unchanged record is written back with
    /// the same bytes even where Mutagen's compressor would encode it differently (games that keep their file layout:
    /// Starfield; other games ignore it). On by default, so a full import writes the same file as an overlay does:
    /// plugins compressed by another encoder (the CK, xEdit) otherwise come back with the same data in other bytes
    /// (e.g. the NPC_ of darkstar.esm and of 8 DarkStar patches). Costs about the size of the compressed records
    /// (1.2 GB for Starfield.esm); set false to save that. An overlay always keeps them, at no cost (it reads them from
    /// memory it holds anyway).
    /// </summary>
    public bool KeepCompressedRecords { get; init; } = true;
    
    /// <summary>
    /// FileSystem to read from
    /// </summary>
    public IFileSystem? FileSystem { get; init; }

    /// <summary>
    /// Optional LinkCache for cross-mod record type resolution.
    /// Required when reading records that reference records from other mods and need type information about those records.
    /// </summary>
    public ILinkCache? LinkCache { get; init; }

    /// <summary>
    /// Optional master reference overrides to use instead of reading from the file header.
    /// Used for split mod imports where split file ModKeys are remapped to the base ModKey.
    /// </summary>
    public IReadOnlyMasterReferenceCollection? MasterOverrides { get; init; }
}