using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// WriteToBinary(..., importMask) must write only the masked groups on the default (parallel) path too. Before the fix the
// parallel writer (<Game>ModCommon.WriteParallel) wrote every group; only the serial writer honoured the mask.
public class ParallelWriteImportMaskTests
{
    private static StarfieldMod MakeMod()
    {
        var mod = new StarfieldMod(ModKey.FromNameAndExtension("Test.esm"), StarfieldRelease.Starfield);
        mod.Weapons.AddNew("TestWeapon");
        mod.Weapons.AddNew("TestWeapon2");
        mod.Npcs.AddNew("TestNpc");
        mod.Keywords.AddNew("TestKeyword");
        return mod;
    }

    private static byte[] Write(StarfieldMod mod, GroupMask? mask, bool parallel)
    {
        var param = new BinaryWriteParameters
        {
            Parallel = new ParallelWriteParameters { MaxDegreeOfParallelism = parallel ? -1 : 1 },
        };
        using var stream = new MemoryStream();
        mod.WriteToBinary(stream, param, importMask: mask);
        return stream.ToArray();
    }

    // The labels (record types) of the top-level groups
    private static List<string> TopLevelGroups(byte[] bytes)
    {
        var ret = new List<string>();
        var p = 24 + BitConverter.ToInt32(bytes, 4);
        while (p < bytes.Length)
        {
            Encoding.ASCII.GetString(bytes, p, 4).ShouldBe("GRUP");
            ret.Add(Encoding.ASCII.GetString(bytes, p + 8, 4));
            p += BitConverter.ToInt32(bytes, p + 4);
        }
        return ret;
    }

    [Fact]
    public void MaskedParallelWrite_HasOnlyTheMaskedGroups()
    {
        var mod = MakeMod();
        var mask = new GroupMask(false) { Weapons = true };

        var parallel = Write(mod, mask, parallel: true);
        TopLevelGroups(parallel).ShouldBe(new[] { "WEAP" });

        // The same bytes as the serial writer, which always honoured the mask
        parallel.ShouldBe(Write(mod, mask, parallel: false));

        using var reread = StarfieldMod.CreateFromBinaryOverlay(new MemoryStream(parallel), StarfieldRelease.Starfield, mod.ModKey);
        reread.Weapons.Count.ShouldBe(2);
        reread.Npcs.Count.ShouldBe(0);
        reread.Keywords.Count.ShouldBe(0);
    }

    [Fact]
    public void MaskWithSeveralGroups_ParallelEqualsSerial()
    {
        var mod = MakeMod();
        var mask = new GroupMask(false) { Weapons = true, Keywords = true };
        var parallel = Write(mod, mask, parallel: true);
        TopLevelGroups(parallel).ShouldBe(new[] { "KYWD", "WEAP" });
        parallel.ShouldBe(Write(mod, mask, parallel: false));
    }

    [Fact]
    public void NoMask_ParallelStillWritesEveryGroup()
    {
        var mod = MakeMod();
        var parallel = Write(mod, null, parallel: true);
        TopLevelGroups(parallel).ShouldBe(new[] { "KYWD", "NPC_", "WEAP" }, ignoreOrder: true);
        parallel.ShouldBe(Write(mod, null, parallel: false));
    }
}
