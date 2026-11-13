using Mutagen.Bethesda.Plugins.Analysis.DI;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Internals;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;

namespace Mutagen.Bethesda.Plugins.Binary.Translations;

internal sealed class ModHeaderWriteLogic
{
    private readonly List<Action<IMajorRecordGetter>> _recordIterationActions = new();
    private readonly List<Action<IModContext<IMajorRecordGetter>>> _recordContextIterationActions = new();
    private readonly List<Action<FormKey, IFormLinkGetter>> _formLinkIterationActions = new();
    private readonly BinaryWriteParameters _params;
    private static readonly RecordCompactionCompatibilityDetector _recordCompactionDetector = new();

    private readonly ModKey _modKey;
    private readonly Dictionary<ModKey, FormKey?> _modKeys = new();
    private bool _usesOwnFormKeys;
    private uint _numRecords;
    private uint? _nextFormID;
    // With the header FormID as read when it is kept (RawHeaderFormIds): two records of a file can read as one FormKey
    // (02000869 and 03000869 with two masters) and still be written as the two FormIDs they are
    private readonly HashSet<(FormKey FormKey, uint? Raw)> _formKeyUniqueness = new();
    private readonly GameCategory _category;
    private FormKey? _disallowedLowerFormKey;
    private uint _higherFormIDRange;
    private GameConstants _constants;
    private readonly IModGetter _mod;
    private readonly HashSet<FormKey> _overriddenForms = new();
    private RangeUInt32? _recordRange;
    // The size the mod is written with (HeaderSizeFlags); its own FormIDs are encoded for it
    private MasterStyle _masterStyle;
    // Whether HeaderSizeFlags picks the size, so the FormID compaction check waits until the size is known
    private bool _sizeFromParams;

    private ModHeaderWriteLogic(
        BinaryWriteParameters param,
        IModGetter mod)
    {
        _mod = mod;
        _params = param;
        _modKey = mod.ModKey;
        _category = mod.GameRelease.ToCategory();
        _constants = GameConstants.Get(mod.GameRelease);
        _higherFormIDRange = _constants.DefaultHighRangeFormID;
        _masterStyle = mod.GetMasterStyle();
        _sizeFromParams = HeaderSizeFlagsLogic.Decides(param.HeaderSizeFlags, mod);
    }

    public static void WriteHeader(
        BinaryWriteParameters? param,
        MutagenWriter writer,
        IModGetter mod,
        IModHeaderCommon modHeader,
        ModKey modKey)
    {
        param ??= BinaryWriteParameters.Default;
        var modHeaderWriter = new ModHeaderWriteLogic(
            param: param,
            mod: mod);
        // Records read as the same FormKey as a later one, written back in their place by the record writer
        DuplicateRecords.BeginWrite(mod);
        modHeaderWriter.AddProcessors(mod, modHeader);
        modHeaderWriter.RunProcessors(mod);
        modHeaderWriter.PostProcessAdjustments(writer, mod, modHeader, 
            modHeaderWriter._constants.SeparateMasterLoadOrders
                ? param.MasterFlagsLookup 
                : null);
        modHeader.WriteToBinary(writer);
    }

    private void AddProcessors(
        IModGetter mod, 
        IModHeaderCommon modHeader)
    {
        AddMasterCollectionActions(mod);
        AddRecordCount();
        AddNextFormIDActions();
        AddFormIDUniqueness();
        AddFormIDCompactionLogic();
        AddCompactionTracking(modHeader);
        AddDisallowedLowerFormIDs();
        RegisterOverriddenFormsFishing();
        AddOwnIdTracking();
    }

    // The highest own object ID in a record header or a link (separated master games)
    private uint? _highestOwnId;

    private void AddOwnIdTracking()
    {
        if (!_constants.SeparateMasterLoadOrders) return;
        _recordIterationActions.Add(maj =>
        {
            var fk = maj.FormKey;
            if (fk.ModKey == _modKey && (_highestOwnId is not { } h || fk.ID > h)) _highestOwnId = fk.ID;
        });
        _formLinkIterationActions.Add((_, link) =>
        {
            var fk = link.FormKey;
            if (fk.ModKey == _modKey && (_highestOwnId is not { } h || fk.ID > h)) _highestOwnId = fk.ID;
        });
    }


