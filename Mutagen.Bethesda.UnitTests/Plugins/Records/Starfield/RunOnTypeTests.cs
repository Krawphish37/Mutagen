using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield condition Run On values from 8 up are Command Target 8, Event Camera Ref 9, My Killer 10, Self Packin 11,
// Target Packin 12, My Ship 13, Player Home Ship 14 and Player 15 (xEdit's Starfield definitions). Starfield.esm:
// 8 on 1 condition (PACK), 10 on 2 (IDLE), 11 and 12 on 76 and 239 (all STBH, whose packin targets fit), 13 on
// 228 (198 LVLN), 14 on 231.
public class RunOnTypeTests
{
    // The third condition of IDLE 346525 in Starfield.esm; byte 20 is Run On (10 there)
    private const string IdleCondition =
        "00-00-00-00-00-00-80-3F-30-02-00-00-08-B5-06-00-00-00-00-00-0A-00-00-00-00-00-00-00-FF-FF-FF-FF";

    [Theory]
    [InlineData(10, Condition.RunOnType.MyKiller)]
    [InlineData(8, Condition.RunOnType.CommandTarget)]
    [InlineData(9, Condition.RunOnType.EventCameraRef)]
    [InlineData(11, Condition.RunOnType.SelfPackin)]
    [InlineData(12, Condition.RunOnType.TargetPackin)]
    [InlineData(13, Condition.RunOnType.MyShip)]
    [InlineData(14, Condition.RunOnType.PlayerShip)]
    [InlineData(15, Condition.RunOnType.Player)]
    [InlineData(2, Condition.RunOnType.Reference)]
    public void RunOn_Reads(byte stored, Condition.RunOnType expected)
    {
        var ctda = Hex(IdleCondition);
        ctda[20] = stored;
        var bytes = Record("IDLE", Subrecord("CTDA", ctda));
        foreach (var idle in ReadBoth<IIdleAnimationGetter>(bytes, f => IdleAnimation.CreateFromBinary(f), (s, p) => IdleAnimationBinaryOverlay.IdleAnimationFactory(s, p)))
        {
            idle.Conditions.Single().Data.RunOnType.ShouldBe(expected);
        }
    }
}
