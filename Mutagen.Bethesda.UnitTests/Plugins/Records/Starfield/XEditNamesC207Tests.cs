using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using TempFile = Noggog.IO.TempFile;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C207: xEdit's names for two unknown fields that the data bears out: AVMD TNAM is the record's Name (xEdit keys its
// AVMD index on it; YNAM, also "Name" in xEdit, keeps its signature name), WRLD GNAM is the GravityScale float.
// DestructableData.Unknown and QuestReferenceAlias.ALFG stay unknown (see their xml comments).
public class XEditNamesC207Tests
{
    [Fact]
    public void NameIsTnamAndGravityScaleIsGnam_BothReaders()
    {
        var mod = new StarfieldMod(ModKey.FromNameAndExtension("XEditNamesC207.esp"), StarfieldRelease.Starfield);
        var avm = new ActorValueModulation(mod)
        {
            EditorID = "SimpleGroup_Test_Colors",
            Type = ActorValueModulation.GroupType.Simple,
            YNAM = "Test_Colors",
            Name = "Test_Colors_AK",
        };
        mod.ActorValueModulations.Add(avm);
        var world = new Worldspace(mod) { EditorID = "TestWorld", GravityScale = 0.5f };
        mod.Worldspaces.Add(world);

        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var path = new ModPath(mod.ModKey, tmp.File.Path);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();

        var full = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield);
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield);
        foreach (var read in new IStarfieldModGetter[] { full, overlay })
        {
            var readAvm = read.ActorValueModulations.Records.Single();
            readAvm.Name.ShouldBe("Test_Colors_AK");
            readAvm.YNAM.ShouldBe("Test_Colors");
            ((INamedRequiredGetter)readAvm).Name.ShouldBe("Test_Colors_AK");
            read.Worldspaces.Records.Single().GravityScale.ShouldBe(0.5f);
        }

        // TNAM holds Name, GNAM the scale
        var bytes = File.ReadAllBytes(tmp.File.Path);
        IndexOf(bytes, "TNAM"u8.ToArray().Concat(BitConverter.GetBytes((ushort)15)).Concat("Test_Colors_AK\0"u8.ToArray()).ToArray())
            .ShouldBeGreaterThan(0);
        IndexOf(bytes, "GNAM"u8.ToArray().Concat(BitConverter.GetBytes((ushort)4)).Concat(BitConverter.GetBytes(0.5f)).ToArray())
            .ShouldBeGreaterThan(0);
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        }
        return -1;
    }
}
