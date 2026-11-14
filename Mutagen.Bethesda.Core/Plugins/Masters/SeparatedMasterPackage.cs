using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Masters;

public interface IReadOnlySeparatedMasterPackage
{
    ModKey CurrentMod { get; }
    IReadOnlyMasterReferenceCollection Raw { get; }
    bool TryLookupModKey(ModKey modKey, bool reference, out MasterStyle style, out uint index);
    FormKey GetFormKey(FormID formId, bool reference);
    FormID GetFormID(FormKey formKey);
}

public class SeparatedMasterPackage : IReadOnlySeparatedMasterPackage
{
    internal record MasterStyleIndex(uint Index, MasterStyle Style);

    public IReadOnlyList<ModKey> Full { get; private set; } = null!;
    public IReadOnlyList<ModKey> Medium { get; private set; } = null!;
    public IReadOnlyList<ModKey> Small { get; private set; } = null!;
    public ModKey CurrentMod { get; private set; }
    public IReadOnlyMasterReferenceCollection Raw { get; private set; } = null!;

    /// <summary>
    /// Whether the current mod's own FormIDs are in its own style (true) or a full index (false), when that was decided
    /// from its file (OwnFormIdStyle) rather than by default
    /// </summary>
    internal bool? OwnFormIdsStyled { get; private set; }

    /// <summary>
    /// The current mod's own size (its header flags): a Small or Medium mod can store its own FormIDs in two forms
    /// </summary>
    internal MasterStyle OwnHeaderStyle { get; private set; }

    /// <summary>
    /// Whether the current mod can store its own FormIDs in a form other than the one written (OwnFormIdForms, C163):
    /// only a Small or Medium mod has two forms (its full index and its FE / FD form)
    /// </summary>
    internal bool HasOwnFormVariants => OwnHeaderStyle != MasterStyle.Full;

    /// <summary>
    /// Whether a FormID is the current mod's own FormID in a form of its own other than the one written: its full index
    /// (the number of full masters), or for a Small mod FE + its small index, for a Medium mod FD + its medium index.
    /// A master index past the list is not one of these (it reads as the mod too, decision 6.1, and is written as before).
    /// </summary>
    /// <param name="includePastList">Also a master index past the list (it reads as the mod too; decision 6.1 revised, C189)</param>
    internal bool IsOwnFormVariant(uint raw, bool includePastList = false)
    {
        uint id;
        var top = raw >> 24;
        if (includePastList && raw != 0)
        {
            ExtractFormIdInfo(new FormID(raw), out _, out var pastIndex, out var pastId, out var list);
            if (pastIndex >= list.Count && pastId != 0)
            {
                return GetFormID(new FormKey(CurrentMod, pastId)).Raw != raw;
            }
        }
        if (top == _ownFullIndex)
        {
            id = raw & FormID.FullIdMask;
        }
        else if (OwnHeaderStyle == MasterStyle.Small && top == FormID.SmallMasterMarker
                 && ((raw & FormID.LightMasterIndexMask) >> FormID.LightMasterIndexShift) == _ownSmallIndex)
        {
            id = raw & FormID.SmallIdMask;
        }
        else if (OwnHeaderStyle == MasterStyle.Medium && top == FormID.MediumMasterMarker
                 && ((raw & FormID.MediumMasterIndexMask) >> FormID.MediumMasterIndexShift) == _ownMediumIndex)
        {
            id = raw & FormID.MediumIdMask;
        }
        else
        {
            return false;
        }
        if (id == 0) return false;
        return GetFormID(new FormKey(CurrentMod, id)).Raw != raw;
    }

