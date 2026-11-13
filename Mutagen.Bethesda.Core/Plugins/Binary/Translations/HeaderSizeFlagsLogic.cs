using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Binary.Translations;

/// <summary>
/// BinaryWriteParameters.HeaderSizeFlags: which size (Small, Medium, Full) and Master flag a mod is written with.
/// The size rules are the FormID compaction ones: a Small master's own records have object IDs up to 0xFFF, a Medium
/// master's up to 0xFFFF (xEdit checks the same, by object ID only; overrides of other plugins' records don't count).
/// </summary>
internal static class HeaderSizeFlagsLogic
{
    private const int MasterFlag = 0x0000_0001;

    /// <summary>
    /// Whether the parameter sets the size (Auto only for Starfield; Smallest and the explicit sizes for any game that has
    /// them)
    /// </summary>
    public static bool Decides(HeaderSizeFlagsOption option, IModGetter mod)
    {
        var constants = GameConstants.Get(mod.GameRelease);
        switch (option)
        {
            case HeaderSizeFlagsOption.NoCheck:
                return false;
            case HeaderSizeFlagsOption.Auto:
                return mod.GameRelease.ToCategory() == GameCategory.Starfield;
            case HeaderSizeFlagsOption.Smallest:
                return true;
            case HeaderSizeFlagsOption.Small:
                if (constants.SmallMasterFlag == null)
                    throw new ArgumentException($"{mod.GameRelease} has no small masters", nameof(BinaryWriteParameters.HeaderSizeFlags));
                return true;
            case HeaderSizeFlagsOption.Medium:
                if (constants.MediumMasterFlag == null)
                    throw new ArgumentException($"{mod.GameRelease} has no medium masters", nameof(BinaryWriteParameters.HeaderSizeFlags));
                return true;
            case HeaderSizeFlagsOption.Full:
                return true;
            default:
                throw new NotImplementedException();
        }
    }

    /// <summary>
    /// The size to write the mod with, given the highest object ID among its own records (null: it has none)
    /// </summary>
    /// <param name="option">The parameter's value</param>
    /// <param name="mod">The mod being written</param>
    /// <param name="maxOwnId">The highest object ID among the mod's own records, null if it has none</param>
    /// <param name="setMaster">Whether to also set the Master flag (Auto and Smallest: a mod not read from a file whose
    /// ModKey is an .esm or .esl)</param>
    public static MasterStyle Pick(HeaderSizeFlagsOption option, IModGetter mod, uint? maxOwnId, out bool setMaster)
    {
        setMaster = false;
        switch (option)
        {
            case HeaderSizeFlagsOption.Small:
                return MasterStyle.Small;
            case HeaderSizeFlagsOption.Medium:
                return MasterStyle.Medium;
            case HeaderSizeFlagsOption.Full:
                return MasterStyle.Full;
            case HeaderSizeFlagsOption.Auto:
            {
                // The size the mod holds, raised only when its own records don't fit it
                var holds = mod.GetMasterStyle();
                var fits = SmallestThatFits(mod, maxOwnId);
                setMaster = NewMaster(mod);
                return Rank(holds) >= Rank(fits) ? holds : fits;
            }
            case HeaderSizeFlagsOption.Smallest:
                setMaster = NewMaster(mod);
                return SmallestThatFits(mod, maxOwnId);
            default:
                throw new NotImplementedException();
        }
    }

    /// <summary>
    /// The smallest size whose object-ID range holds the given highest own object ID
    /// </summary>
    public static MasterStyle SmallestThatFits(IModGetter mod, uint? maxOwnId)
    {
        var id = maxOwnId ?? 0;
        if (mod.CanBeSmallMaster && id <= FormID.SmallIdMask) return MasterStyle.Small;
        if (mod.CanBeMediumMaster && id <= FormID.MediumIdMask) return MasterStyle.Medium;
        return MasterStyle.Full;
    }

    private static bool NewMaster(IModGetter mod) =>
        !OverriddenFormsBaseline.WasRead(mod) && mod.ModKey.Type is ModType.Master or ModType.Light;

    private static int Rank(MasterStyle style) => style switch
    {
        MasterStyle.Small => 0,
        MasterStyle.Medium => 1,
        _ => 2,
    };

    /// <summary>
    /// The header flags with the size flags set for the given size (the other flags kept)
    /// </summary>
    public static int Apply(int rawFlags, GameConstants constants, MasterStyle style, bool setMaster)
    {
        var small = constants.SmallMasterFlag ?? 0;
        var medium = constants.MediumMasterFlag ?? 0;
        var ret = rawFlags & ~(small | medium);
        if (style == MasterStyle.Small) ret |= small;
        if (style == MasterStyle.Medium) ret |= medium;
        if (setMaster) ret |= MasterFlag;
        return ret;
    }

    /// <summary>
    /// The object IDs the mod's own records may use when written with the given size (the FormID compaction ranges)
    /// </summary>
    public static RangeUInt32 AllowedRange(IModGetter mod, MasterStyle style)
    {
        var lower = mod.GetDefaultInitialNextFormID(forceUseLowerFormIDRanges: null);
        return new RangeUInt32(lower, style switch
        {
            MasterStyle.Small => FormID.SmallIdMask,
            MasterStyle.Medium => FormID.MediumIdMask,
            _ => FormID.FullIdMask,
        });
    }
}
