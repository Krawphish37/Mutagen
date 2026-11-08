using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A Starfield scene FX action's BTXT is the Start Time, a float (xEdit's Starfield definitions). Scene 18B60C in
// Starfield.esm stores 5.5 (00 00 B0 40) and 0.1 (CD CC CC 3D), which an Int32 read as 1,085,276,160 and 1,036,831,949.
public class FxSceneActionStartTimeTests
{
    [Theory]
    [InlineData(5.5f, new byte[] { 0x00, 0x00, 0xB0, 0x40 })]
    [InlineData(0.1f, new byte[] { 0xCD, 0xCC, 0xCC, 0x3D })]
    public void StartTime_WritesAndReadsBack(float time, byte[] expected)
    {
        var scene = new Scene(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield);
        scene.Actions = new() { new FxSceneAction { StartTime = time } };
        var bytes = Write(w => scene.WriteToBinary(w));
        GameConstants.Starfield.MajorRecord(bytes).FindSubrecord(new RecordType("BTXT")).Content.ToArray().ShouldBe(expected);
        foreach (var read in ReadBoth<ISceneGetter>(bytes, f => Scene.CreateFromBinary(f), (s, p) => SceneBinaryOverlay.SceneFactory(s, p)))
        {
            read.Actions!.OfType<IFxSceneActionGetter>().Single().StartTime.ShouldBe(time);
        }
    }
}
