using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins;

namespace Mutagen.Bethesda.Starfield;

/// <summary>
/// Which enum names a Starfield record type's header flags (the record header's Flags, <c>MajorRecordFlagsRaw</c>).
/// A consumer can name any record's header flags from its signature or getter type without looking for a nested
/// <c>MajorFlag</c> type by name. A type with no entry has no named header flags (print the raw bits).
/// Placed objects (REFR) take their enum from the base object's record type: see <see cref="GetPlacedObjectEnumType"/>.
/// </summary>
public static class StarfieldMajorFlagRegistry
{
    private static readonly Dictionary<RecordType, Type> _byRecordType = new();
    private static readonly Dictionary<Type, Type> _byGetterType = new();

    static StarfieldMajorFlagRegistry()
    {
        Add("AACT", typeof(IActionRecordGetter), typeof(ActionRecord.MajorFlag));
        Add("ACHR", typeof(IPlacedNpcGetter), typeof(PlacedNpc.MajorFlag));
        Add("ACTI", typeof(IActivatorGetter), typeof(Activator.MajorFlag));
        Add("ALCH", typeof(IIngestibleGetter), typeof(Ingestible.MajorFlag));
        Add("AMMO", typeof(IAmmunitionGetter), typeof(Ammunition.MajorFlag));
        Add("ARMA", typeof(IArmorAddonGetter), typeof(ArmorAddon.MajorFlag));
        Add("ARMO", typeof(IArmorGetter), typeof(Armor.MajorFlag));
        Add("BMMO", typeof(IBiomeMarkerGetter), typeof(BiomeMarker.MajorFlag));
        Add("CELL", typeof(ICellGetter), typeof(Cell.MajorFlag));
        Add("CLFM", typeof(IColorRecordGetter), typeof(ColorRecord.MajorFlag));
        Add("COBJ", typeof(IConstructibleObjectGetter), typeof(ConstructibleObject.MajorFlag));
        Add("CONT", typeof(IContainerGetter), typeof(Container.MajorFlag));
        Add("CSTY", typeof(ICombatStyleGetter), typeof(CombatStyle.MajorFlag));
        Add("DIAL", typeof(IDialogTopicGetter), typeof(DialogTopic.MajorFlag));
        Add("DOOR", typeof(IDoorGetter), typeof(Door.MajorFlag));
        Add("FURN", typeof(IFurnitureGetter), typeof(Furniture.MajorFlag));
        Add("GBFM", typeof(IGenericBaseFormGetter), typeof(GenericBaseForm.MajorFlag));
        Add("GLOB", typeof(IGlobalGetter), typeof(Global.MajorFlag));
        Add("HDPT", typeof(IHeadPartGetter), typeof(HeadPart.MajorFlag));
        Add("IDLM", typeof(IIdleMarkerGetter), typeof(IdleMarker.MajorFlag));
        Add("INFO", typeof(IDialogResponsesGetter), typeof(DialogResponses.MajorFlag));
        Add("KEYM", typeof(IKeyGetter), typeof(Key.MajorFlag));
        Add("KYWD", typeof(IKeywordGetter), typeof(Keyword.MajorFlag));
        Add("LAYR", typeof(ILayerGetter), typeof(Layer.MajorFlag));
        Add("LCTN", typeof(ILocationGetter), typeof(Location.MajorFlag));
        Add("LIGH", typeof(ILightGetter), typeof(Light.MajorFlag));
        Add("LSCR", typeof(ILoadScreenGetter), typeof(LoadScreen.MajorFlag));
        Add("LVLB", typeof(ILeveledBaseFormGetter), typeof(LeveledBaseForm.MajorFlag));
        Add("LVLI", typeof(ILeveledItemGetter), typeof(LeveledItem.MajorFlag));
        Add("LVLN", typeof(ILeveledNpcGetter), typeof(LeveledNpc.MajorFlag));
        Add("LVLP", typeof(ILeveledPackInGetter), typeof(LeveledPackIn.MajorFlag));
        Add("MGEF", typeof(IMagicEffectGetter), typeof(MagicEffect.MajorFlag));
        Add("MISC", typeof(IMiscItemGetter), typeof(MiscItem.MajorFlag));
        Add("MSTT", typeof(IMoveableStaticGetter), typeof(MoveableStatic.MajorFlag));
        Add("NAVM", typeof(INavigationMeshGetter), typeof(NavigationMesh.MajorFlag));
        Add("NPC_", typeof(INpcGetter), typeof(Npc.MajorFlag));
        Add("OMOD", typeof(IAObjectModificationGetter), typeof(AObjectModification.MajorFlag));
        Add("PARW", typeof(IPlacedArrowGetter), typeof(APlacedTrap.MajorFlag));
        Add("PBAR", typeof(IPlacedBarrierGetter), typeof(APlacedTrap.MajorFlag));
        Add("PBEA", typeof(IPlacedBeamGetter), typeof(APlacedTrap.MajorFlag));
        Add("PCON", typeof(IPlacedConeGetter), typeof(APlacedTrap.MajorFlag));
        Add("PERK", typeof(IPerkGetter), typeof(Perk.MajorFlag));
        Add("PFLA", typeof(IPlacedFlameGetter), typeof(APlacedTrap.MajorFlag));
        Add("PGRE", typeof(IPlacedTrapGetter), typeof(APlacedTrap.MajorFlag));
        Add("PHZD", typeof(IPlacedHazardGetter), typeof(APlacedTrap.MajorFlag));
        Add("PKIN", typeof(IPackInGetter), typeof(PackIn.MajorFlag));
        Add("PMIS", typeof(IPlacedMissileGetter), typeof(APlacedTrap.MajorFlag));
        Add("PROJ", typeof(IProjectileGetter), typeof(Projectile.MajorFlag));
        Add("QUST", typeof(IQuestGetter), typeof(Quest.MajorFlag));
        Add("REGN", typeof(IRegionGetter), typeof(Region.MajorFlag));
        Add("SCOL", typeof(IStaticCollectionGetter), typeof(StaticCollection.MajorFlag));
        Add("STAT", typeof(IStaticGetter), typeof(Static.MajorFlag));
        Add("STDT", typeof(IStarGetter), typeof(Star.MajorFlag));
        Add("TACT", typeof(ITalkingActivatorGetter), typeof(TalkingActivator.MajorFlag));
        Add("TERM", typeof(ITerminalGetter), typeof(Terminal.MajorFlag));
        Add("TRNS", typeof(ITransformGetter), typeof(Transform.MajorFlag));
        Add("WEAP", typeof(IWeaponGetter), typeof(Weapon.MajorFlag));
        Add("WRLD", typeof(IWorldspaceGetter), typeof(Worldspace.MajorFlag));
    }

