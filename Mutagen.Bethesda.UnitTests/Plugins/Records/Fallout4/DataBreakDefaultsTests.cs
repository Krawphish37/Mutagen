using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Shouldly;
using Xunit;
using TempFile = Noggog.IO.TempFile;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Fallout4;

// A data subrecord that ends at a break leaves the fields after it out. The overlay reads them as their declared
// defaults; the full import left them at 0, because Loqui drops the initializer of a field with a set hook (the
// break flags). Fallout4.esm has 8 weathers with a short FNAM (e.g. 171621:Fallout4.esm): FogDistanceDayFarHeightRange
// read 10000 from the overlay and 0 from the full import, so the two weren't Equal (M5).
public class DataBreakDefaultsTests
{
    [Fact]
    public void ShortFnam_FullImportReadsDeclaredDefaults_LikeTheOverlay()
    {
        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var mod = new Fallout4Mod(ModKey.FromNameAndExtension("DataBreakDefaults.esp"), Fallout4Release.Fallout4);
        var weather = mod.Weather.AddNew("ShortFnam");
        weather.FogDistanceDayNear = 1;
        // Ends at the last break, as those weathers do (an earlier break sets every later break flag in the overlay, a separate issue)
        weather.FNAMDataTypeState |= Weather.FNAMDataType.Break1;
        var path = new ModPath(mod.ModKey, tmp.File.Path);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();

        var full = Fallout4Mod.CreateFromBinary(path, Fallout4Release.Fallout4).Weather[weather.FormKey];
        using var overlayMod = Fallout4Mod.CreateFromBinaryOverlay(path, Fallout4Release.Fallout4);
        var overlay = overlayMod.Weather[weather.FormKey];

        full.FNAMDataTypeState.ShouldBe(Weather.FNAMDataType.Break1);
        overlay.FNAMDataTypeState.ShouldBe(Weather.FNAMDataType.Break1);
        overlay.FogDistanceDayFarHeightRange.ShouldBe(10000f);
        full.FogDistanceDayFarHeightRange.ShouldBe(10000f);
        full.FogDistanceNightFarHeightRange.ShouldBe(10000f);
        full.FogDistanceDayFarHeightMid.ShouldBe(0f);
        full.Equals(overlay).ShouldBeTrue();
    }
}
