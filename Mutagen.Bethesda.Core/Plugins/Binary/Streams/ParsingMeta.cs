using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records.Internals;
using Mutagen.Bethesda.Plugins.Utility;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Strings.DI;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Binary.Streams;

/// <summary>
/// Class containing all the extra meta bits for parsing
/// </summary>
public sealed class ParsingMeta
{
    /// <summary>
    /// Game constants meta object to reference for header length measurements
    /// </summary>
    public GameConstants Constants { get; }

    /// <summary>
    /// Masters to reference while reading
    /// </summary>
    public IReadOnlySeparatedMasterPackage MasterReferences { get; set; }

    /// <summary>
    /// Optional RecordInfoCache to reference while reading
    /// </summary>
    public RecordTypeInfoCacheReader? RecordInfoCache { get; set; }

    /// <summary>
    /// Optional LinkCache for cross-mod record type resolution
    /// </summary>
    public ILinkCache? LinkCache { get; set; }

    /// <summary>
    /// Optional strings lookup to reference while reading
    /// </summary>
    public IStringsFolderLookup? StringsLookup { get; set; }

    /// <summary>
    /// Whether to do parallel work when possible
    /// </summary>
    public bool Parallel { get; set; }

    /// <summary>
    /// Tracker of whether within worldspace data section
    /// </summary>
    public bool InWorldspace { get; set; }

    /// <summary>
    /// Tracker of current major record version
    /// </summary>
    public ushort? FormVersion { get; set; }

    /// <summary>
    /// MajorRecordReader's ChildRecordRead.Headers (C58/W9): every major record after the first one read is read from
    /// its header alone
    /// </summary>
    internal bool ChildRecordsHeadersOnly { get; set; }

    /// <summary>
    /// With <see cref="ChildRecordsHeadersOnly"/>: whether the first record (the parent) has been started
    /// </summary>
    internal bool HeadersOnlyParentRead { get; set; }

    /// <summary>
    /// Mod header HEDR version
    /// </summary>
    public float? ModHeaderVersion { get; set; }

    /// <summary>
    /// ModKey of the mod being parsed
    /// </summary>
    public ModKey ModKey { get; }

    public EncodingBundle Encodings { get; set; } = new(MutagenEncoding._1252, MutagenEncoding._1252);

    public Language TranslatedTargetLanguage { get; set; } = Language.English;

    public bool ThrowOnUnknown { get; set; }

    /// <summary>
    /// Full import: keep each compressed record's stream as read (see BinaryReadParameters.KeepCompressedRecords)
    /// </summary>
    public bool KeepCompressedRecords { get; set; }

    public IFileSystem FileSystem { get; set; } = IFileSystemExt.DefaultFilesystem;

    /// <summary>
    /// How plugin files opened for this read are shared (BinaryReadParameters.FileShare)
    /// </summary>
    public FileShare FileShare { get; set; } = PluginFileAccess.DefaultShare;

    /// <summary>
    /// Records a later record with the same FormKey replaced in their group (shared by the parallel copies)
    /// </summary>
    internal Mutagen.Bethesda.Plugins.Records.DuplicateRecords.Set? Duplicates { get; set; }

    public ParsingMeta(
        GameConstants constants,
        ModKey modKey,
        IReadOnlySeparatedMasterPackage masterReferences)
    {
        Constants = constants;
        ModKey = modKey;
        MasterReferences = masterReferences;
    }

    public static implicit operator GameConstants(ParsingMeta bundle)
    {
        return bundle.Constants;
    }

    /// <summary>
    /// Creates a copy to hand to a parse that runs on another thread.<br/>
    /// Shared services (masters, caches, strings lookup, link cache) are carried over by reference,
    /// while per-record trackers such as <see cref="FormVersion"/> and <see cref="InWorldspace"/>
    /// become independent, so one thread setting or clearing them cannot affect another.
    /// </summary>
    internal ParsingMeta CloneForParallelParse()
    {
        return (ParsingMeta)MemberwiseClone();
    }

    public void ReportIssue(RecordType? recordType, string note)
    {
        // Nothing for now.  Need to implement
    }

    private void Absorb(StringsReadParameters? stringsReadParameters)
    {
        if (stringsReadParameters == null) return;
        if (stringsReadParameters.TargetLanguage != null)
        {
            TranslatedTargetLanguage = stringsReadParameters.TargetLanguage.Value;
        }

        if (stringsReadParameters.NonLocalizedEncodingOverride == null)
        {
            // Text stored in a plugin without strings files is one text, whatever language the strings files are read
            // in: it is read with the encoding writing uses for it (the game's default), so it reads the same in every
            // target language and writes back as it was read. Picking it by the target language read code page 1252
            // text (e.g. 0x92 in "Samson’s Art", economy.esm) as U+FFFD in every language but English, and wrote it
            // back as '?'. A caller's own encoding provider still chooses by the target language.
            Encodings = Encodings with
            {
                NonLocalized = stringsReadParameters.EncodingProvider is { } encodingProv
                    ? encodingProv.GetEncoding(Constants.Release, TranslatedTargetLanguage)
                    : Constants.Encodings.NonLocalized
            };
        }
        else
        {
            Encodings = Encodings with
            {
                NonLocalized = stringsReadParameters.NonLocalizedEncodingOverride
            };
        }

        if (stringsReadParameters.NonTranslatedEncodingOverride != null)
        {
            Encodings = Encodings with
            {
                NonTranslated = stringsReadParameters.NonTranslatedEncodingOverride
            };
        }
    }