    private static void Add(string recordType, Type getterType, Type enumType)
    {
        _byRecordType.Add(new RecordType(recordType), enumType);
        _byGetterType.Add(getterType, enumType);
    }

    /// <summary>Record type (signature) to its header-flag enum type. REFR is not listed (base-dependent).</summary>
    public static IReadOnlyDictionary<RecordType, Type> EnumTypesByRecordType => _byRecordType;

    /// <summary>Getter interface (e.g. <c>IKeyGetter</c>) to its header-flag enum type.</summary>
    public static IReadOnlyDictionary<Type, Type> EnumTypesByGetterType => _byGetterType;

    /// <summary>The header-flag enum of a record type, e.g. KEYM gives <c>typeof(Key.MajorFlag)</c>.</summary>
    public static bool TryGetEnumType(RecordType recordType, [MaybeNullWhen(false)] out Type enumType)
        => _byRecordType.TryGetValue(recordType, out enumType);

    /// <summary>
    /// The header-flag enum of a record class or interface: the class (<c>Key</c>), its getter (<c>IKeyGetter</c>) or
    /// setter (<c>IKey</c>) interface.
    /// </summary>
    public static bool TryGetEnumType(Type recordType, [MaybeNullWhen(false)] out Type enumType)
    {
        if (_byGetterType.TryGetValue(recordType, out enumType)) return true;
        foreach (var kv in _byGetterType)
        {
            if (kv.Key.IsAssignableFrom(recordType))
            {
                enumType = kv.Value;
                return true;
            }
        }
        enumType = null;
        return false;
    }

    /// <summary>
    /// The header-flag enum of a placed object (REFR), chosen by the record type of the object it places, as the
    /// <see cref="PlacedObject"/> enums document; <see cref="PlacedObject.DefaultMajorFlag"/> for any other base type.
    /// </summary>
    public static Type GetPlacedObjectEnumType(RecordType baseRecordType)
    {
        switch (baseRecordType.Type)
        {
            case "ACTI":
            case "STAT":
            case "SCOL":
            case "TREE":
                return typeof(PlacedObject.StaticMajorFlag);
            case "CONT":
            case "TERM":
                return typeof(PlacedObject.ContainerMajorFlag);
            case "DOOR":
                return typeof(PlacedObject.DoorMajorFlag);
            case "LIGH":
                return typeof(PlacedObject.LightMajorFlag);
            case "MSTT":
                return typeof(PlacedObject.MoveableStaticMajorFlag);
            case "ADDN":
                return typeof(PlacedObject.AddonNodeMajorFlag);
            case "ALCH":
            case "BOOK":
            case "SCRL":
            case "AMMO":
            case "ARMO":
            case "INGR":
            case "KEYM":
            case "MISC":
            case "FURN":
            case "WEAP":
                return typeof(PlacedObject.ItemMajorFlag);
            default:
                return typeof(PlacedObject.DefaultMajorFlag);
        }
    }
}