    private void RunProcessors(IModGetter mod)
    {
        // Do any major record iteration work
        if (_recordIterationActions.Count > 0
            || _formLinkIterationActions.Count > 0
            || _recordContextIterationActions.Count > 0)
        {
            if (_recordContextIterationActions.Count > 0)
            {
                foreach (var context in mod.EnumerateMajorRecordSimpleContexts())
                {
                    RunProcessorsOnModContexts(context);
                }
            }
            else
            {
                foreach (var maj in mod.EnumerateMajorRecords())
                {
                    RunProcessorsOnMajorRecord(maj);
                }
            }
        }
    }

    private void RunProcessorsOnMajorRecord(IMajorRecordGetter maj)
    {
        foreach (var majAction in _recordIterationActions)
        {
            majAction(maj);
        }

        if (_formLinkIterationActions.Count > 0)
        {
            foreach (var linkInfo in maj.EnumerateFormLinks())
            {
                foreach (var formLinkAction in _formLinkIterationActions)
                {
                    formLinkAction(maj.FormKey, linkInfo);
                }
            }
        }
    }

    private void RunProcessorsOnModContexts(IModContext<IMajorRecordGetter> context)
    {
        RunProcessorsOnMajorRecord(context.Record);
        foreach (var majAction in _recordContextIterationActions)
        {
            majAction(context);
        }
    }

    private IReadOnlyMasterReferenceCollection ConstructWriteMasters(IModGetter mod)
    {
        MasterReferenceCollection ret = new MasterReferenceCollection(mod.ModKey);
        _modKeys.Remove(mod.ModKey);
        _modKeys.Remove(ModKey.Null);
        var modKeysList = _modKeys.Keys.ToList();
        SortMasters(mod, modKeysList);
        if (_params.MastersContentCustomOverride != null)
        {
            modKeysList = _params.MastersContentCustomOverride(modKeysList).ToList();
            SortMasters(mod, modKeysList);
        }
        modKeysList = SpellMasters(mod, modKeysList);
        ret.SetTo(modKeysList.Select(m => new MasterReference()
        {
            Master = m,
            FileSize = mod.GameRelease.ToCategory().IncludesMasterReferenceDataSubrecords() ? 0 : null
        }));
        return ret;
    }

    /// <summary>
    /// File names are compared without case, so the collected masters carry the spelling of whichever FormKey met
    /// them first, which depends on the order records are visited in. Each master is written as the mod's own master
    /// list spells it, else as the load order given for sorting does, else as collected.
    /// </summary>
    private List<ModKey> SpellMasters(IModGetter mod, List<ModKey> modKeysList)
    {
        var spelling = new Dictionary<ModKey, ModKey>();
        if (_params.MastersListOrdering is MastersListOrderingByLoadOrder byLoadOrder)
        {
            foreach (var m in byLoadOrder.LoadOrder)
            {
                spelling[m] = m;
            }
        }
        foreach (var m in mod.MasterReferences)
        {
            spelling[m.Master] = m.Master;
        }
        return modKeysList.Select(m => spelling.TryGetValue(m, out var spelled) ? spelled : m).ToList();
    }

    private void PostProcessAdjustments(
        MutagenWriter writer,
        IModGetter mod,
        IModHeaderCommon modHeader,
        IReadOnlyCache<IModMasterStyledGetter, ModKey>? masterFlagLookup)
    {
        HandleDisallowedLowerFormIDs(); 
        SetSizeFlags(mod, modHeader);
        SetOutgoingMasters(writer, mod, modHeader, masterFlagLookup);
        SetNumRecords(mod, modHeader);
        SetInteriorCellCount(mod, modHeader);
        SetNextFormID(mod, modHeader);
        SetOverriddenForms(modHeader);
    }
    
