using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// The planet content manager content properties component (on PCBN, PKIN, WRLD) per xEdit: UNAM is Days Until Reset
// [GLOB] and NAM5/NAM6/NAM7 are curves [CURV]. Mutagen read all four as UInt32, so FormID remapping missed them.
// In Starfield.esm PKIN 1D8EE7 uses two of the curves: PCM_CellLoad_ForceCurveOne and PCM_CellLoad_ForceCurveZero.
public class PlanetContentPropertiesLinkTests
{
    private static readonly (string Sig, string Hex)[] Pkin1D8EE7 =
    {
        ("ZNAM", "00-00-00-00"), ("YNAM", "00"), ("XNAM", "00-00-00-00"), ("WNAM", "00-00-00-00"), ("VNAM", "00"),
        ("UNAM", "00-00-00-00"), ("NAM1", "00-00-00-00"), ("NAM2", "00-00-00-00"), ("NAM3", "00-00-00-00"),
        ("NAM4", "00-FF-00-00"), ("NAM5", "00-00-00-00"), ("NAM6", "B5-9F-1F-00"), ("NAM7", "B4-9F-1F-00"),
        ("NAM8", "00"), ("NAM9", "00-00-00-00"), ("CITC", "00-00-00-00"),
    };

    private static byte[] PackInBytes(params (string Sig, string Hex)[] subs)
    {
        var parts = new List<byte[]> { Subrecord("BFCB", Encoding.ASCII.GetBytes("BGSPlanetContentManagerContentProperties_Component\0")) };
        parts.AddRange(subs.Select(s => Subrecord(s.Sig, s.Hex)));
        parts.Add(Subrecord("BFCE", Array.Empty<byte>()));
        return Record("PKIN", parts.ToArray());
    }

    private static IEnumerable<IPlanetContentManagerContentPropertiesComponentGetter> Read(byte[] bytes) =>
        ReadBoth<IPackInGetter>(bytes, f => PackIn.CreateFromBinary(f), (s, p) => PackInBinaryOverlay.PackInFactory(s, p))
            .Select(p => p.Components.OfType<IPlanetContentManagerContentPropertiesComponentGetter>().Single());

    [Fact]
    public void Curves_ReadAsLinks()
    {
        foreach (var props in Read(PackInBytes(Pkin1D8EE7)))
        {
            props.NumberOfTimesPerRequestCurve.IsNull.ShouldBeTrue();
            props.SpawnGroupsLimitCurve.FormKey.ID.ShouldBe(0x1F9FB5u); // PCM_CellLoad_ForceCurveOne
            props.SpawnGroupDistanceOverrideCurve.FormKey.ID.ShouldBe(0x1F9FB4u); // PCM_CellLoad_ForceCurveZero
            props.DaysUntilResetGlobal.IsNull.ShouldBeTrue();
            props.DebugColor!.Value.G.ShouldBe((byte)255);
            var links = props.EnumerateFormLinks().Select(l => l.FormKey.ID).ToArray();
            links.ShouldContain(0x1F9FB5u);
            links.ShouldContain(0x1F9FB4u);
        }
    }

    [Fact]
    public void Global_ReadsAsLink()
    {
        var subs = Pkin1D8EE7.Select(s => s.Sig == "UNAM" ? ("UNAM", "34-12-00-00") : s).ToArray();
        foreach (var props in Read(PackInBytes(subs)))
        {
            props.DaysUntilResetGlobal.FormKey.ID.ShouldBe(0x1234u);
            props.EnumerateFormLinks().Select(l => l.FormKey.ID).ShouldContain(0x1234u);
        }
    }

    [Fact]
    public void Component_WritesSameBytes()
    {
        var bytes = PackInBytes(Pkin1D8EE7);
        foreach (var packIn in ReadBoth<IPackInGetter>(bytes, f => PackIn.CreateFromBinary(f), (s, p) => PackInBinaryOverlay.PackInFactory(s, p)))
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => packIn.WriteToBinary(w)));
            foreach (var (sig, hex) in Pkin1D8EE7)
            {
                written.FindSubrecord(new RecordType(sig)).Content.ToArray().ShouldBe(Hex(hex), sig);
            }
        }
    }
}
