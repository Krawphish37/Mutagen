using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Noggog;

namespace Mutagen.Bethesda.Starfield;

partial class NpcRaceOverride
{
    /// <summary>
    /// The ONA2 flags (first 4 bytes): which override sections follow, in this order.
    /// </summary>
    [Flags]
    public enum Section : uint
    {
        Size = 0x01,
        Movement = 0x02,
        Flight = 0x04,
        UnarmedWeapon = 0x08,
        FlagOverrides = 0x10,
        Bleedout = 0x20,
        General = 0x40,
        ElectromagneticShock = 0x80,
    }
}

partial class NpcRaceOverrideBinaryCreateTranslation
{
    public static partial void FillBinaryFlagParseCustom(
        MutagenFrame frame,
        INpcRaceOverride item)
    {
        var sections = (NpcRaceOverride.Section)frame.ReadUInt32();
        if (sections.HasFlag(NpcRaceOverride.Section.Size))
        {
            // Stored as a signed byte; -1 is Size.Unknown
            item.Size = (Size)(sbyte)frame.ReadUInt8();
        }
        if (sections.HasFlag(NpcRaceOverride.Section.Movement))
        {
            item.Movement = NpcRaceOverrideMovement.CreateFromBinary(frame);
        }
        if (sections.HasFlag(NpcRaceOverride.Section.Flight))
        {
            item.Flight = NpcRaceOverrideFlight.CreateFromBinary(frame);
        }
        if (sections.HasFlag(NpcRaceOverride.Section.UnarmedWeapon))
        {
            item.UnarmedWeapon = NpcRaceOverrideUnarmedWeapon.CreateFromBinary(frame);
        }
        if (sections.HasFlag(NpcRaceOverride.Section.FlagOverrides))
        {
            item.FlagOverrides = NpcRaceOverrideFlags.CreateFromBinary(frame);
        }
        if (sections.HasFlag(NpcRaceOverride.Section.Bleedout))
        {
            item.Bleedout = NpcRaceOverrideBleedout.CreateFromBinary(frame);
        }
        if (sections.HasFlag(NpcRaceOverride.Section.General))
        {
            item.General = NpcRaceOverrideGeneral.CreateFromBinary(frame);
        }
        if (sections.HasFlag(NpcRaceOverride.Section.ElectromagneticShock))
        {
            item.ElectromagneticShock = NpcRaceOverrideElectromagneticShock.CreateFromBinary(frame);
        }
    }

    public static NpcRaceOverride ParseContent(ReadOnlyMemorySlice<byte> content, BinaryOverlayFactoryPackage package)
    {
        var ret = new NpcRaceOverride();
        var frame = new MutagenFrame(new MutagenMemoryReadStream(content, package.MetaData));
        FillBinaryFlagParseCustom(frame, ret);
        return ret;
    }
}

partial class NpcRaceOverrideBinaryWriteTranslation
{
    public static partial void WriteBinaryFlagParseCustom(
        MutagenWriter writer,
        INpcRaceOverrideGetter item)
    {
        NpcRaceOverride.Section sections = default;
        sections = sections.SetFlag(NpcRaceOverride.Section.Size, item.Size != null);
        sections = sections.SetFlag(NpcRaceOverride.Section.Movement, item.Movement != null);
        sections = sections.SetFlag(NpcRaceOverride.Section.Flight, item.Flight != null);
        sections = sections.SetFlag(NpcRaceOverride.Section.UnarmedWeapon, item.UnarmedWeapon != null);
        sections = sections.SetFlag(NpcRaceOverride.Section.FlagOverrides, item.FlagOverrides != null);
        sections = sections.SetFlag(NpcRaceOverride.Section.Bleedout, item.Bleedout != null);
        sections = sections.SetFlag(NpcRaceOverride.Section.General, item.General != null);
        sections = sections.SetFlag(NpcRaceOverride.Section.ElectromagneticShock, item.ElectromagneticShock != null);
        writer.Write((uint)sections);
        if (item.Size is { } size)
        {
            writer.Write(unchecked((byte)(sbyte)(int)size));
        }
        item.Movement?.WriteToBinary(writer);
        item.Flight?.WriteToBinary(writer);
        item.UnarmedWeapon?.WriteToBinary(writer);
        item.FlagOverrides?.WriteToBinary(writer);
        item.Bleedout?.WriteToBinary(writer);
        item.General?.WriteToBinary(writer);
        item.ElectromagneticShock?.WriteToBinary(writer);
    }
}

partial class NpcRaceOverrideBinaryOverlay
{
    private NpcRaceOverride? _parsed;
    private NpcRaceOverride Parsed => _parsed ??= NpcRaceOverrideBinaryCreateTranslation.ParseContent(_structData, _package);

    public Size? Size => Parsed.Size;
    public INpcRaceOverrideMovementGetter? Movement => Parsed.Movement;
    public INpcRaceOverrideFlightGetter? Flight => Parsed.Flight;
    public INpcRaceOverrideUnarmedWeaponGetter? UnarmedWeapon => Parsed.UnarmedWeapon;
    public INpcRaceOverrideFlagsGetter? FlagOverrides => Parsed.FlagOverrides;
    public INpcRaceOverrideBleedoutGetter? Bleedout => Parsed.Bleedout;
    public INpcRaceOverrideGeneralGetter? General => Parsed.General;
    public INpcRaceOverrideElectromagneticShockGetter? ElectromagneticShock => Parsed.ElectromagneticShock;
}