    private void SetOutgoingMasters(MutagenWriter writer, IModGetter mod, IModHeaderCommon modHeader,
        IReadOnlyCache<IModMasterStyledGetter, ModKey>? masterFlagLookup)
    {
        writer.MetaData.MasterReferences = ConstructWriteMasters(mod);
        writer.MetaData.SeparatedMasterPackage = SeparatedMasterPackage.Factory(
            mod.GameRelease,
            mod.ModKey,
            _masterStyle,
            writer.MetaData.MasterReferences,
            masterFlagLookup,
            // Reported style of the file it was read from; the form written is the size's (FE / FD), or the one set
            // through RoundTripMixIn.SetOwnFormIdStyle. FormIDs read in another form are kept per record (C147, C163).
            OwnFormIdStyle.Get(mod),
            OwnFormIdStyle.GetExplicit(mod));

        if (_usesOwnFormKeys
            && writer.MetaData.SeparatedMasterPackage is SeparatedMasterPackage separated)
        {
            // Refuse up front if the mod's own FormIDs will not fit, rather than partway through the records
            separated.CheckOwnIndexFits();
            // An own object ID above what the size's form holds is written with the full index (never masked), which
            // must fit too
            if (_masterStyle != MasterStyle.Full && _highestOwnId > FormID.IdMask(_masterStyle))
            {
                separated.CheckOwnFullIndexFits();
            }
        }

        modHeader.MasterReferences.SetTo(writer.MetaData.MasterReferences!.Masters.Select(m => m.DeepCopy()));
    }

    private void SetNextFormID(IModGetter mod, IModHeaderCommon modHeader)
    {
        if (_params.NextFormID == NextFormIDOption.Auto && OverriddenFormsBaseline.Applies(mod))
        {
            // Starfield: the value as read while still right (OverriddenFormsBaseline.HeaderCounts); an overlay can't
            // change, so it keeps it
            if (OverriddenFormsBaseline.IsUnchangeable(mod)) return;
            if (OverriddenFormsBaseline.HeaderCounts.NextObjectId(
                    OverriddenFormsBaseline.GetSnapshot(mod), modHeader.NextFormID, _nextFormID) is { } kept)
            {
                modHeader.NextFormID = kept;
                return;
            }
        }
        if (_params.NextFormID != NextFormIDOption.NoCheck)
        {
            bool? forceLowerBound = null;
            if (_params.MinimumFormID is ForceLowerFormIdRangeOption force)
            {
                forceLowerBound = force.ForceLowerRangeSetting;
            }
            modHeader.NextFormID = _nextFormID.HasValue ? _nextFormID.Value + 1 : mod.GetDefaultInitialNextFormID(forceLowerBound);
        }
    }

    private void SetNumRecords(IModGetter mod, IModHeaderCommon modHeader)
    {
        if (_params.RecordCount == RecordCountOption.Auto && OverriddenFormsBaseline.Applies(mod))
        {
            // Starfield: the Creation Kit counts records + 1, groups not counted; a mod read from a file keeps its count
            // moved by the change in records (OverriddenFormsBaseline.HeaderCounts). An overlay can't change.
            if (OverriddenFormsBaseline.IsUnchangeable(mod)) return;
            modHeader.NumRecords = OverriddenFormsBaseline.HeaderCounts.RecordCount(
                OverriddenFormsBaseline.GetSnapshot(mod), modHeader.NumRecords, _numRecords);
            return;
        }
        if (_params.RecordCount != RecordCountOption.NoCheck)
        {
            // Can't use raw count, as more gets considered in this tally
            modHeader.NumRecords = mod.GetRecordCount();
        }
    }

    private void SetInteriorCellCount(IModGetter mod, IModHeaderCommon modHeader)
    {
        if (_params.RecordCount == RecordCountOption.NoCheck) return;
        if (modHeader is not IModHeaderInteriorCellCount interiorCellCount) return;
        interiorCellCount.SyncInteriorCellCount(mod);
    }

    #region Master Content Sync Logic
    private void AddMasterCollectionActions(IModGetter mod)
    {
        switch (_params.MastersListContent)
        {
            case MastersListContentOption.NoCheck:
                _modKeys.Set(mod.MasterReferences.Select(m => new KeyValuePair<ModKey, FormKey?>(m.Master, FormKey.Null)));
                break;
            case MastersListContentOption.Auto:
                // Starfield, read from a file (or given its read state): every master it was read with stays, referenced
                // or not; the walk below adds the ones its records now reference. Otherwise as Iterate.
                if (OverriddenFormsBaseline.Applies(mod) && OverriddenFormsBaseline.WasRead(mod))
                {
                    _modKeys.Set(mod.MasterReferences.Select(m => new KeyValuePair<ModKey, FormKey?>(m.Master, FormKey.Null)));
                }
                goto case MastersListContentOption.Iterate;
            case MastersListContentOption.Iterate:
                _recordIterationActions.Add(maj =>
                {
                    var formKey = maj.FormKey;
                    if (mod.ModKey == formKey.ModKey)
                    {
                        _usesOwnFormKeys = true;
                        return;
                    }
                    if (_params.CleanNulls && formKey.IsNull) return;
                    _modKeys[formKey.ModKey] = formKey;
                });
                _formLinkIterationActions.Add((maj, formLink) =>
                {
                    if (mod.ModKey == formLink.FormKey.ModKey)
                    {
                        _usesOwnFormKeys = true;
                        return;
                    }
                    if (_params.CleanNulls && formLink.FormKey.IsNull) return;
                    _modKeys[formLink.FormKey.ModKey] = maj;
                });
                break;
            default:
                throw new NotImplementedException();
        }
    }