    /// <summary>
    /// How a FormID is stored (StoredFormIdMixIn, C203): its form by its top byte (FE Small, FD Medium, else Full), or
    /// past the list when its index is past both the masters of that size and the mod's own place in that form
    /// </summary>
    internal Mutagen.Bethesda.Plugins.Records.StoredFormIdForm Classify(uint raw, out bool own)
    {
        ExtractFormIdInfo(new FormID(raw), out var style, out var index, out _, out _);
        uint mastersOfStyle = style switch
        {
            MasterStyle.Small => _ownSmallIndex,
            MasterStyle.Medium => _ownMediumIndex,
            _ => _ownFullIndex,
        };
        own = index >= mastersOfStyle && raw != 0;
        // The mod's own place exists in the full form and in the form of its own size only
        var ownPlace = style == MasterStyle.Full || style == OwnHeaderStyle;
        if (index > mastersOfStyle || (index == mastersOfStyle && !ownPlace))
        {
            return Mutagen.Bethesda.Plugins.Records.StoredFormIdForm.PastTheList;
        }
        return style switch
        {
            MasterStyle.Small => Mutagen.Bethesda.Plugins.Records.StoredFormIdForm.Small,
            MasterStyle.Medium => Mutagen.Bethesda.Plugins.Records.StoredFormIdForm.Medium,
            _ => Mutagen.Bethesda.Plugins.Records.StoredFormIdForm.Full,
        };
    }

    /// <summary>
    /// The FormKey a stored FormID reads as, as one number (DuplicateFormKeyScan, C204): a master's FormID as stored, and
    /// any form of the mod's own (its place in any form, or an index past the list) as 1 &lt;&lt; 32 + its ID
    /// </summary>
    internal ulong DuplicateKey(uint raw)
    {
        ExtractFormIdInfo(new FormID(raw), out var style, out var index, out var id, out _);
        uint mastersOfStyle = style switch
        {
            MasterStyle.Small => _ownSmallIndex,
            MasterStyle.Medium => _ownMediumIndex,
            _ => _ownFullIndex,
        };
        return index < mastersOfStyle ? raw : (1UL << 32) | id;
    }

    // The indices the mod's own FormIDs take in each form: the number of masters of that size
    private uint _ownFullIndex;
    private uint _ownMediumIndex;
    private uint _ownSmallIndex;

    private IReadOnlyDictionary<ModKey, MasterStyleIndex> _lookup = null!;

    internal static readonly IReadOnlySeparatedMasterPackage EmptyNull =
        NotSeparate(new MasterReferenceCollection(ModKey.Null));

    // A Small or Medium mod's own FormIDs are in the form of its size (FE + small index, FD + medium index), as SF1Edit
    // writes new records and every official Creation stores them (decision A3). FormIDs read in another form (its full
    // index, as the Creation Kit writes a small .esm) are kept as stored where unchanged (RawHeaderFormIds,
    // OwnFormIdForms: C147, C163). Only RoundTripMixIn.SetOwnFormIdStyle overrides the form (OwnFormIdStyle.GetExplicit).

    internal SeparatedMasterPackage()
    {
    }

    public static IReadOnlySeparatedMasterPackage Factory(
        GameRelease release,
        ModKey currentModKey,
        MasterStyle style,
        IReadOnlyMasterReferenceCollection masters,
        IReadOnlyCache<IModMasterStyledGetter, ModKey>? masterFlagLookup)
    {
        return Factory(release, currentModKey, style, masters, masterFlagLookup, ownFormIdsStyled: null);
    }

    /// <param name="ownFormIdsStyled">How the mod stores its own FormIDs, if known from its file (OwnFormIdStyle), reported only</param>
    /// <param name="ownFormIdsForm">The form to write them in, when set explicitly (OwnFormIdStyle.GetExplicit): true its
    /// own style, false its full index; null the form of its size</param>
    internal static IReadOnlySeparatedMasterPackage Factory(
        GameRelease release,
        ModKey currentModKey,
        MasterStyle style,
        IReadOnlyMasterReferenceCollection masters,
        IReadOnlyCache<IModMasterStyledGetter, ModKey>? masterFlagLookup,
        bool? ownFormIdsStyled,
        bool? ownFormIdsForm = null)
    {
        var constants = GameConstants.Get(release);
        if (constants.SeparateMasterLoadOrders)
        {
            return SeparatedMasterPackage.Separate(currentModKey, style, masters, masterFlagLookup, ownFormIdsStyled, ownFormIdsForm);
        }
        else
        {
            return SeparatedMasterPackage.NotSeparate(masters);
        }
    }

