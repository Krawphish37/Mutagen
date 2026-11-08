using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield scene actor flags (LNAM) per xEdit's Starfield definitions: Can Be Removed 0x04, Run Only Scene Packages
// 0x08, No Command State 0x10, No Player Dialogue 0x20, Timeline Headtracking 0x40, Timeline Cameras 0x80.
// Starfield.esm: 0x08 on 13,426 actors (Mutagen's old NoCommandState), 0x04 on 30.
public class SceneActorFlagTests
{
    private static IEnumerable<ISceneGetter> ReadScene(string lnam) =>
        ReadBoth<ISceneGetter>(Record("SCEN",
                Subrecord("ALID", "0E-00-00-00"), Subrecord("LNAM", lnam), Subrecord("DNAM", "12-00-00-00")),
            f => Scene.CreateFromBinary(f),
            (s, p) => SceneBinaryOverlay.SceneFactory(s, p));

    [Theory]
    [InlineData("06-00-00-00", SceneActor.Flag.Optional | SceneActor.Flag.CanBeRemoved)] // SCEN 2C6D07, actor 14 (CompanionAtLodge)
    [InlineData("08-00-00-00", SceneActor.Flag.RunOnlyScenePackages)]
    [InlineData("20-00-00-00", SceneActor.Flag.NoPlayerDialogue)]
    public void Flags_Read(string lnam, SceneActor.Flag expected)
    {
        foreach (var scene in ReadScene(lnam))
        {
            scene.Actors.Single().Flags.ShouldBe(expected);
        }
    }

    [Theory]
    [InlineData(SceneActor.Flag.CanBeRemoved, 0x04u)]
    [InlineData(SceneActor.Flag.RunOnlyScenePackages, 0x08u)]
    [InlineData(SceneActor.Flag.TimelineCameras, 0x80u)]
    public void Flag_Writes(SceneActor.Flag flag, uint expected)
    {
        var scene = new Scene(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield);
        scene.Actors.Add(new SceneActor { ID = 14, Flags = flag });
        var written = GameConstants.Starfield.MajorRecord(Write(w => scene.WriteToBinary(w)));
        BitConverter.ToUInt32(written.FindSubrecord(new RecordType("LNAM")).Content.Slice(0, 4)).ShouldBe(expected);
    }
}
