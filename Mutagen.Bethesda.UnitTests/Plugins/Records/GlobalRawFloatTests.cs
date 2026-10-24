using Mutagen.Bethesda.Plugins;
using Shouldly;
using Xunit;
using TempFile = Noggog.IO.TempFile;
using Sk = Mutagen.Bethesda.Skyrim;
using F4 = Mutagen.Bethesda.Fallout4;
using F3 = Mutagen.Bethesda.Fallout3;
using Ob = Mutagen.Bethesda.Oblivion;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

// RawFloat on GlobalShort / GlobalInt / Fallout4 GlobalBool used to return 0 instead of null
// when Data was unset, unlike GlobalFloat / GlobalUnknown.
public class GlobalRawFloatTests
{
    private static ModPath TempPath(TempFile tmp, ModKey modKey) => new(modKey, tmp.File.Path);

    [Fact]
    public void Skyrim_UnsetData_RawFloatIsNull()
    {
        var mod = new Sk.SkyrimMod(ModKey.FromFileName("GlobalRawFloat.esp"), Sk.SkyrimRelease.SkyrimSE);
        new Sk.GlobalShort(mod).RawFloat.ShouldBeNull();
        new Sk.GlobalInt(mod).RawFloat.ShouldBeNull();
        new Sk.GlobalFloat(mod).RawFloat.ShouldBeNull();
        new Sk.GlobalShort(mod) { Data = 7 }.RawFloat.ShouldBe(7f);
        new Sk.GlobalInt(mod) { Data = -3 }.RawFloat.ShouldBe(-3f);
    }

    [Fact]
    public void Skyrim_RawFloatSetNull_RoundTrips()
    {
        var mod = new Sk.SkyrimMod(ModKey.FromFileName("GlobalRawFloat.esp"), Sk.SkyrimRelease.SkyrimSE);
        var glob = new Sk.GlobalShort(mod) { RawFloat = 5 };
        glob.RawFloat = null;
        glob.Data.ShouldBeNull();
        glob.RawFloat.ShouldBeNull();
    }

    [Fact]
    public void Fallout4_UnsetData_RawFloatIsNull()
    {
        var mod = new F4.Fallout4Mod(ModKey.FromFileName("GlobalRawFloat.esp"), F4.Fallout4Release.Fallout4);
        new F4.GlobalShort(mod).RawFloat.ShouldBeNull();
        new F4.GlobalInt(mod).RawFloat.ShouldBeNull();
        new F4.GlobalBool(mod).RawFloat.ShouldBeNull();
        new F4.GlobalFloat(mod).RawFloat.ShouldBeNull();
        new F4.GlobalBool(mod) { Data = true }.RawFloat.ShouldBe(1f);
        new F4.GlobalBool(mod) { Data = false }.RawFloat.ShouldBe(0f);
        new F4.GlobalInt(mod) { Data = 12 }.RawFloat.ShouldBe(12f);
    }

    [Fact]
    public void Fallout3_UnsetData_RawFloatIsNull()
    {
        var mod = new F3.Fallout3Mod(ModKey.FromFileName("GlobalRawFloat.esp"), F3.Fallout3Release.Fallout3);
        new F3.GlobalShort(mod).RawFloat.ShouldBeNull();
        new F3.GlobalInt(mod).RawFloat.ShouldBeNull();
        new F3.GlobalFloat(mod).RawFloat.ShouldBeNull();
        new F3.GlobalShort(mod) { Data = 2 }.RawFloat.ShouldBe(2f);
    }

    [Fact]
    public void Oblivion_UnsetData_RawFloatIsNull()
    {
        var mod = new Ob.OblivionMod(ModKey.FromFileName("GlobalRawFloat.esp"), Ob.OblivionRelease.Oblivion);
        new Ob.GlobalShort(mod).RawFloat.ShouldBeNull();
        new Ob.GlobalInt(mod).RawFloat.ShouldBeNull();
        new Ob.GlobalFloat(mod).RawFloat.ShouldBeNull();
        new Ob.GlobalInt(mod) { Data = 4 }.RawFloat.ShouldBe(4f);
    }

