using System.IO.Abstractions;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Shouldly;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Top-level group order for the gameplay option groups. The Creation Kit writes AVMD, then GPOF and GPOG, then GWED:
// SFBGS00D.esm (official) has ... LGDI AVMD GPOF GPOG GWED, SFBGS050.esm ... LGDI PSDC AVMD GPOF GPOG, darkstar.esm
// ... LGDI AVMD GPOF GPOG; xEdit's group order is ... TODD AVMD PERS CHAL FXPD GPOF GPOG GWED. Mutagen wrote GPOF and
// GPOG before TODD and AVMD, so a mod with both came back with its groups in another order (darkstar.esm, found by
// the threeway prototype's Mutagen-only rewrite).
public class TopLevelGroupOrderTests
{
    private static List<string> TopLevelGroups(byte[] bytes)
    {
        var ret = new List<string>();
        int p = 24 + BitConverter.ToInt32(bytes, 4); // past the TES4 header record
        while (p + 24 <= bytes.Length)
        {
            Encoding.ASCII.GetString(bytes, p, 4).ShouldBe("GRUP");
            ret.Add(Encoding.ASCII.GetString(bytes, p + 8, 4));
            p += BitConverter.ToInt32(bytes, p + 4);
        }
        return ret;
    }

    [Theory, MutagenModAutoData(GameRelease.Starfield)]
    public void GameplayOptionGroupsFollowAvmdAndPrecedeGwed(IFileSystem fileSystem, ModPath filePath, StarfieldMod mod)
    {
        mod.GravityWielderEffectDatas.AddNew("TestGravityWielder");
        mod.GameplayOptionsGroups.AddNew("TestOptionsGroup").Options = new GameplayOptionsGroupLeaf();
        mod.GameplayOptions.AddNew("TestOption");
        mod.ActorValueModulations.AddNew("TestModulation");
        mod.TimeOfDays.AddNew("TestTimeOfDay");
        mod.BeginWrite.ToPath(filePath).WithNoLoadOrder().NoModKeySync().WithFileSystem(fileSystem).Write();

        TopLevelGroups(fileSystem.File.ReadAllBytes(filePath))
            .ShouldBe(new[] { "TODD", "AVMD", "GPOF", "GPOG", "GWED" });
    }
}