    public static IReadOnlySeparatedMasterPackage Factory(
        GameRelease release,
        ModPath modPath,
        IReadOnlyCache<IModMasterStyledGetter, ModKey>? masterFlagLookup,
        IFileSystem? fileSystem = null)
    {
        var header = ModHeaderFrame.FromPath(modPath, release, fileSystem: fileSystem);
        var masters = MasterReferenceCollection.FromModHeader(modPath.ModKey, header);
        return FactoryDetecting(release, modPath.ModKey, header.MasterStyle, masters, masterFlagLookup,
            () => PluginFileAccess.OpenRead(fileSystem, modPath.Path));
    }

    /// <summary>
    /// Builds the package for reading a mod from its file, deciding from the file how it stores its own FormIDs
    /// </summary>
    internal static IReadOnlySeparatedMasterPackage FactoryDetecting(
        GameRelease release,
        ModKey currentModKey,
        MasterStyle style,
        IReadOnlyMasterReferenceCollection masters,
        IReadOnlyCache<IModMasterStyledGetter, ModKey>? masterFlagLookup,
        Func<Stream> openFile,
        bool disposeStream = true)
    {
        var package = Factory(release, currentModKey, style, masters, masterFlagLookup);
        if (style == MasterStyle.Full
            || package is not SeparatedMasterPackage separated)
        {
            return package;
        }
        bool? detected;
        var stream = openFile();
        try
        {
            detected = OwnFormIdStyle.Detect(
                stream,
                GameConstants.Get(release),
                style,
                fullMasters: separated.Full.Count(m => m != currentModKey),
                mediumMasters: separated.Medium.Count(m => m != currentModKey),
                smallMasters: separated.Small.Count(m => m != currentModKey));
        }
        finally
        {
            if (disposeStream) stream.Dispose();
        }
        return detected == null ? package : Factory(release, currentModKey, style, masters, masterFlagLookup, detected);
    }

    internal class NotSeparatedMasterPackage : IReadOnlySeparatedMasterPackage
    {
        public ModKey CurrentMod { get; }
        public IReadOnlyMasterReferenceCollection Raw { get; }

        public IReadOnlyList<ModKey> Normal { get; }

        internal IReadOnlyDictionary<ModKey, MasterStyleIndex> _lookup = null!;

        public NotSeparatedMasterPackage(
            ModKey currentMod,
            IReadOnlyMasterReferenceCollection masters,
            IReadOnlyList<ModKey> normal)
        {
            CurrentMod = currentMod;
            Raw = masters;
            Normal = normal;
        }

        public bool TryLookupModKey(ModKey modKey, bool reference, out MasterStyle style, out uint index)
        {
            if (_lookup.TryGetValue(modKey, out var x))
            {
                style = x.Style;
                index = x.Index;
                return true;
            }

            style = default;
            index = default;
            return false;
        }

        public FormID GetFormID(FormKey formKey)
        {
            if (formKey == FormKey.Null) return FormID.Null;

            if (!TryLookupModKey(formKey.ModKey, reference: true, out var style, out var index))
            {
                throw new UnmappableFormIDException(
                    new FormLinkInformation(formKey, typeof(IMajorRecordGetter)),
                    this);
            }

            return FormID.Factory(style, index, formKey.ID);
        }

        public FormKey GetFormKey(FormID formId, bool reference)
        {
            // Only a reference can be fully null with a null ModKey
            if (reference && formId.Raw == 0)
            {
                return FormKey.Null;
            }

            var loadOrder = Normal;
            var modID = formId.FullMasterIndex;

            if (modID >= loadOrder.Count)
            {
                return new FormKey(
                    CurrentMod,
                    formId.FullId);
            }

            var master = loadOrder[checked((int)modID)];
            return new FormKey(
                master,
                formId.FullId);
        }
    }