    [Fact]
    public void Skyrim_Overlay_UnsetData_RawFloatIsNull()
    {
        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var mod = new Sk.SkyrimMod(ModKey.FromFileName("GlobalRawFloat.esp"), Sk.SkyrimRelease.SkyrimSE);
        mod.Globals.Add(new Sk.GlobalShort(mod) { EditorID = "ShortNull" });
        mod.Globals.Add(new Sk.GlobalInt(mod) { EditorID = "IntNull" });
        mod.Globals.Add(new Sk.GlobalShort(mod) { EditorID = "ShortSet", Data = 7 });
        var path = TempPath(tmp, mod.ModKey);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();

        using var overlay = Sk.SkyrimMod.CreateFromBinaryOverlay(path, Sk.SkyrimRelease.SkyrimSE);
        var globs = overlay.Globals.Cast<Sk.GlobalBinaryOverlay>().ToDictionary(g => g.EditorID!);
        globs["ShortNull"].RawFloat.ShouldBeNull();
        globs["IntNull"].RawFloat.ShouldBeNull();
        globs["ShortSet"].RawFloat.ShouldBe(7f);
    }

    [Fact]
    public void Fallout4_Overlay_UnsetData_RawFloatIsNull()
    {
        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var mod = new F4.Fallout4Mod(ModKey.FromFileName("GlobalRawFloat.esp"), F4.Fallout4Release.Fallout4);
        mod.Globals.Add(new F4.GlobalShort(mod) { EditorID = "ShortNull" });
        mod.Globals.Add(new F4.GlobalInt(mod) { EditorID = "IntNull" });
        mod.Globals.Add(new F4.GlobalBool(mod) { EditorID = "BoolNull" });
        mod.Globals.Add(new F4.GlobalBool(mod) { EditorID = "BoolTrue", Data = true });
        var path = TempPath(tmp, mod.ModKey);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();

        using var overlay = F4.Fallout4Mod.CreateFromBinaryOverlay(path, F4.Fallout4Release.Fallout4);
        var globs = overlay.Globals.Cast<F4.GlobalBinaryOverlay>().ToDictionary(g => g.EditorID!);
        globs["ShortNull"].RawFloat.ShouldBeNull();
        globs["IntNull"].RawFloat.ShouldBeNull();
        globs["BoolNull"].RawFloat.ShouldBeNull();
        globs["BoolTrue"].RawFloat.ShouldBe(1f);
    }

    [Fact]
    public void Fallout3_Overlay_UnsetData_RawFloatIsNull()
    {
        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var mod = new F3.Fallout3Mod(ModKey.FromFileName("GlobalRawFloat.esp"), F3.Fallout3Release.Fallout3);
        mod.Globals.Add(new F3.GlobalShort(mod) { EditorID = "ShortNull" });
        mod.Globals.Add(new F3.GlobalInt(mod) { EditorID = "IntNull" });
        var path = TempPath(tmp, mod.ModKey);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();

        using var overlay = F3.Fallout3Mod.CreateFromBinaryOverlay(path, F3.Fallout3Release.Fallout3);
        var globs = overlay.Globals.Cast<F3.GlobalBinaryOverlay>().ToDictionary(g => g.EditorID!);
        globs["ShortNull"].RawFloat.ShouldBeNull();
        globs["IntNull"].RawFloat.ShouldBeNull();
    }

    [Fact]
    public void Oblivion_Overlay_UnsetData_RawFloatIsNull()
    {
        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var mod = new Ob.OblivionMod(ModKey.FromFileName("GlobalRawFloat.esp"), Ob.OblivionRelease.Oblivion);
        mod.Globals.Add(new Ob.GlobalShort(mod) { EditorID = "ShortNull" });
        mod.Globals.Add(new Ob.GlobalInt(mod) { EditorID = "IntNull" });
        var path = TempPath(tmp, mod.ModKey);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();

        using var overlay = Ob.OblivionMod.CreateFromBinaryOverlay(path, Ob.OblivionRelease.Oblivion);
        var globs = overlay.Globals.Cast<Ob.GlobalBinaryOverlay>().ToDictionary(g => g.EditorID!);
        globs["ShortNull"].RawFloat.ShouldBeNull();
        globs["IntNull"].RawFloat.ShouldBeNull();
    }
}
