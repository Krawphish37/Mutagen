using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Placed light data (XLIG) had Fallout 4's field names. xEdit's Starfield names: FOV 90+/-, Luminous Scale,
// End Distance Cap, Near Clip/Cut On, Inner FOV, Shadow Offset, then two unknown 4-byte fields. The second unknown is
// one byte (0 or 1) and 3 filler bytes (01 AB AB AB on 3,981 of 100,532 lights in Starfield.esm, 00 AB AB AB on
// 96,547), so as a UInt32 it read 2880154369. The first unknown is AB AB AB AB filler on all but 3 lights.
// Volume reflection probe offset/intensity (XVOI) was Float1-4: an offset (X, Y, Z) and an intensity
// (1.0 on 11,518 of 11,711; also 2, 0, 0.35, 0.2).
// External emittance (XEED, REFR and ACHR) was 8 raw bytes; xEdit: External Emittance Luminance Scale (float), Override
// Enabled (u8 bool) and 3 unused bytes. Starfield.esm, 16,153 REFR: the bool is 1 on all, the filler AB AB AB on 16,141.
public class PlacedLightProbeFieldTests
{
    // XLIG of REFR 1D2D2A (Starfield.esm)
    private const string Light = "68-D0-17-C1-9A-99-99-3F-5C-8F-72-40-6A-3D-6A-3F-FA-A4-D2-41-00-00-00-00-AB-AB-AB-AB-01-AB-AB-AB";

    // REFR 336FEC (Starfield.esm): a reflection probe volume with XVOI offset (-3, -3, -2), intensity 1
    private const string Probe = "424643421200566F6C756D65735F436F6D706F6E656E7400564C4D53600001000000050000000000803F000000000000000000000000000000000000803F000000000000000000000000000000000000803F000000000000000000000000000000000000803F03BDA33FDC0CE13F8EA4053FBC70A3411C99B44056DB1C414246434500004E414D45040043000000584C59520400E46F330058564C32550000000000000000000000000000000000000000000000803F0000803FCDCC4C3E0000803F010000000000803F0000803F0000803F000000000000803F0000803F00000000000000000000803F00004843000AD7A33C58564F491000000040C0000040C0000000C00000803F4441544118004FCF6843D97D63422B6C0541000000000000000000000000";

    [Fact]
    public void LightData_Names()
    {
        foreach (var refr in ReadBoth<IPlacedObjectGetter>(Record("REFR", Subrecord("XLIG", Light)), f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            var l = refr.Lighting!;
            l.Fov90PlusMinus.ShouldBe(BitConverter.ToSingle(Hex("68-D0-17-C1")));
            l.LuminousScale.ShouldBe(1.2f);
            l.EndDistanceCap.ShouldBe(3.79f);
            l.NearClipCutOn.ShouldBe(BitConverter.ToSingle(Hex("6A-3D-6A-3F")));
            l.InnerFov.ShouldBe(BitConverter.ToSingle(Hex("FA-A4-D2-41")));
            l.ShadowOffset.ShouldBe(0f);
            l.Unknown1.ShouldBe(0xABABABABu);
            l.Unknown2.ShouldBe((byte)1);
            l.Unknown2Unused.ToArray().ShouldBe(Hex("AB-AB-AB"));
            var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("XLIG")).Content.ToArray().ShouldBe(Hex(Light));
        }
    }

    [Fact]
    public void ExternalEmittance_Fields()
    {
        // XEED of REFR 07CFDF (Starfield.esm): luminance scale 1.7, override enabled, AB AB AB
        const string xeed = "9A-99-D9-3F-01-AB-AB-AB";
        foreach (var refr in ReadBoth<IPlacedObjectGetter>(Record("REFR", Subrecord("XEED", xeed)), f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            var e = refr.ExternalEmittance!;
            e.LuminanceScale.ShouldBe(1.7f);
            e.OverrideEnabled.ShouldBeTrue();
            e.OverrideEnabledUnused.ToArray().ShouldBe(Hex("AB-AB-AB"));
            var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("XEED")).Content.ToArray().ShouldBe(Hex(xeed));
        }
    }

    [Fact]
    public void ReflectionProbe_OffsetIntensity()
    {
        foreach (var refr in ReadBoth<IPlacedObjectGetter>(Record("REFR", Hex(Probe)), f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            var probe = refr.VolumeReflectionProbeOffsetIntensity!;
            probe.Offset.X.ShouldBe(-3f);
            probe.Offset.Y.ShouldBe(-3f);
            probe.Offset.Z.ShouldBe(-2f);
            probe.Intensity.ShouldBe(1f);
            var written = GameConstants.Starfield.MajorRecord(Write(w => refr.WriteToBinary(w)));
            // Only XVOI: Mutagen writes XLYR after XVL2/XVOI, where this record has it before (field order, not part of this change).
            written.FindSubrecord(new RecordType("XVOI")).Content.ToArray().ShouldBe(Hex("00-00-40-C0-00-00-40-C0-00-00-00-C0-00-00-80-3F"));
        }
    }
}