    #endregion

    #region Record Count Logic
    private void AddRecordCount()
    {
        switch (_params.RecordCount)
        {
            case RecordCountOption.NoCheck:
                break;
            case RecordCountOption.Iterate:
            case RecordCountOption.Auto:
                _recordIterationActions.Add(maj => _numRecords++);
                break;
            default:
                throw new NotImplementedException();
        }
    }
    #endregion

    #region FormID Uniqueness Logic
    private void AddFormIDUniqueness()
    {
        switch (_params.FormIDUniqueness)
        {
            case FormIDUniquenessOption.NoCheck:
                break;
            case FormIDUniquenessOption.Iterate:
                _recordIterationActions.Add(maj =>
                {
                    if (!_formKeyUniqueness.Add((maj.FormKey, RawHeaderFormIds.Get(maj))))
                    {
                        throw new ArgumentException($"Two records with the same FormKey were encountered: {maj.FormKey}");
                    }
                });
                break;
            default:
                throw new NotImplementedException();
        }
    }
    #endregion

    #region FormID Compaction Logic
    private void AddFormIDCompactionLogic()
    {
        switch (_params.FormIDCompaction)
        {
            case FormIDCompactionOption.NoCheck:
                break;
            case FormIDCompactionOption.Iterate:
            {
                // The size is picked after the records are walked (SetSizeFlags), which checks them then
                if (_sizeFromParams) break;
                var formIdRange = _recordCompactionDetector.GetAllowedRange(_mod, potential: true);
                if (formIdRange != null)
                {
                    _recordIterationActions.Add(maj =>
                    {
                        _recordCompactionDetector.ThrowIfIncompatible(_mod, formIdRange.Value, maj);
                    });
                }
                break;
            }
            default:
                throw new NotImplementedException();
        }
    }
    #endregion

    #region Size flags
    /// <summary>
    /// HeaderSizeFlags: picks the size the mod is written with from its own records' object IDs (walked by
    /// AddCompactionTracking), sets the header's size and Master flags, and then runs the FormID compaction check for
    /// that size
    /// </summary>
    private void SetSizeFlags(IModGetter mod, IModHeaderCommon modHeader)
    {
        if (!_sizeFromParams) return;
        var maxOwnId = _recordRange?.Max;
        _masterStyle = HeaderSizeFlagsLogic.Pick(_params.HeaderSizeFlags, mod, maxOwnId, out var setMaster);
        modHeader.RawFlags = HeaderSizeFlagsLogic.Apply(modHeader.RawFlags, _constants, _masterStyle, setMaster);

        if (_params.FormIDCompaction != FormIDCompactionOption.Iterate || _recordRange == null) return;
        var range = HeaderSizeFlagsLogic.AllowedRange(mod, _masterStyle);
        if (range.IsInRange(_recordRange.Value.Min) && range.IsInRange(_recordRange.Value.Max)) return;
        foreach (var maj in mod.EnumerateMajorRecords())
        {
            if (maj.FormKey.ModKey != _modKey || range.IsInRange(maj.FormKey.ID)) continue;
            throw new FormIDCompactionOutOfBoundsException(
                small: _masterStyle == MasterStyle.Small,
                medium: _masterStyle == MasterStyle.Medium,
                range: range,
                outOfBounds: maj);
        }
    }
    #endregion

