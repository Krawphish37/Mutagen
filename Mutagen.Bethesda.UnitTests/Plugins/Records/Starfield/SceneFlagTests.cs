using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield scene flags (FNAM) per xEdit's Starfield definitions: Top Level Topics On End 0x4, Interruptable 0x8,
// Player Dialogue 0x10, Prevent Player Exit Dialogue 0x20, Speech Challenge 0x100, Allow/Disable Dialogue Camera
// 0x400, No Follower Idle Chatter 0x800, Greeting 0x1000, Top Level 0x2000, Force Primary Actor Alias 0x4000,
// Hailing 0x8000, Ship Dialogue Only 0x10000. Starfield.esm: 0x100 is set on exactly the 993 scenes with speech
// challenge data, and 0x10 on 2,176 of the 2,202 scenes with a player dialogue action.
public class SceneFlagTests
{
    private static IEnumerable<ISceneGetter> ReadScene(string fnam) =>
        ReadBoth<ISceneGetter>(Record("SCEN", Subrecord("FNAM", fnam)),
            f => Scene.CreateFromBinary(f),
            (s, p) => SceneBinaryOverlay.SceneFactory(s, p));

    [Theory]
    [InlineData("10-01-00-00", Scene.Flag.PlayerDialogue | Scene.Flag.SpeechChallenge)] // SCEN 310D4
    [InlineData("00-80-01-00", Scene.Flag.Hailing | Scene.Flag.ShipDialogueOnly)]       // SCEN 327692
    public void Flags_Read(string fnam, Scene.Flag expected)
    {
        foreach (var scene in ReadScene(fnam))
        {
            scene.Flags.ShouldBe(expected);
        }
    }

    [Theory]
    [InlineData(Scene.Flag.TopLevelTopicsOnEnd, 0x4u)]
    [InlineData(Scene.Flag.PreventPlayerExitDialogue, 0x20u)]
    [InlineData(Scene.Flag.NoFollowerIdleChatter, 0x800u)]
    [InlineData(Scene.Flag.Greeting, 0x1000u)]
    public void Flag_Writes(Scene.Flag flag, uint expected)
    {
        var scene = new Scene(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield) { Flags = flag };
        var written = GameConstants.Starfield.MajorRecord(Write(w => scene.WriteToBinary(w)));
        BitConverter.ToUInt32(written.FindSubrecord(new RecordType("FNAM")).Content.Slice(0, 4)).ShouldBe(expected);
    }
}
