using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Surface tree CNAM and DNAM were raw bytes. xEdit: CNAM unknown, DNAM Edge Size (u32). In Starfield.esm both are
// 4 bytes on all 1,505 surface trees: CNAM is always 2 and DNAM always 256. CNAM becomes a UInt32 (still unknown),
// DNAM the UInt32 EdgeSize.
public class SurfaceTreeEdgeSizeTests
{
    // CassiopeiaeISurfaceTree (299071:Starfield.esm) without its four 256 KB / 64 KB grids (FNAM, GNAM)
    private static byte[] Bytes() => Record("SFTR",
        Subrecord("EDID", "43617373696F706569616549537572666163655472656500"),
        Subrecord("BFCB", "53757266616365547265655061747465726E53776170496E666F5F436F6D706F6E656E7400"),
        Subrecord("DAT2", "02000000C4DE2300A8106500002EBC200088"),
        Subrecord("BFCE", ""),
        Subrecord("CNAM", "02000000"),
        Subrecord("DNAM", "00010000"),
        Subrecord("NAM1", "4574612043617373696F70656961655C00"));

    [Fact]
    public void EdgeSize()
    {
        var bytes = Bytes();
        foreach (var tree in ReadBoth<ISurfaceTreeGetter>(bytes, f => SurfaceTree.CreateFromBinary(f), (s, p) => SurfaceTreeBinaryOverlay.SurfaceTreeFactory(s, p)))
        {
            tree.EditorID.ShouldBe("CassiopeiaeISurfaceTree");
            tree.CNAM.ShouldBe(2u);
            tree.EdgeSize.ShouldBe(256u);
            tree.Filter.ShouldBe("Eta Cassiopeiae\\");
            var written = GameConstants.Starfield.MajorRecord(Write(w => tree.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(GameConstants.Starfield.MajorRecord(bytes).Content.ToArray());
        }
    }
}