    #region Next Form ID
    private void AddNextFormIDActions()
    {
        switch (_params.NextFormID)
        {
            case NextFormIDOption.NoCheck:
                break;
            case NextFormIDOption.Iterate:
            case NextFormIDOption.Auto:
                _recordIterationActions.Add(maj =>
                {
                    var fk = maj.FormKey;
                    if (fk.ModKey != _modKey) return;
                    if (!_nextFormID.HasValue || fk.ID > _nextFormID)
                    {
                        _nextFormID = fk.ID;
                    }
                });
                break;
            default:
                throw new NotImplementedException();
        }
    }
    #endregion

    #region Master Order Sync Logic
    /// <summary>
    /// Starfield, no ordering given: a mod read from a file keeps its masters in the file's order (the masters it still
    /// has); masters it didn't have come after them, in the default order. The default order sorts by name, which put
    /// Starfield.esm after "SFBGS..." and "ShatteredSpace.esm": darkstar.esm's Starfield.esm, sfbgs00d.esm,
    /// sfbgs003.esm came back as sfbgs003.esm, sfbgs00d.esm, Starfield.esm, and every master FormID was re-encoded.
    /// </summary>
    private void SortMasters(IModGetter mod, List<ModKey> modKeys)
    {
        if (_params.MastersListOrdering == null
            && OverriddenFormsBaseline.Applies(mod)
            && OverriddenFormsBaseline.WasRead(mod))
        {
            var read = mod.MasterReferences.Select(m => m.Master).ToList();
            var present = modKeys.ToHashSet();
            var kept = read.Where(present.Contains).Distinct().ToList();
            var keptSet = kept.ToHashSet();
            var added = modKeys.Where(m => !keptSet.Contains(m)).ToList();
            SortMasters(added);
            modKeys.Clear();
            modKeys.AddRange(kept);
            modKeys.AddRange(added);
            return;
        }
        SortMasters(modKeys);
    }

    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");

    private static readonly Comparer<ModKey> StarfieldMasterFirst = Comparer<ModKey>.Create((x, y) =>
    {
        var xFirst = x == StarfieldEsm;
        var yFirst = y == StarfieldEsm;
        if (xFirst != yFirst) return xFirst ? -1 : 1;
        return ModKey.AlphabeticalAndMastersFirst.Compare(x, y);
    });

    private void SortMasters(List<ModKey> modKeys)
    {
        switch (_params.MastersListOrdering)
        {
            case null:
                // Starfield: the game's own master first (by name it would come after its DLC, "SFBGS..." and
                // "ShatteredSpace.esm"); the rest by name as before
                modKeys.Sort(_category == GameCategory.Starfield
                    ? StarfieldMasterFirst
                    : ModKey.AlphabeticalAndMastersFirst);
                return;
            case MastersListOrderingEnumOption e:
                switch (e.Option)
                {
                    case MastersListOrderingOption.NoCheck:
                        return;
                    case MastersListOrderingOption.MastersFirst:
                        modKeys.Sort(ModKey.AlphabeticalAndMastersFirst);
                        return;
                    default:
                        throw new NotImplementedException();
                }
            case MastersListOrderingByLoadOrder lo:
                try
                {
                    modKeys.Sort(ModKey.LoadOrderComparer(lo.LoadOrder));
                }
                catch (Exception ex)
                {
                    if (ex is not ArgumentOutOfRangeException && ex is not InvalidOperationException) throw;
                    var keys = modKeys.ToHashSet();
                    keys.Remove(lo.LoadOrder);
                    var modToComplainAbout = keys.First();
                    throw new MissingModException(modToComplainAbout, $"A referenced mod was not present on the load order being sorted against: {modToComplainAbout}.  This mod was referenced by MajorRecord: {_modKeys[modToComplainAbout]}");
                }
                return;
            default:
                throw new NotImplementedException();
        }
    }
    #endregion

    #region Compaction
    private void AddCompactionTracking(IModHeaderCommon header)
    {
        _recordIterationActions.Add(maj =>
        {
            if (maj.FormKey.ModKey == _modKey)
            {
                if (_recordRange == null)
                {
                    _recordRange = new RangeUInt32(maj.FormKey.ID);
                }
                else if (maj.FormKey.ID > _recordRange.Value.Max)
                {
                    _recordRange = new RangeUInt32(_recordRange.Value.Min, maj.FormKey.ID);
                }
                else if (maj.FormKey.ID < _recordRange.Value.Min)
                {
                    _recordRange = new RangeUInt32(maj.FormKey.ID, _recordRange.Value.Max);
                }
            }
        });
    }
    #endregion