    internal static IReadOnlySeparatedMasterPackage NotSeparate(IReadOnlyMasterReferenceCollection masters)
    {
        var normal = new List<ModKey>(masters.Masters.Select((x => x.Master)));
        normal.Add(masters.CurrentMod);
        var ret = new NotSeparatedMasterPackage(
            masters.CurrentMod,
            masters,
            normal);
        var lookup = new Dictionary<ModKey, MasterStyleIndex>();
        FillLookup(ret.Normal, lookup, MasterStyle.Full);
        ret._lookup = lookup;
        return ret;
    }

    internal static IReadOnlySeparatedMasterPackage Separate(
        ModKey currentModKey,
        MasterStyle style,
        IReadOnlyMasterReferenceCollection masters,
        IReadOnlyCache<IModMasterStyledGetter, ModKey>? masterFlagLookup,
        bool? ownFormIdsStyled = null,
        bool? ownFormIdsForm = null)
    {
        var normal = new List<ModKey>();
        var medium = new List<ModKey>();
        var small = new List<ModKey>();

        void AddToList(IModMasterStyledGetter mod, ModKey modKey)
        {
            switch (mod.MasterStyle)
            {
                case MasterStyle.Full:
                    AddToListViaStyle(MasterStyle.Full, modKey);
                    break;
                case MasterStyle.Small:
                    AddToListViaStyle(MasterStyle.Small, modKey);
                    break;
                case MasterStyle.Medium:
                    AddToListViaStyle(MasterStyle.Medium, modKey);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        void AddToListViaStyle(MasterStyle style, ModKey modKey)
        {
            switch (style)
            {
                case MasterStyle.Full:
                    normal.Add(modKey);
                    break;
                case MasterStyle.Small:
                    small.Add(modKey);
                    break;
                case MasterStyle.Medium:
                    medium.Add(modKey);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(style), style, null);
            }
        }

        foreach (var master in masters.Masters.Select(x => x.Master))
        {
            if (masterFlagLookup == null)
            {
                throw new MissingModMappingException("Master flag lookup was not provided.");
            }
            if (!masterFlagLookup.TryGetValue(master, out var mod))
            {
                // A Starfield plugin's FormIDs can't be read without every master's style (Full, Medium or Small): it
                // decides which list, and so which index, each master has. The game and xEdit don't load a plugin whose
                // master is missing either, so this stays an error, with what to do about it.
                throw new MissingModException(master,
                    $"{currentModKey} lists {master} as a master, but {master}'s master style (Full, Medium or Small) isn't known: " +
                    "it is in neither the load order nor the master flags lookup given. Its FormIDs can't be read or written " +
                    "without it. Put the master in the load order or data folder, or give its style with WithKnownMasters " +
                    "(a KeyedMasterStyle in BinaryReadParameters/BinaryWriteParameters.MasterFlagsLookup).");
            }

            AddToList(mod, master);
        }

        // Every master has to fit its FormID field.  Full indices end below 0xFD, as 0xFD and 0xFE in the
        // top byte mark Medium and Small FormIDs.  The mod's own index is only checked once it is used
        // (CheckOwnIndexFits on write, GetFormID), as a mod that only overrides records never needs one.
        CheckMasterCount(currentModKey, MasterStyle.Full, normal);
        CheckMasterCount(currentModKey, MasterStyle.Medium, medium);
        CheckMasterCount(currentModKey, MasterStyle.Small, small);

        // The mod's own FormIDs. A Small or Medium mod: the form of its size (FE + small index, FD + medium index), as
        // SF1Edit writes new records and every official Creation stores them (decision A3), for new records and links;
        // FormIDs read in another form keep it (RawHeaderFormIds, OwnFormIdForms: C147, C163). The style read from the file
        // (OwnFormIdStyle) is reported only; RoundTripMixIn.SetOwnFormIdStyle overrides the form when set explicitly. A
        // Full mod: its full index. (No list of official plugins decides it: C181 removed it.)
        if (style != MasterStyle.Full && (ownFormIdsForm ?? true))
        {
            AddToListViaStyle(style, currentModKey);
        }
        else
        {
            normal.Add(currentModKey);
        }

        var ret = new SeparatedMasterPackage()
        {
            Full = normal,
            Medium = medium,
            Small = small,
            Raw = masters,
            CurrentMod = masters.CurrentMod,
            OwnFormIdsStyled = ownFormIdsStyled,
            OwnHeaderStyle = style,
            _ownFullIndex = (uint)normal.Count(m => m != currentModKey),
            _ownMediumIndex = (uint)medium.Count(m => m != currentModKey),
            _ownSmallIndex = (uint)small.Count(m => m != currentModKey),
        };
        var lookup = new Dictionary<ModKey, MasterStyleIndex>();
        FillLookup(ret.Full, lookup, MasterStyle.Full);
        FillLookup(ret.Small, lookup, MasterStyle.Small);
        FillLookup(ret.Medium, lookup, MasterStyle.Medium);
        ret._lookup = lookup;
        return ret;
    }

    /// <summary>
    /// How many indices a FormID of the given style can address
    /// </summary>
    private static uint IndexCapacity(MasterStyle style) => style switch
    {
        // 0x00-0xFC
        MasterStyle.Full => FormID.MediumMasterMarker,
        // 0x00-0xFF
        MasterStyle.Medium => (FormID.MediumMasterIndexMask >> FormID.MediumMasterIndexShift) + 1,
        // 0x000-0xFFF
        MasterStyle.Small => (FormID.LightMasterIndexMask >> FormID.LightMasterIndexShift) + 1,
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, null)
    };

