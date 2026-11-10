using System.IO.Abstractions;
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

    private IReadOnlyDictionary<ModKey, MasterStyleIndex> _lookup = null!;

    internal static readonly IReadOnlySeparatedMasterPackage EmptyNull =
        NotSeparate(new MasterReferenceCollection(ModKey.Null));

    // Hack until this is better understood
    private static HashSet<ModKey> _starfieldMasters = new()
    {
        "OldMars.esm", 
        "Constellation.esm", 
        "SFBGS003.esm", 
        "SFBGS004.esm", 
        "SFBGS006.esm",
        "SFBGS007.esm",
        "SFBGS008.esm",
        "SFBGS00D.esm",
        "SFBGS047.esm",
        "SFBGS050.esm",
        // The other official Small and Medium plugins: every one of them encodes its own records in its own style
        // (FE for Small, FD for Medium), like the plugins above (checked on all their record headers)
        "sfbgs00a_a.esm",
        "sfbgs00a_d.esm",
        "sfbgs00a_e.esm",
        "sfbgs00a_f.esm",
        "sfbgs00a_g.esm",
        "sfbgs00a_i.esm",
        "sfbgs00b.esm",
        "sfbgs019.esm",
        "sfbgs021.esm",
        "sfbgs023.esm",
        "sfta01.esm",
        "sfta02.esm",
        "sfta03.esm",
        "sfta04.esm",
        "sfta05.esm",
        "sfta06.esm",
    };

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
        var constants = GameConstants.Get(release);
        if (constants.SeparateMasterLoadOrders)
        {
            return SeparatedMasterPackage.Separate(currentModKey, style, masters, masterFlagLookup);
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
        return Factory(release, modPath.ModKey, header.MasterStyle, masters, masterFlagLookup);
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
        IReadOnlyCache<IModMasterStyledGetter, ModKey>? masterFlagLookup)
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
                throw new MissingModException(master,
                    "Mod was missing from load order when constructing the separate mod lists needed for FormID translation.");
            }

            AddToList(mod, master);
        }

        // Every master has to fit its FormID field.  Full indices end below 0xFD, as 0xFD and 0xFE in the
        // top byte mark Medium and Small FormIDs.  The mod's own index is only checked once it is used
        // (CheckOwnIndexFits on write, GetFormID), as a mod that only overrides records never needs one.
        CheckMasterCount(currentModKey, MasterStyle.Full, normal);
        CheckMasterCount(currentModKey, MasterStyle.Medium, medium);
        CheckMasterCount(currentModKey, MasterStyle.Small, small);

        if (_starfieldMasters.Contains(currentModKey))
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