    #region DisallowedLowerFormIDs

    private void AddDisallowedLowerFormIDs()
    {
        // Base masters are allowed to own the lower range without any masters
        if (Implicits.Get(_mod.GameRelease).BaseMasters.Contains(_modKey)) return;
        switch (_params.LowerRangeDisallowedHandler)
        {
            case NoCheckIfLowerRangeDisallowed:
                break;
            default:
                _recordIterationActions.Add(maj =>
                {
                    if (maj.FormKey.ModKey != _modKey) return;
                    if (_higherFormIDRange > maj.FormKey.ID)
                    {
                        _disallowedLowerFormKey = maj.FormKey;
                    }
                });
                break;
        }
    }

    private void HandleDisallowedLowerFormIDs()
    {
        if (_disallowedLowerFormKey == null) return;
        // Lower range is only disallowed when there are no masters
        if (HasOutgoingMasters()) return;

        switch (_params.LowerRangeDisallowedHandler)
        {
            case NoCheckIfLowerRangeDisallowed:
                return;
            case ThrowIfLowerRangeDisallowed:
                throw new LowerFormKeyRangeDisallowedException(_disallowedLowerFormKey.Value);
            case AddPlaceholderMasterIfLowerRangeDisallowed placeholder:
                if (placeholder.ModKey == null)
                {
                    throw new LowerFormKeyRangeDisallowedException(_disallowedLowerFormKey.Value);
                }
                _modKeys[placeholder.ModKey.Value] = null;
                break;
        }
    }

    private bool HasOutgoingMasters()
    {
        // Mirrors ConstructWriteMasters, which drops the mod itself and the null ModKey
        foreach (var modKey in _modKeys.Keys)
        {
            if (modKey == _modKey || modKey.IsNull) continue;
            return true;
        }
        return false;
    }

    #endregion

    #region OverriddenForms

    private void RegisterOverriddenFormsFishing()
    {
        if (!_mod.ListsOverriddenForms) return;
        switch (_params.OverriddenFormsOption)
        {
            case OverriddenFormsOption.NoCheck:
                break;
            case OverriddenFormsOption.Iterate:
                CollectOverriddenForms();
                break;
            case OverriddenFormsOption.Auto:
                // Starfield only. A mod that can't change (an overlay) keeps its list as read: nothing to compute
                if (OverriddenFormsBaseline.Applies(_mod) && !OverriddenFormsBaseline.IsUnchangeable(_mod))
                {
                    CollectOverriddenForms();
                }
                break;
        }
    }

    private void CollectOverriddenForms()
    {
        _recordContextIterationActions.Add(context =>
        {
            if (OverriddenFormsBaseline.IsCandidate(context, _modKey))
            {
                _overriddenForms.Add(context.Record.FormKey);
            }
        });
    }

    private void SetOverriddenForms(
        IModHeaderCommon modHeader)
    {
        if (!_mod.ListsOverriddenForms) return;
        switch (_params.OverriddenFormsOption)
        {
            case OverriddenFormsOption.NoCheck:
                break;
            case OverriddenFormsOption.Iterate:
                if (_overriddenForms.Count == 0)
                {
                    modHeader.SetOverriddenForms(null);
                }
                else
                {
                    modHeader.SetOverriddenForms(OverriddenFormsBaseline.Sorted(_overriddenForms));
                }
                break;
            case OverriddenFormsOption.Auto:
                if (!OverriddenFormsBaseline.Applies(_mod) || OverriddenFormsBaseline.IsUnchangeable(_mod)) break;
                if (OverriddenFormsBaseline.Get(_mod) is { } baseline)
                {
                    // Read from a file (or given a baseline): the list as the mod holds it, updated only by what
                    // changed since the read
                    var current = _mod.OverriddenForms?.Select(x => x.FormKey).ToList();
                    var updated = OverriddenFormsBaseline.Update(current, baseline, _overriddenForms);
                    if (!ReferenceEquals(updated, current))
                    {
                        modHeader.SetOverriddenForms(updated);
                    }
                }
                else
                {
                    // Not read from a file: computed, as Iterate
                    modHeader.SetOverriddenForms(_overriddenForms.Count == 0
                        ? null
                        : OverriddenFormsBaseline.Sorted(_overriddenForms));
                }
                break;
        }
    }

    #endregion
}