    private void Absorb(BinaryReadParameters? readParameters)
    {
        if (readParameters == null) return;
        if (Constants.UsesStrings)
        {
            Absorb(readParameters.StringsParam);
        }
        ThrowOnUnknown = readParameters.ThrowOnUnknownSubrecord;
        KeepCompressedRecords = readParameters.KeepCompressedRecords;
        Parallel = readParameters.Parallel;
        FileSystem = readParameters.FileSystem.GetOrDefault();
        FileShare = readParameters.FileShare;
        LinkCache = readParameters.LinkCache;
        DuplicateFormKeys = readParameters.DuplicateFormKeys;
    }

    /// <summary>
    /// What reading does with records that share a FormKey (BinaryReadParameters.DuplicateFormKeys, C204)
    /// </summary>
    internal Mutagen.Bethesda.Plugins.Binary.Parameters.DuplicateFormKeyOption DuplicateFormKeys { get; set; }

    /// <summary>
    /// Starfield, DuplicateFormKeyOption.Refuse: refuses the plugin when it is opened if its records share a FormKey
    /// </summary>
    private static void CheckDuplicates(BinaryReadParameters param, GameRelease release, ModKey modKey,
        IReadOnlySeparatedMasterPackage masters, Func<Stream> openFile, bool disposeStream)
    {
        var constants = GameConstants.Get(release);
        if (param.DuplicateFormKeys != Mutagen.Bethesda.Plugins.Binary.Parameters.DuplicateFormKeyOption.Refuse
            || !constants.SeparateMasterLoadOrders)
        {
            return;
        }
        var stream = openFile();
        try
        {
            Mutagen.Bethesda.Plugins.Records.DuplicateFormKeyScan.ThrowIfAny(stream, constants, masters, modKey);
        }
        finally
        {
            if (disposeStream) stream.Dispose();
        }
    }

    private void ReadModHeaderVersion(ModHeaderFrame header)
    {
        foreach (var sub in header)
        {
            if (sub.RecordType == RecordTypes.HEDR)
            {
                ModHeaderVersion = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(sub.Content.Slice(0, 4));
                return;
            }
        }
    }

    public static ParsingMeta Factory(
        BinaryReadParameters param,
        GameRelease release,
        ModPath modPath)
    {
        var header = ModHeaderFrame.FromPath(modPath, release, fileSystem: param.FileSystem, share: param.FileShare);
        var rawMasters = param.MasterOverrides
            ?? MasterReferenceCollection.FromModHeader(modPath.ModKey, header);
        // Decided from the file: how the mod stores its own FormIDs (OwnFormIdStyle)
        var masters = SeparatedMasterPackage.FactoryDetecting(release, modPath.ModKey, header.MasterStyle, rawMasters,
            param.MasterFlagsLookup, () => PluginFileAccess.OpenRead(param.FileSystem, modPath.Path, param.FileShare));
        if (param.DuplicateFormKeys == Mutagen.Bethesda.Plugins.Binary.Parameters.DuplicateFormKeyOption.Refuse
            && GameConstants.Get(release).SeparateMasterLoadOrders)
        {
            Mutagen.Bethesda.Plugins.Records.DuplicateFormKeyScan.ThrowIfAny(modPath.Path, param.FileSystem.GetOrDefault(),
                () => PluginFileAccess.OpenRead(param.FileSystem, modPath.Path, param.FileShare),
                GameConstants.Get(release), masters, modPath.ModKey);
        }
        var meta = new ParsingMeta(GameConstants.Get(release), modPath.ModKey, masters);
        meta.ReadModHeaderVersion(header);
        meta.Absorb(param);
        return meta;
    }

    public static ParsingMeta Factory(
        BinaryReadParameters param,
        GameRelease release,
        ModKey modKey,
        Stream stream)
    {
        var header = ModHeaderFrame.FromStream(stream, modKey, release);
        var rawMasters = param.MasterOverrides
            ?? MasterReferenceCollection.FromModHeader(modKey, header);
        stream.Position = 0;
        // Decided from the file: how the mod stores its own FormIDs (OwnFormIdStyle)
        var masters = SeparatedMasterPackage.FactoryDetecting(release, modKey, header.MasterStyle, rawMasters,
            param.MasterFlagsLookup, () => stream, disposeStream: false);
        CheckDuplicates(param, release, modKey, masters, () => stream, disposeStream: false);
        var meta = new ParsingMeta(GameConstants.Get(release), modKey, masters);
        meta.ReadModHeaderVersion(header);
        meta.Absorb(param);
        return meta;
    }

    /// <summary>
    /// The reading state for a plugin's header (TES4) alone (C58/W17): its masters as <see cref="Factory(BinaryReadParameters, GameRelease, ModPath)"/>
    /// takes them, without the checks that walk the rest of the file (how the mod stores its own FormIDs, duplicate
    /// FormKeys), which the header doesn't need. A master whose size isn't known is an error, as when the plugin is
    /// opened (C180).
    /// </summary>
    internal static ParsingMeta FactoryForModHeader(
        BinaryReadParameters param,
        GameRelease release,
        ModKey modKey,
        ModHeaderFrame header)
    {
        var rawMasters = param.MasterOverrides
            ?? MasterReferenceCollection.FromModHeader(modKey, header);
        var masters = SeparatedMasterPackage.Factory(release, modKey, header.MasterStyle, rawMasters, param.MasterFlagsLookup);
        var meta = new ParsingMeta(GameConstants.Get(release), modKey, masters);
        meta.ReadModHeaderVersion(header);
        meta.Absorb(param);
        return meta;
    }

    public static ParsingMeta FactoryNoMasters(
        BinaryReadParameters param,
        GameRelease release,
        ModKey modKey)
    {
        var meta = new ParsingMeta(GameConstants.Get(release), modKey, null!);
        meta.Absorb(param);
        return meta;
    }
}