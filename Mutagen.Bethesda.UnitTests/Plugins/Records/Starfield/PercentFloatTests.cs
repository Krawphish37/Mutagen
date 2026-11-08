using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield stores these "percent" fields as plain floats (xEdit's Starfield definitions; Mutagen's own Fallout 4 and
// Skyrim CombatStyle use Float for the same fields): Dirtiness Scale (ODTY, 44 record types), 65 CombatStyle fields,
// Grass Height/Color Range, EffectShader alpha ratios and LandscapeTexture Dirtiness. A Percent with no integer type
// read them as a UInt32 fraction of 4,294,967,295 (1.0 became 0.248) and clamped to 0-1.
public class PercentFloatTests
{
    [Fact]
    public void DirtinessScale_ReadsOne()
    {
        // PackIn 3A78E2 in Starfield.esm: ODTY 00 00 80 3F
        var bytes = Record("PKIN", Subrecord("ODTY", "00-00-80-3F"));
        foreach (var packIn in ReadBoth<IPackInGetter>(bytes, f => PackIn.CreateFromBinary(f), (s, p) => PackInBinaryOverlay.PackInFactory(s, p)))
        {
            packIn.DirtinessScale.ShouldBe(1f);
        }
    }

    [Fact]
    public void CombatStyle_GeneralMults()
    {
        // CombatStyle 2BFC0E: CSGD offensive mult 1.0, avoid threat chance 0.5
        var bytes = Record("CSTY", Subrecord("CSGD",
            "00-00-80-3F-00-00-00-00-00-00-00-00-FF-FF-1F-41-00-00-80-3F-CC-CC-CC-3D-00-00-00-00-FF-FF-1F-41-00-00-00-00-00-00-00-3F-00-00-40-3F-99-99-19-3F-00-00-00-00-00-00-80-3F-00-00-80-3F"));
        foreach (var style in ReadBoth<ICombatStyleGetter>(bytes, f => CombatStyle.CreateFromBinary(f), (s, p) => CombatStyleBinaryOverlay.CombatStyleFactory(s, p)))
        {
            style.OffensiveMult.ShouldBe(1f);
            style.DefensiveMult.ShouldBe(0f);
            style.AvoidThreatChance.ShouldBe(0.5f);
        }
    }

    [Fact]
    public void Grass_HeightRange()
    {
        // GrassFieldDried02C (GRAS 2CC3FB): DNAM height range 0.25 (00 00 80 3E)
        var bytes = Record("GRAS", Subrecord("DNAM",
            "00-00-B4-42-33-33-33-3F-00-00-80-3E-00-00-00-00-00-00-20-41-00-00-00-00-FF-FF-7F-7F-5A-00-32-06-00-00-28-42-7F-7F"));
        foreach (var grass in ReadBoth<IGrassGetter>(bytes, f => Grass.CreateFromBinary(f), (s, p) => GrassBinaryOverlay.GrassFactory(s, p)))
        {
            grass.HeightRange.ShouldBe(0.25f);
            grass.ColorRange.ShouldBe(0f);
        }
    }

    [Fact]
    public void DirtinessScale_WritesTheFloat()
    {
        var packIn = new PackIn(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield) { DirtinessScale = 1f };
        var written = GameConstants.Starfield.MajorRecord(Write(w => packIn.WriteToBinary(w)));
        written.FindSubrecord(new RecordType("ODTY")).Content.ToArray().ShouldBe(Hex("00-00-80-3F"));
    }
}
