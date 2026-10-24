using System.Buffers.Binary;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Fallout3;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

/// <summary>
/// Typed enumeration of a cell's placed children on an overlay should only construct the children
/// of the requested type.  Each test makes the placed object unreadable (compressed flag over data
/// that is not zlib), so constructing it throws, and then asks for the placed npcs only.
/// </summary>
public class CellTypedChildEnumerationTests
{
    private const int CompressedFlag = 0x0004_0000;

    private static MemoryStream WriteWithUnreadableRecord(
        GameRelease release,
        Action<Stream> write,
        RecordType recordType,
        FormKey formKey)
    {
        var stream = new MemoryStream();
        write(stream);
        var bytes = stream.ToArray();
        var constants = GameConstants.Get(release).MajorConstants;
        for (int i = 0; i + constants.HeaderLength <= bytes.Length; i++)
        {
            if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i)) != recordType.TypeInt) continue;
            if ((BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i + 12)) & 0x00FF_FFFF) != formKey.ID) continue;
            var dataLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i + 4)));
            dataLength.ShouldBeGreaterThan(4);
            var flags = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i + 8));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i + 8), flags | CompressedFlag);
            var data = bytes.AsSpan(i + constants.HeaderLength, dataLength);
            BinaryPrimitives.WriteUInt32LittleEndian(data, 16);
            data.Slice(4).Fill(0xFF);
            return new MemoryStream(bytes);
        }
        throw new ArgumentException($"Could not find {recordType} {formKey}");
    }

    private static void AssertOnlyRequestedChildrenConstructed<TRequested, TUnreadable>(
        IModGetter overlay,
        params FormKey[] expected)
        where TRequested : class, IMajorRecordGetter
        where TUnreadable : class, IMajorRecordGetter
    {
        overlay.EnumerateMajorRecords<TRequested>()
            .Select(x => x.FormKey)
            .ShouldBe(expected);
        // Sanity: the corrupted child really is unreadable
        Assert.ThrowsAny<Exception>(() => overlay.EnumerateMajorRecords<TUnreadable>().ToArray());
    }

    [Fact]
    public void SkyrimOverlaySkipsOtherChildTypes()
    {
        var mod = new Mutagen.Bethesda.Skyrim.SkyrimMod(TestConstants.PluginModKey, Mutagen.Bethesda.Skyrim.SkyrimRelease.SkyrimSE);
        var persistentNpc = new Mutagen.Bethesda.Skyrim.PlacedNpc(mod);
        var obj = new Mutagen.Bethesda.Skyrim.PlacedObject(mod);
        obj.Base.SetTo(new FormKey(mod.ModKey, 0x123456));
        var temporaryNpc = new Mutagen.Bethesda.Skyrim.PlacedNpc(mod);
        var cell = new Mutagen.Bethesda.Skyrim.Cell(mod);
        cell.Persistent.Add(persistentNpc);
        cell.Temporary.Add(obj);
        cell.Temporary.Add(temporaryNpc);
        mod.Cells.Records.Add(new Mutagen.Bethesda.Skyrim.CellBlock()
        {
            BlockNumber = 0,
            GroupType = Mutagen.Bethesda.Skyrim.GroupTypeEnum.InteriorCellBlock,
            SubBlocks =
            {
                new Mutagen.Bethesda.Skyrim.CellSubBlock()
                {
                    BlockNumber = 0,
                    GroupType = Mutagen.Bethesda.Skyrim.GroupTypeEnum.InteriorCellSubBlock,
                    Cells = { cell }
                }
            }
        });
        var stream = WriteWithUnreadableRecord(GameRelease.SkyrimSE, s => mod.WriteToBinary(s), "REFR", obj.FormKey);
        using var overlay = Mutagen.Bethesda.Skyrim.SkyrimMod.CreateFromBinaryOverlay(stream, Mutagen.Bethesda.Skyrim.SkyrimRelease.SkyrimSE, mod.ModKey);
        AssertOnlyRequestedChildrenConstructed<Mutagen.Bethesda.Skyrim.IPlacedNpcGetter, Mutagen.Bethesda.Skyrim.IPlacedObjectGetter>(
            overlay, persistentNpc.FormKey, temporaryNpc.FormKey);
        overlay.EnumerateMajorRecordContexts<Mutagen.Bethesda.Skyrim.IPlacedNpc, Mutagen.Bethesda.Skyrim.IPlacedNpcGetter>(overlay.ToImmutableLinkCache())
            .Select(x => x.Record.FormKey)
            .ShouldBe(new[] { persistentNpc.FormKey, temporaryNpc.FormKey });
    }

    [Fact]
    public void Fallout4OverlaySkipsOtherChildTypes()
    {
        var mod = new Mutagen.Bethesda.Fallout4.Fallout4Mod(TestConstants.PluginModKey, Mutagen.Bethesda.Fallout4.Fallout4Release.Fallout4);
        var persistentNpc = new Mutagen.Bethesda.Fallout4.PlacedNpc(mod);
        var obj = new Mutagen.Bethesda.Fallout4.PlacedObject(mod);
        obj.Base.SetTo(new FormKey(mod.ModKey, 0x123456));
        var temporaryNpc = new Mutagen.Bethesda.Fallout4.PlacedNpc(mod);
        var cell = new Mutagen.Bethesda.Fallout4.Cell(mod);
        cell.Persistent.Add(persistentNpc);
        cell.Temporary.Add(obj);
        cell.Temporary.Add(temporaryNpc);
        mod.Cells.Records.Add(new Mutagen.Bethesda.Fallout4.CellBlock()
        {
            BlockNumber = 0,
            GroupType = Mutagen.Bethesda.Fallout4.GroupTypeEnum.InteriorCellBlock,
            SubBlocks =
            {
                new Mutagen.Bethesda.Fallout4.CellSubBlock()
                {
                    BlockNumber = 0,
                    GroupType = Mutagen.Bethesda.Fallout4.GroupTypeEnum.InteriorCellSubBlock,
                    Cells = { cell }
                }
            }
        });
        var stream = WriteWithUnreadableRecord(GameRelease.Fallout4, s => mod.WriteToBinary(s), "REFR", obj.FormKey);
        using var overlay = Mutagen.Bethesda.Fallout4.Fallout4Mod.CreateFromBinaryOverlay(stream, Mutagen.Bethesda.Fallout4.Fallout4Release.Fallout4, mod.ModKey);
        AssertOnlyRequestedChildrenConstructed<Mutagen.Bethesda.Fallout4.IPlacedNpcGetter, Mutagen.Bethesda.Fallout4.IPlacedObjectGetter>(
            overlay, persistentNpc.FormKey, temporaryNpc.FormKey);
    }

    [Fact]
    public void StarfieldOverlaySkipsOtherChildTypes()
    {
        var mod = new Mutagen.Bethesda.Starfield.StarfieldMod(TestConstants.PluginModKey, Mutagen.Bethesda.Starfield.StarfieldRelease.Starfield);
        var persistentNpc = new Mutagen.Bethesda.Starfield.PlacedNpc(mod);
        var obj = new Mutagen.Bethesda.Starfield.PlacedObject(mod);
        obj.Base.SetTo(new FormKey(mod.ModKey, 0x123456));
        var temporaryNpc = new Mutagen.Bethesda.Starfield.PlacedNpc(mod);
        var cell = new Mutagen.Bethesda.Starfield.Cell(mod);
        cell.Persistent.Add(persistentNpc);
        cell.Temporary.Add(obj);
        cell.Temporary.Add(temporaryNpc);
        mod.Cells.Records.Add(new Mutagen.Bethesda.Starfield.CellBlock()
        {
            BlockNumber = 0,
            GroupType = Mutagen.Bethesda.Starfield.GroupTypeEnum.InteriorCellBlock,
            SubBlocks =
            {
                new Mutagen.Bethesda.Starfield.CellSubBlock()
                {
                    BlockNumber = 0,
                    GroupType = Mutagen.Bethesda.Starfield.GroupTypeEnum.InteriorCellSubBlock,
                    Cells = { cell }
                }
            }
        });
        var stream = WriteWithUnreadableRecord(GameRelease.Starfield, s => mod.WriteToBinary(s), "REFR", obj.FormKey);
        using var overlay = Mutagen.Bethesda.Starfield.StarfieldMod.CreateFromBinaryOverlay(stream, Mutagen.Bethesda.Starfield.StarfieldRelease.Starfield, mod.ModKey);
        AssertOnlyRequestedChildrenConstructed<Mutagen.Bethesda.Starfield.IPlacedNpcGetter, Mutagen.Bethesda.Starfield.IPlacedObjectGetter>(
            overlay, persistentNpc.FormKey, temporaryNpc.FormKey);
        overlay.EnumerateMajorRecordContexts<Mutagen.Bethesda.Starfield.IPlacedNpc, Mutagen.Bethesda.Starfield.IPlacedNpcGetter>(overlay.ToImmutableLinkCache())
            .Select(x => x.Record.FormKey)
            .ShouldBe(new[] { persistentNpc.FormKey, temporaryNpc.FormKey });
    }

    [Fact]
    public void OblivionOverlaySkipsOtherChildTypes()
    {
        var mod = new Mutagen.Bethesda.Oblivion.OblivionMod(TestConstants.PluginModKey, Mutagen.Bethesda.Oblivion.OblivionRelease.Oblivion);
        var persistentNpc = new Mutagen.Bethesda.Oblivion.PlacedNpc(mod);
        var obj = new Mutagen.Bethesda.Oblivion.PlacedObject(mod);
        obj.Base.SetTo(new FormKey(mod.ModKey, 0x123456));
        var temporaryNpc = new Mutagen.Bethesda.Oblivion.PlacedNpc(mod);
        var distantNpc = new Mutagen.Bethesda.Oblivion.PlacedNpc(mod);
        var cell = new Mutagen.Bethesda.Oblivion.Cell(mod);
        cell.Persistent.Add(persistentNpc);
        cell.Temporary.Add(obj);
        cell.Temporary.Add(temporaryNpc);
        cell.VisibleWhenDistant.Add(distantNpc);
        mod.Cells.Records.Add(new Mutagen.Bethesda.Oblivion.CellBlock()
        {
            BlockNumber = 0,
            GroupType = Mutagen.Bethesda.Oblivion.GroupTypeEnum.InteriorCellBlock,
            SubBlocks =
            {
                new Mutagen.Bethesda.Oblivion.CellSubBlock()
                {
                    BlockNumber = 0,
                    GroupType = Mutagen.Bethesda.Oblivion.GroupTypeEnum.InteriorCellSubBlock,
                    Cells = { cell }
                }
            }
        });
        var stream = WriteWithUnreadableRecord(GameRelease.Oblivion, s => mod.WriteToBinary(s), "REFR", obj.FormKey);
        using var overlay = Mutagen.Bethesda.Oblivion.OblivionMod.CreateFromBinaryOverlay(stream, Mutagen.Bethesda.Oblivion.OblivionRelease.Oblivion, mod.ModKey);
        AssertOnlyRequestedChildrenConstructed<Mutagen.Bethesda.Oblivion.IPlacedNpcGetter, Mutagen.Bethesda.Oblivion.IPlacedObjectGetter>(
            overlay, persistentNpc.FormKey, temporaryNpc.FormKey, distantNpc.FormKey);
    }

    [Fact]
    public void Fallout3OverlaySkipsOtherChildTypes()
    {
        var mod = new Mutagen.Bethesda.Fallout3.Fallout3Mod(TestConstants.PluginModKey, Mutagen.Bethesda.Fallout3.Fallout3Release.Fallout3);
        var persistentNpc = new Mutagen.Bethesda.Fallout3.PlacedNpc(mod);
        var obj = new Mutagen.Bethesda.Fallout3.PlacedObject(mod);
        obj.Base.SetTo(new FormKey(mod.ModKey, 0x123456));
        var temporaryNpc = new Mutagen.Bethesda.Fallout3.PlacedNpc(mod);
        var distantNpc = new Mutagen.Bethesda.Fallout3.PlacedNpc(mod);
        var cell = new Mutagen.Bethesda.Fallout3.Cell(mod);
        cell.Persistent.Add(persistentNpc);
        cell.Temporary.Add(obj);
        cell.Temporary.Add(temporaryNpc);
        cell.VisibleWhenDistant.Add(distantNpc);
        mod.Cells.Records.Add(new Mutagen.Bethesda.Fallout3.CellBlock()
        {
            BlockNumber = 0,
            GroupType = Mutagen.Bethesda.Fallout3.GroupTypeEnum.InteriorCellBlock,
            SubBlocks =
            {
                new Mutagen.Bethesda.Fallout3.CellSubBlock()
                {
                    BlockNumber = 0,
                    GroupType = Mutagen.Bethesda.Fallout3.GroupTypeEnum.InteriorCellSubBlock,
                    Cells = { cell }
                }
            }
        });
        var stream = WriteWithUnreadableRecord(GameRelease.Fallout3, s => mod.WriteToBinary(s), "REFR", obj.FormKey);
        using var overlay = Mutagen.Bethesda.Fallout3.Fallout3Mod.CreateFromBinaryOverlay(stream, Mutagen.Bethesda.Fallout3.Fallout3Release.Fallout3, mod.ModKey);
        AssertOnlyRequestedChildrenConstructed<Mutagen.Bethesda.Fallout3.IPlacedNpcGetter, Mutagen.Bethesda.Fallout3.IPlacedObjectGetter>(
            overlay, persistentNpc.FormKey, temporaryNpc.FormKey, distantNpc.FormKey);
    }
}
