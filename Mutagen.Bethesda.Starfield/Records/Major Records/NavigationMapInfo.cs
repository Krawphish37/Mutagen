using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Translations.Binary;
using Noggog;

namespace Mutagen.Bethesda.Starfield;

public partial class NavigationMapInfo
{
    // NVMI Flags (xEdit)
    [Flags]
    public enum Flag : uint
    {
        IsIsland = 0x20,
        NotEdited = 0x40,
    }
}

// The end of NVMI, after the traversals, per xEdit:
// - Has Island Data (1 byte), then the island data when it is not 0;
// - the pathing cell: CRC, Parent World, then the Parent Cell when Parent World is null, else the grid (Y then X);
// - one unknown byte and a UInt32-counted list of CRCs (empty everywhere in Starfield.esm).
// Starfield.esm: all 56,631 NVMI end exactly there; the has-byte is 0 or 1.
partial class NavigationMapInfoBinaryCreateTranslation
{
    public static partial void FillBinaryIslandAndPathingLogicCustom(MutagenFrame frame, INavigationMapInfo item)
    {
        ReadTail(frame, out var island, out var cell, out var unknownByte, out var crcs);
        item.IslandData = island;
        item.PathingCell = cell;
        item.UnknownByte = unknownByte;
        item.UnknownCrcs.SetTo(crcs);
    }

    internal static void ReadTail(MutagenFrame frame, out NavmeshIslandData? island, out NavmeshPathingCell cell, out byte unknownByte, out List<uint> crcs)
    {
        island = frame.ReadUInt8() != 0 ? NavmeshIslandData.CreateFromBinary(frame) : null;
        cell = new NavmeshPathingCell { Crc = frame.ReadUInt32() };
        cell.ParentWorld.SetTo(FormKeyBinaryTranslation.Instance.Parse(frame));
        if (cell.ParentWorld.IsNull)
        {
            cell.ParentCell.SetTo(FormKeyBinaryTranslation.Instance.Parse(frame));
        }
        else
        {
            cell.Grid = P2Int16BinaryTranslation<MutagenFrame, MutagenWriter>.Instance.Parse(frame, swapCoords: true);
        }
        unknownByte = frame.ReadUInt8();
        var count = checked((int)frame.ReadUInt32());
        crcs = new List<uint>(count);
        for (int i = 0; i < count; i++)
        {
            crcs.Add(frame.ReadUInt32());
        }
    }
}

partial class NavigationMapInfoBinaryWriteTranslation
{
    public static partial void WriteBinaryIslandAndPathingLogicCustom(MutagenWriter writer, INavigationMapInfoGetter item)
    {
        if (item.IslandData is { } island)
        {
            writer.Write((byte)1);
            island.WriteToBinary(writer);
        }
        else
        {
            writer.Write((byte)0);
        }
        var cell = item.PathingCell;
        writer.Write(cell.Crc);
        FormLinkBinaryTranslation.Instance.Write(writer, cell.ParentWorld);
        if (cell.ParentWorld.IsNull)
        {
            FormLinkBinaryTranslation.Instance.Write(writer, cell.ParentCell);
        }
        else
        {
            P2Int16BinaryTranslation<MutagenFrame, MutagenWriter>.Instance.Write(writer, cell.Grid, swapCoords: true);
        }
        writer.Write(item.UnknownByte);
        writer.Write(item.UnknownCrcs.Count);
        foreach (var crc in item.UnknownCrcs)
        {
            writer.Write(crc);
        }
    }
}

partial class NavigationMapInfoBinaryOverlay
{
    public INavmeshIslandDataGetter? IslandData { get; private set; }
    public INavmeshPathingCellGetter PathingCell { get; private set; } = null!;
    public byte UnknownByte { get; private set; }
    public IReadOnlyList<uint> UnknownCrcs { get; private set; } = Array.Empty<uint>();

    partial void CustomFactoryEnd(OverlayStream stream, int finalPos, int offset)
    {
        var frame = new MutagenFrame(new MutagenMemoryReadStream(_structData.Slice(TraversalsEndingPos), _package.MetaData));
        NavigationMapInfoBinaryCreateTranslation.ReadTail(frame, out var island, out var cell, out var unknownByte, out var crcs);
        IslandData = island;
        PathingCell = cell;
        UnknownByte = unknownByte;
        UnknownCrcs = crcs;
        IslandAndPathingLogicEndingPos = TraversalsEndingPos + checked((int)frame.Position);
    }
}

partial class NavmeshPathingCellBinaryOverlay
{
    // Pathing cells are only read through NavigationMapInfo (above), which builds them fully;
    // on its own the overlay can't tell which member of the union follows.
    public IFormLinkGetter<ICellGetter> ParentCell => FormLink<ICellGetter>.Null;
    public P2Int16 Grid => default;
}