    private static void CheckMasterCount(
        ModKey currentModKey,
        MasterStyle style,
        IReadOnlyList<ModKey> masters)
    {
        var capacity = IndexCapacity(style);
        if (masters.Count <= capacity) return;
        throw new TooManyMastersException(
            currentModKey,
            masters.ToArray(),
            $"{currentModKey} has too many {style} masters on masters list. {masters.Count} {style} masters need indices, but only {capacity} fit in a FormID.");
    }

    private IReadOnlyList<ModKey> ListForStyle(MasterStyle style) => style switch
    {
        MasterStyle.Full => Full,
        MasterStyle.Medium => Medium,
        MasterStyle.Small => Small,
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, null)
    };

    /// <summary>
    /// Throws if the mod's own FormIDs would need an index past what their FormID field can hold.
    /// Only relevant for a mod that has records of its own, or links to them.
    /// </summary>
    internal void CheckOwnIndexFits()
    {
        if (!TryLookupModKey(CurrentMod, reference: true, out var style, out var index)) return;
        if (index < IndexCapacity(style)) return;
        throw OwnIndexDoesNotFit(style, index);
    }

    /// <summary>
    /// Throws if the mod's full index can't hold its own FormIDs (a Small/Medium mod writes an own object ID too big for
    /// its FE / FD form with its full index)
    /// </summary>
    internal void CheckOwnFullIndexFits()
    {
        if (_ownFullIndex >= IndexCapacity(MasterStyle.Full)) throw OwnIndexDoesNotFit(MasterStyle.Full, _ownFullIndex);
    }

    private TooManyMastersException OwnIndexDoesNotFit(MasterStyle style, uint index)
    {
        return new TooManyMastersException(
            CurrentMod,
            ListForStyle(style).Where(x => x != CurrentMod).ToArray(),
            $"{CurrentMod} has too many {style} masters on masters list to also hold its own records. They would need {style} index 0x{index:X}, but only {IndexCapacity(style)} {style} indices fit in a FormID.");
    }

    public static IReadOnlySeparatedMasterPackage FromConstants(GameConstants constants, ModPath path,
        IFileSystem? fileSystem = null)
    {
        if (constants.SeparateMasterLoadOrders)
        {
            throw new ArgumentException(
                $"Cannot make {nameof(SeparatedMasterPackage)} just a path on a game that has separated masters: {constants.SeparateMasterLoadOrders}");
        }

        return NotSeparate(MasterReferenceCollection.FromPath(path, constants.Release, fileSystem: fileSystem));
    }

    internal static void FillLookup(
        IReadOnlyList<ModKey> masters,
        Dictionary<ModKey, MasterStyleIndex> dict,
        MasterStyle style)
    {
        uint index = 0;
        foreach (var modKey in masters)
        {
            dict.Set(modKey, new(index, style));
            index++;
        }
    }

    public bool TryLookupModKey(ModKey modKey, bool reference, out MasterStyle style, out uint index)
    {
        if (_lookup.TryGetValue(modKey, out var x))
        {
            style = x.Style;
            index = x.Index;
            return true;
        }

        style = default;
        index = default;
        return false;
    }

    private void ExtractFormIdInfo(
        FormID formId,
        out MasterStyle style,
        out uint index,
        out uint id,
        out IReadOnlyList<ModKey> loadOrder)
    {
        var fullMasterIndex = formId.FullMasterIndex;
        switch (fullMasterIndex)
        {
            case FormID.SmallMasterMarker:
            {
                index = formId.LightMasterIndex;
                id = formId.LightId;
                style = MasterStyle.Small;
                loadOrder = Small;
                break;
            }
            case FormID.MediumMasterMarker:
            {
                index = formId.MediumMasterIndex;
                id = formId.MediumId;
                style = MasterStyle.Medium;
                loadOrder = Medium;
                break;
            }
            default:
            {
                index = formId.FullMasterIndex;
                id = formId.FullId;
                style = MasterStyle.Full;
                loadOrder = Full;
                break;
            }
        }
    }

    public FormID GetFormID(FormKey formKey)
    {
        if (formKey == FormKey.Null) return FormID.Null;

        if (!TryLookupModKey(formKey.ModKey, reference: true, out var style, out var index))
        {
            throw new UnmappableFormIDException(
                new FormLinkInformation(formKey, typeof(IMajorRecordGetter)),
                this);
        }

        if (index >= IndexCapacity(style))
        {
            // Separate already checked the masters, so only the mod's own index can get here
            throw OwnIndexDoesNotFit(style, index);
        }

        if (style != MasterStyle.Full && formKey.ModKey == CurrentMod && formKey.ID > FormID.IdMask(style))
        {
            // The mod's own FormID in the form of its size has fewer ID bits, and masking would make it another record.
            // Its full index holds the whole ID with the same meaning (such a FormID can't be one of the mod's records:
            // the writer refuses new ones; it is a link stored that way, e.g. a master index past the list).
            if (_ownFullIndex >= IndexCapacity(MasterStyle.Full)) throw OwnIndexDoesNotFit(MasterStyle.Full, _ownFullIndex);
            return FormID.Factory(MasterStyle.Full, _ownFullIndex, formKey.ID);
        }

        return FormID.Factory(style, index, formKey.ID);
    }

    public FormKey GetFormKey(FormID formId, bool reference)
    {
        // Only a reference can be fully null with a null ModKey
        if (reference && formId.Raw == 0)
        {
            return FormKey.Null;
        }

        ExtractFormIdInfo(
            formId,
            out var style,
            out var index,
            out var id,
            out var loadOrder);

        if (index >= loadOrder.Count)
        {
            // A master index past the list (or the plugin's own FormIDs in a style it isn't listed in) reads as the plugin
            // itself, but is written back as the plugin's own FormID. Noted for SubrecordOrder, which must not count that
            // rewrite as a change of the record.
            if (!TryLookupModKey(CurrentMod, reference: true, out var ownStyle, out var ownIndex)
                || FormID.Factory(ownStyle, ownIndex, id).Raw != formId.Raw)
            {
                Mutagen.Bethesda.Plugins.Binary.Translations.SubrecordOrder.NonCanonicalFormIdRead = true;
            }
            return new FormKey(
                CurrentMod,
                id);
        }

        var master = loadOrder[checked((int)index)];
        return new FormKey(
            master,
            id);
    }
}