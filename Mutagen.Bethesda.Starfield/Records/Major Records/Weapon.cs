using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield.Internals;

namespace Mutagen.Bethesda.Starfield;

public partial class Weapon
{
    [Flags]
    public enum MajorFlag
    {
        NonPlayable = 0x0000_0004,
        HighResFirstPersonOnly = 0x2000_0000,
    }
    
    public enum FiringTypeEnum
    {
        SingleOrBinary,
        Burst,
        Automatic,
    }

    // WGEN Type (xEdit; Fallout 4's list plus Mounted)
    public enum AnimationTypes
    {
        Unarmed = 0,
        OneHandSword = 1,
        OneHandDagger = 2,
        OneHandAxe = 3,
        OneHandMace = 4,
        TwoHandSword = 5,
        TwoHandAxe = 6,
        Bow = 7,
        Staff = 8,
        Gun = 9,
        Grenade = 10,
        Mine = 11,
        Mounted = 12,
    }

    // WAIM Reticle Type (xEdit)
    public enum ReticleTypeEnum
    {
        None = 0,
        Unarmed = 1,
        Pistol = 2,
        Shotgun = 3,
        Rifle = 4,
        Laser = 5,
        Tool = 6,
        CustomCrosshair1 = 7,
    }

    // WMEL Attack Speed (xEdit)
    public enum AttackSpeedEnum
    {
        VerySlow = 0,
        Slow = 1,
        Medium = 2,
        Fast = 3,
        VeryFast = 4,
    }

    public const string ObjectModificationName = "TESObjectWEAP_InstanceData";
    
