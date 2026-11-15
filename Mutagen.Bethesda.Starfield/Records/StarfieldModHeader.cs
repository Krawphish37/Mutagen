using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Internals;
using Noggog;
using System.Diagnostics;
using Mutagen.Bethesda.Plugins;

namespace Mutagen.Bethesda.Starfield;

public partial class StarfieldModHeader : IModHeaderInteriorCellCount
{
    /// <summary>
    /// Reads a plugin's header (its TES4 record) alone, as the full import reads it when it opens the plugin: flags,
    /// HEDR stats, author, description, masters and overridden forms. Only the header's bytes are read; the rest of
    /// the plugin isn't opened (C58/W17).<br/>
    /// The overridden forms (ONAM) are FormIDs, so each master's size must be known, as for opening the plugin: from
    /// <see cref="BinaryReadParameters.MasterFlagsLookup"/> (a master whose size isn't known is an error, C180).
    /// </summary>
    /// <param name="path">The plugin's ModKey and path</param>
    /// <param name="release">The game release to read it as</param>
    /// <param name="param">The reading parameters (file system, file share, master sizes, masters override, encodings)</param>
    /// <returns>The header, as a new object</returns>
    public static StarfieldModHeader CreateFromPath(ModPath path, StarfieldRelease release, BinaryReadParameters? param = null)
    {
        param ??= BinaryReadParameters.Default;
        var gameRelease = release.ToGameRelease();
        var header = ModHeaderFrame.FromPath(path, gameRelease, fileSystem: param.FileSystem, share: param.FileShare);
        var meta = ParsingMeta.FactoryForModHeader(param, gameRelease, path.ModKey, header);
        return CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(header.HeaderAndContentData, meta)));
    }

    [Flags]
    public enum HeaderFlag
    {
        Master = 0x0000_0001,
        /// <summary>
        /// xEdit's "Optimized" (TES4 flag bit 4); no plugin in the Starfield Data folder sets it
        /// </summary>
        Optimized = 0x0000_0010,
        Localized = 0x0000_0080,
        Light = 0x0000_0100,
        Overlay = 0x0000_0200,
        Medium = 0x0000_0400,
        /// <summary>
        /// xEdit's "Blueprint" (TES4 flag bit 11): set by the ship blueprint plugins (BlueprintShips-Starfield.esm and
        /// the other BlueprintShips-*.esm)
        /// </summary>
        Blueprint = 0x0000_0800,
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    int IModHeaderCommon.RawFlags
    {
        get => (int)this.Flags;
        set => this.Flags = (HeaderFlag)value;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    uint IModHeaderCommon.NumRecords
    {
        get => this.Stats.NumRecords;
        set => this.Stats.NumRecords = value;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    uint IModHeaderCommon.NextFormID
    {
        get => this.Stats.NextFormID;
        set => this.Stats.NextFormID = value;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    float IModHeaderCommon.HeaderVersion
    {
        get => this.Stats.Version;
        set => this.Stats.Version = value;
    }

    IExtendedList<MasterReference> IModHeaderCommon.MasterReferences => this.MasterReferences;

    public void SetOverriddenForms(IEnumerable<FormKey>? formKeys)
    {
        if (formKeys == null)
        {
            this.OverriddenForms = null;
        }
        else
        {
            this.OverriddenForms ??= new();
            this.OverriddenForms.SetTo(formKeys.Select(f => f.ToLink<IStarfieldMajorRecordGetter>()));
        }
    }

    void IModHeaderInteriorCellCount.SyncInteriorCellCount(IModGetter mod)
    {
        if (mod is not IStarfieldModGetter starfieldMod) return;
        // Every cell in the top level CELL group counts, overrides and deleted cells included
        var count = starfieldMod.Cells.Records
            .SelectMany(block => block.SubBlocks)
            .Sum(subBlock => subBlock.Cells.Count);
        // Leave the subrecord out if there are no interior cells and it was not there to begin with
        if (count == 0 && this.InteriorCellCount == null) return;
        this.InteriorCellCount = count;
    }
}

public partial interface IStarfieldModHeader : IModHeaderCommon
{
}

partial class StarfieldModHeaderBinaryCreateTranslation
{
    public static partial void FillBinaryMasterReferencesCustom(MutagenFrame frame, IStarfieldModHeader item, PreviousParse lastParsed)
    {
        item.MasterReferences.SetTo(
            ListBinaryTranslation<MasterReference>.Instance.Parse(
                reader: frame.SpawnAll(),
                triggeringRecord: RecordTypes.MAST,
                transl: MasterReference.TryCreateFromBinary));
    }
}

partial class StarfieldModHeaderBinaryWriteTranslation
{
    public static partial void WriteBinaryMasterReferencesCustom(MutagenWriter writer, IStarfieldModHeaderGetter item)
    {
        ListBinaryTranslation<IMasterReferenceGetter>.Instance.Write(
            writer: writer,
            items: item.MasterReferences,
            transl: (MutagenWriter subWriter, IMasterReferenceGetter subItem, TypedWriteParams conv) =>
            {
                var Item = subItem;
                ((MasterReferenceBinaryWriteTranslation)((IBinaryItem)Item).BinaryWriteTranslator).Write(
                    item: Item,
                    writer: subWriter,
                    translationParams: conv);
            });
    }
}