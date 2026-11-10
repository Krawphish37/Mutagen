using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Traversals (XTV2, on CELL and REFR) hold a 4-byte Flags field before the Traversal FormID (xEdit: Unknown 0, Unknown 1,
// No Traversal FormID). Mutagen read the flags only to decide whether a FormID follows and did not keep them: the full
// reader skipped the FormID only when the flags were exactly 4, the overlay whenever bit 2 was set, and the writer wrote
// 0 or 4. Now the flags are TraversalReference.Flags; bit 2 still follows whether Traversal is set, other bits are kept.
// Starfield.esm, 14,140,571 cell traversals: flags 4 (no FormID) on 14,140,490, flags 0 (with a FormID) on 81.
public class TraversalFlagTests
{
    // XTV2 of PackInShipPISMODHabBlackBonny3IntStorageCell (05B77A:Starfield.esm): 7 traversals, flags 0,
    // each with TraversalJumpDefault (00AFF2:Starfield.esm)
    private const string WithFormIDs = "00-00-00-00-88-42-F9-3E-7E-C4-8D-3F-36-73-C5-BF-88-42-F9-3E-5C-69-70-BF-37-73-C5-BF-95-F3-1F-40-71-89-80-40-B7-D1-81-3F-00-00-00-00-F2-AF-00-00-48-42-48-42-00-7C-00-7C-00-00-00-00-99-2C-10-C0-01-D0-09-40-E8-3D-BB-BF-42-03-56-BF-00-22-E8-BF-36-73-C5-BF-CE-E4-6E-40-F4-DC-AE-40-F3-B4-78-3F-00-00-00-00-F2-AF-00-00-99-41-99-41-00-7C-00-7C-00-00-00-00-FE-EE-01-3F-67-85-84-BF-38-73-C5-BF-34-38-CA-BC-AF-36-3D-40-C4-E8-AF-BF-86-43-3D-40-F8-B2-D9-40-52-93-94-3F-00-00-00-00-F2-AF-00-00-27-46-27-46-00-7C-00-7C-00-00-00-00-23-C5-29-BF-A6-48-3D-40-A2-2D-AC-BF-23-C5-29-BF-1C-D5-8B-BF-36-73-C5-BF-B9-6A-36-40-DA-94-DA-40-60-76-96-3F-00-00-00-00-F2-AF-00-00-48-42-48-42-00-7C-00-7C-01-00-00-00-8A-9F-82-BE-D9-26-85-BF-36-73-C5-BF-92-BB-49-BF-B0-36-3D-40-14-34-AC-BF-E2-43-3D-40-CE-A2-DA-40-9A-C6-94-3F-00-00-00-00-F2-AF-00-00-27-46-27-46-00-7C-00-7C-01-00-00-00-E6-47-D0-BF-6C-28-27-40-E6-9F-AF-BF-14-B5-A4-BE-52-93-85-BF-37-73-C5-BF-DF-0F-45-40-66-89-C1-40-2C-C9-8C-3F-00-00-00-00-F2-AF-00-00-99-41-99-41-00-7C-00-7C-01-00-00-00-48-5C-81-BD-A6-48-3D-40-DC-5E-AF-BF-48-5C-81-BD-AB-97-81-BF-36-73-C5-BF-9D-0F-3D-40-00-D5-CE-40-72-30-92-3F-00-00-00-00-F2-AF-00-00-48-42-48-42-00-7C-00-7C";

    // XTV2 of LC125SonasHome (27459B:Starfield.esm): one traversal with flags 4 (no FormID), then 4 zero bytes
    private const string NoFormID = "06-00-00-00-C8-AC-32-C0-A3-24-98-C0-78-20-54-3E-C2-0E-4C-C0-E8-5E-31-C0-EE-32-EC-3D-7D-8E-2D-40-47-39-62-40-25-98-6A-3F-04-00-00-00-16-46-16-46-2C-3D-52-3C-00-00-00-00";

    private static IEnumerable<ICellGetter> Read(string xtv2)
        => ReadBoth<ICellGetter>(Record("CELL", Subrecord("XTV2", xtv2)), f => Cell.CreateFromBinary(f), (s, p) => CellBinaryOverlay.CellFactory(s, p));

    private static void WritesBack(ICellGetter cell, string xtv2)
    {
        var written = GameConstants.Starfield.MajorRecord(Write(w => cell.WriteToBinary(w)));
        written.FindSubrecord(new RecordType("XTV2")).Content.ToArray().ShouldBe(Hex(xtv2));
    }

    [Fact]
    public void FlagsZero_HasTraversal()
    {
        foreach (var cell in Read(WithFormIDs))
        {
            cell.Traversals!.Count.ShouldBe(7);
            foreach (var trav in cell.Traversals)
            {
                trav.Flags.ShouldBe((TraversalReference.Flag)0);
                trav.Traversal.FormKey.ID.ShouldBe(0x00AFF2u);
            }
            WritesBack(cell, WithFormIDs);
        }
    }

    [Fact]
    public void NoTraversalFormID()
    {
        foreach (var cell in Read(NoFormID))
        {
            var trav = cell.Traversals!.Single();
            trav.Flags.ShouldBe((TraversalReference.Flag)0); // the fixes chain keeps bit 2 out of Flags (it follows whether Traversal is set)
            trav.Traversal.IsNull.ShouldBeTrue();
            trav.Traversal.IsNull.ShouldBeTrue();
            trav.Unknown1.ShouldBe(6);
            WritesBack(cell, NoFormID);
        }
    }

    [Fact]
    public void OtherFlagBits_Kept()
    {
        // The NoFormID traversal with bit 0 also set (05 instead of 04): both readers skip the FormID and keep the flags.
        var xtv2 = NoFormID.Replace("3F-04-00-00-00-16", "3F-05-00-00-00-16");
        foreach (var cell in Read(xtv2))
        {
            var trav = cell.Traversals!.Single();
            trav.Flags.ShouldBe(TraversalReference.Flag.Unknown0); // bit 2 is kept out of Flags on the fixes chain
            trav.Traversal.IsNull.ShouldBeTrue();
            WritesBack(cell, xtv2);
        }
    }

    [Fact]
    public void Writer_SetsBit2FromTraversal()
    {
        var item = new TraversalReference { Flags = TraversalReference.Flag.Unknown1 };
        item.Traversal.SetTo(new FormKey(TestModKey, 0x800));
        var bytes = Write(w => ((ITraversalReferenceGetter)item).WriteToBinary(w));
        BitConverter.ToInt32(bytes, 0x28).ShouldBe(2);
        item.Traversal.SetToNull();
        bytes = Write(w => ((ITraversalReferenceGetter)item).WriteToBinary(w));
        BitConverter.ToInt32(bytes, 0x28).ShouldBe(6);
    }
}
