using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C220: names from the Creation Kit's display names (CreationKit.exe 1.16.244 reflection; the game strips them).
public class CkDisplayNameTests
{
    // XBSD is the engine's ExtraBendableSplineParams::ParamData (24 bytes); its float at offset 4 is fThickness, which the
    // CK shows as "Spline Radius". Slack 0.5, radius 2.25, half extents (1, 2, 3), not wind-detached, padding AB AB AB.
    private const string Xbsd = "0000003F 00001040 0000803F 00000040 00004040 00 ABABAB";

    [Fact]
    public void PlacedObjectSpline_SplineRadius()
    {
        foreach (var refr in ReadBoth<IPlacedObjectGetter>(Record("REFR", Subrecord("XBSD", Xbsd)), f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            refr.Spline!.Slack.ShouldBe(0.5f);
            refr.Spline.SplineRadius.ShouldBe(2.25f);
            GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w))).FindSubrecord(new RecordType("XBSD")).Content.ToArray().ShouldBe(Hex(Xbsd));
        }
        typeof(IPlacedObjectSplineGetter).GetProperty("Thickness").ShouldBeNull();
    }

    // WwiseSoundHook: the CK shows StartEvent, StopEvent, "Condition Form", "Event Form" (C218's names).
    [Fact]
    public void SoundReference_CkNames()
    {
        foreach (var name in new[] { "StartEvent", "StopEvent", "ConditionForm", "EventForm" })
            typeof(ISoundReferenceGetter).GetProperty(name).ShouldNotBeNull();
    }
}