    public enum Property
    {
        AimAssistTemplate = RecordTypeInts.AA00,
        AimInnerConeAngleDegrees = RecordTypeInts.AA01,
        AimOuterConeAngleDegrees = RecordTypeInts.AA02,
        SteeringDegreesperSecond = RecordTypeInts.AA03,
        PitchScale = RecordTypeInts.AA04,
        InnerSteeringRing = RecordTypeInts.AA05,
        OuterSteeringRing = RecordTypeInts.AA06,
        Friction = RecordTypeInts.AA07,
        MoveFollowDegreesperSecond = RecordTypeInts.AA08,
        ADSSnapSteeringMultiplier = RecordTypeInts.AA09,
        ADSSnapSeconds = RecordTypeInts.AA10,
        ADSSnapConeAngleDegrees = RecordTypeInts.AA11,
        NoSteering = RecordTypeInts.AA12,
        BulletBendingConeAngleDegrees = RecordTypeInts.AA13,
        ADSSnapSteeringMultiplierInnerRing = RecordTypeInts.AA14,
        ADSSnapSteeringMultiplierOuterRing = RecordTypeInts.AA15,
        ADSMultiplierInnerConeAngleDegrees = RecordTypeInts.AA16,
        ADSMultiplierOuterConeAngleDegrees = RecordTypeInts.AA17,
        ADSMultiplierInnerSteeringRing = RecordTypeInts.AA18,
        ADSMultiplierOuterSteeringRing = RecordTypeInts.AA19,
        ADSMultiplierFriction = RecordTypeInts.AA20,
        ADSMultiplierSteeringDegreeperSecond = RecordTypeInts.AA21,
        AimAssistEnabled = RecordTypeInts.AA22,
        AmmoList = RecordTypeInts.ALIS,
        AmmoType = RecordTypeInts.AMMO,
        AmmoCapacity = RecordTypeInts.ACPT,
        NPCsUseAmmo = RecordTypeInts.AUSE,
        Resistance = RecordTypeInts.DRES,
        Skill = RecordTypeInts.DSKL,
        BoltChargeSeconds = RecordTypeInts.FBOL,
        HasStagedTrigger = RecordTypeInts.FHST,
        GeneralType = RecordTypeInts.GTYP,
        Value = RecordTypeInts.GVAL,
        Weight = RecordTypeInts.GWEI,
        AttackOxygenCost = RecordTypeInts.WAOC,
        Power = RecordTypeInts.PAVI,
        PowerBonus = RecordTypeInts.PBAV,
        FullRechargeTime = RecordTypeInts.PFCT,
        RechargeDelay = RecordTypeInts.PRCD,
        AccuracyBonus = RecordTypeInts.WACB,
        AimModelBaseStability = RecordTypeInts.WABS,
        AimModelTemplate = RecordTypeInts.WAIM,
        AimModelConeMinDegrees = RecordTypeInts.WCO1,
        AimModelConeMaxDegrees = RecordTypeInts.WCO2,
        AimModelConeIncreasePerShot = RecordTypeInts.WCO3,
        AimModelConeDecreasePerSec = RecordTypeInts.WCO4,
        AimModelConeDecreaseDelayPerSec = RecordTypeInts.WCO5,
        AimModelConeIronSightsMultiplier = RecordTypeInts.WCO6,
        AimModelRecoilDiminishSpringForce = RecordTypeInts.WRC1,
        AimModelRecoilDiminishSightsMult = RecordTypeInts.WRC2,
        AimModelRecoilMaxDegreePerShot = RecordTypeInts.WRC3,
        AimModelRecoilMultiplierHipFire = RecordTypeInts.WRC5,
        // No member for WRC4, WRC6 and PUSE: the game's table has each twice (WRC4 fAimModelRecoilMinDegPerShot /
        // fAimModelRecoilArcDeg, WRC6 fAimModelConeSneakMultiplier / uAimModelRecoilShotsForRunaway, PUSE bUsePower /
        // bConsumeAmmo) and which one the game uses is unproven; the raw value still reads and writes (C216)
        // Slots the game keeps with no value, by the game's names (iDEPRECATED2 .. bDEPRECATED10, pwDEPRECATED) (C216)
        Deprecated2 = RecordTypeInts.W002,
        Deprecated3 = RecordTypeInts.W003,
        Deprecated4 = RecordTypeInts.W004,
        Deprecated5 = RecordTypeInts.W005,
        Deprecated6 = RecordTypeInts.W006,
        Deprecated7 = RecordTypeInts.W007,
        Deprecated8 = RecordTypeInts.W008,
        Deprecated9 = RecordTypeInts.W009,
        Deprecated10 = RecordTypeInts.W010,
        Deprecated = RecordTypeInts.WSWP,
        // The game names it fDEPRECATED9 too (W009 is bDEPRECATED9), so the ID tells them apart
        DeprecatedZFPF = RecordTypeInts.ZFPF,
        AimModelRecoilArcRotateDegrees = RecordTypeInts.WRC8,
        AimOpticalSightMarker = RecordTypeInts.WAOS,
        ActorValue = RecordTypeInts.WACV,
        AttackDelaySeconds = RecordTypeInts.WADL,
        BashDamage = RecordTypeInts.WSDM,
        Reach = RecordTypeInts.WREA,
        Stagger = RecordTypeInts.WSTG,
        AttackSeconds = RecordTypeInts.WATS,
        BlockMaterial = RecordTypeInts.WBMT,
        BoltAction = RecordTypeInts.WBOL,
        BashImpactDataSet = RecordTypeInts.WBSH,
        ChargingAttack = RecordTypeInts.WCAT,
        CriticalChargeBonus = RecordTypeInts.WCCB,
        ChargingReload = RecordTypeInts.WCHR,
        BurstDelaySeconds = RecordTypeInts.FBDS,
        BurstShots = RecordTypeInts.FBUR,
        CriticalDamageMultiplier = RecordTypeInts.WCDM,
        CriticalChanceIncMultiplier = RecordTypeInts.WCIM,
        DamagePhysical = RecordTypeInts.WDMG,
        DamageTypeValue = RecordTypeInts.WDTV,
        EnableMarkingTargetsRecon = RecordTypeInts.WEMT,
        Enchantment = RecordTypeInts.WENC,
        FullAuto = RecordTypeInts.FREP,
        FiringType = RecordTypeInts.FTYP,
        HasDualTrigger = RecordTypeInts.FHDT,
        ImpactDataset = RecordTypeInts.WIMP,
        Keyword = RecordTypeInts.WKEY,
        MinRange = RecordTypeInts.WMNR,
        MaxRange = RecordTypeInts.WMXR,
        ModCount = RecordTypeInts.WTMC,
        OverrideRateofFire = RecordTypeInts.FORF,
        ProjectileCount = RecordTypeInts.WNPR,
        ProjectileOverride = RecordTypeInts.WOPR,
        ReloadSpeed = RecordTypeInts.WRSP,
        Scope = RecordTypeInts.WSCP,
        ShotsperSecond = RecordTypeInts.WSPS,
        SightedTransitionSeconds = RecordTypeInts.WSTS,
        SoundLevel = RecordTypeInts.WSLV,
        Speed = RecordTypeInts.WSPD,
        ColorRemappingIndex = RecordTypeInts.WCOL,
        CriticalEffect = RecordTypeInts.WCRT,
        DisableShellCaseEject = RecordTypeInts.WDSH,
        CriticalEffectonDeathOnly = RecordTypeInts.WEDT,
        EquipSlot = RecordTypeInts.WEQS,
        FullPowerSeconds = RecordTypeInts.WFPS,
        FireSeconds = RecordTypeInts.WFSC,
        HitBehavior = RecordTypeInts.WHBV,
        HoldInput = RecordTypeInts.WHIP,
        MinorCrime = RecordTypeInts.WMCR,
        MinPowerperShot = RecordTypeInts.WMPS,
        Can = RecordTypeInts.WNDP,
        NonHostile = RecordTypeInts.WNHO,
        NonPlayable = RecordTypeInts.WNPL,
        OutofRangeDamageMult = RecordTypeInts.WOOR,
        OverrideShellCasing = RecordTypeInts.WOSC,
        PlayerOnly = RecordTypeInts.WPLY,
        VariableRangeApertureAcceleration = RecordTypeInts.VAAC,
        VariableRangeApertureDeceleration = RecordTypeInts.VADC,
        VariableRangeApertureInputRangeMin = RecordTypeInts.VAIN,
        VariableRangeApertureInputRangeMax = RecordTypeInts.VAIX,
        VariableRangeApertureValueRangeMin = RecordTypeInts.VAVN,
        VariableRangeApertureValueRangeMax = RecordTypeInts.VAVX,
        VariableRangeDistanceAcceleration = RecordTypeInts.VDAC,
        VariableRangeDistanceDeceleration = RecordTypeInts.VDDC,
        VariableRangeDistanceInputRangeMin = RecordTypeInts.VDIN,
        VariableRangeDistanceInputRangeMax = RecordTypeInts.VDIX,
        VariableRangeDistanceValueRangeMax = RecordTypeInts.VDVM,
        VariableRangeDistanceValueRangeMin = RecordTypeInts.VDVN,
        UseVariableRange = RecordTypeInts.VUSE,
        ReticleType = RecordTypeInts.WRET,
        Rank = RecordTypeInts.WRNK,
        ReloadSingle = RecordTypeInts.WRSG,
        SoundLevelMult = RecordTypeInts.WSLM,
        LayeredMaterialSwaps = RecordTypeInts.WSWL,
        ADSImageSpaceModifier = RecordTypeInts.ZIMG,
        ADSOffsetX = RecordTypeInts.ZOFX,
        ADSOffsetY = RecordTypeInts.ZOFY,
        ADSOffsetZ = RecordTypeInts.ZOFZ,
        Overlay = RecordTypeInts.ZOVL,
        FOVMult = RecordTypeInts.ZFOV,
        Template = RecordTypeInts.ZDTA,
        // Object templates (OBTS) on the record itself store these small ids, not the 4-letter OMOD codes above (xEdit wbObjectModPropertiesWEAPEnum).
        TemplatePhysicalWeaponDamage = 28,
        TemplateUnknown29 = 29,
        TemplateDamageTypeValue = 71,
        TemplateLayeredMaterialSwap = 85,
    }
    
    IFormLinkNullableGetter<IObjectEffectGetter> IEnchantableGetter.ObjectEffect => this.ObjectEffect;
}