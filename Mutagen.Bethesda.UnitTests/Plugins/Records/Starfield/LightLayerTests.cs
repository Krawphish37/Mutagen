using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A model's light layer (FLLD) is 4 flags in xEdit: Default Light Layer, Helmet Light, Ship Interior, Ship Exterior.
// Mutagen had a plain UInt32 (or raw bytes on LGDI and PROJ, "ModelData" on MATT). All FLLD in Starfield.esm are
// 4 bytes; almost all are 1 (Default), a few 2, 5, 9 and 15.
public class LightLayerTests
{
    // MODL and FLLD of StsGenIntAddOn_Panel_Console01a_NoAnim (STAT 167AB3): Default + Ship Interior
    private const string Modl = "53-74-61-72-53-74-61-74-69-6F-6E-73-2F-47-65-6E-2F-49-6E-74-65-72-69-6F-72-73-2F-41-64-64-4F-6E-73-2F-53-74-73-47-65-6E-49-6E-74-41-64-64-4F-6E-5F-50-61-6E-65-6C-5F-43-6F-6E-73-6F-6C-65-30-31-61-5F-4E-6F-41-6E-69-6D-2E-6E-69-66-00";
    private const string Flld = "05-00-00-00";

    private static IEnumerable<IStaticGetter> Read() =>
        ReadBoth<IStaticGetter>(Record("STAT", Subrecord("MODL", Modl), Subrecord("FLLD", Flld)), f => Static.CreateFromBinary(f), (s, p) => StaticBinaryOverlay.StaticFactory(s, p));

    [Fact]
    public void LightLayer_Reads()
    {
        foreach (var stat in Read())
        {
            stat.Model!.LightLayer.ShouldBe(LightLayer.DefaultLightLayer | LightLayer.ShipInterior);
        }
    }

    [Fact]
    public void LightLayer_WritesSameBytes()
    {
        foreach (var stat in Read())
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => stat.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("FLLD")).Content.ToArray().ShouldBe(Hex(Flld));
        }
    }
}
