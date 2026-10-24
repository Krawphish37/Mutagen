using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Shouldly;
using Xunit;
using F3 = Mutagen.Bethesda.Fallout3;
using F4 = Mutagen.Bethesda.Fallout4;
using Ob = Mutagen.Bethesda.Oblivion;
using Sk = Mutagen.Bethesda.Skyrim;
using TempFile = Noggog.IO.TempFile;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

// A Global whose Data is null is written without an FLTV subrecord (Data is nullable).
// The full importer used to require FLTV (GlobalCustomParsing.Create), so a plugin
// written by Mutagen's own writer could not be read back by Mutagen's own full importer,
// while the overlay read it fine.
public class GlobalMissingFltvTests
{
    private static TempFile GetTempFile() => new(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");

    private static ModPath Write(IModGetter mod, TempFile tmp)
    {
        var path = new ModPath(mod.ModKey, tmp.File.Path);
        mod.BeginWrite
            .ToPath(path)
            .WithNoLoadOrder()
            .NoModKeySync()
            .Write();
        return path;
    }

    [Fact]
    public void Skyrim_NullData_RoundTrips()
    {
        using var tmp = GetTempFile();
        var mod = new Sk.SkyrimMod(ModKey.FromNameAndExtension("GlobalMissingFltv.esp"), Sk.SkyrimRelease.SkyrimSE);
        var shortNull = new Sk.GlobalShort(mod) { EditorID = "ShortNull" };
        var intNull = new Sk.GlobalInt(mod) { EditorID = "IntNull" };
        var floatNull = new Sk.GlobalFloat(mod) { EditorID = "FloatNull" };
        var shortFive = new Sk.GlobalShort(mod) { EditorID = "ShortFive", Data = 5 };
        mod.Globals.Add(shortNull);
        mod.Globals.Add(intNull);
        mod.Globals.Add(floatNull);
        mod.Globals.Add(shortFive);
        var path = Write(mod, tmp);

        var full = Sk.SkyrimMod.CreateFromBinary(path, Sk.SkyrimRelease.SkyrimSE);
        full.Globals.Count.ShouldBe(4);
        ((Sk.GlobalShort)full.Globals[shortNull.FormKey]).Data.ShouldBeNull();
        ((Sk.GlobalInt)full.Globals[intNull.FormKey]).Data.ShouldBeNull();
        ((Sk.GlobalFloat)full.Globals[floatNull.FormKey]).Data.ShouldBeNull();
        ((Sk.GlobalShort)full.Globals[shortFive.FormKey]).Data.ShouldBe((short)5);

        using var overlay = Sk.SkyrimMod.CreateFromBinaryOverlay(path, Sk.SkyrimRelease.SkyrimSE);
        ((Sk.IGlobalShortGetter)overlay.Globals[shortNull.FormKey]).Data.ShouldBeNull();
        ((Sk.IGlobalIntGetter)overlay.Globals[intNull.FormKey]).Data.ShouldBeNull();
        ((Sk.IGlobalFloatGetter)overlay.Globals[floatNull.FormKey]).Data.ShouldBeNull();
        ((Sk.IGlobalShortGetter)overlay.Globals[shortFive.FormKey]).Data.ShouldBe((short)5);
    }

    [Fact]
    public void Fallout4_NullData_RoundTrips()
    {
        using var tmp = GetTempFile();
        var mod = new F4.Fallout4Mod(ModKey.FromNameAndExtension("GlobalMissingFltv.esp"), F4.Fallout4Release.Fallout4);
        var shortNull = new F4.GlobalShort(mod) { EditorID = "ShortNull" };
        var intNull = new F4.GlobalInt(mod) { EditorID = "IntNull" };
        var floatNull = new F4.GlobalFloat(mod) { EditorID = "FloatNull" };
        var boolNull = new F4.GlobalBool(mod) { EditorID = "BoolNull" };
        var intSeven = new F4.GlobalInt(mod) { EditorID = "IntSeven", Data = 7 };
        mod.Globals.Add(shortNull);
        mod.Globals.Add(intNull);
        mod.Globals.Add(floatNull);
        mod.Globals.Add(boolNull);
        mod.Globals.Add(intSeven);
        var path = Write(mod, tmp);

        var full = F4.Fallout4Mod.CreateFromBinary(path, F4.Fallout4Release.Fallout4);
        full.Globals.Count.ShouldBe(5);
        ((F4.GlobalShort)full.Globals[shortNull.FormKey]).Data.ShouldBeNull();
        ((F4.GlobalInt)full.Globals[intNull.FormKey]).Data.ShouldBeNull();
        ((F4.GlobalFloat)full.Globals[floatNull.FormKey]).Data.ShouldBeNull();
        ((F4.GlobalBool)full.Globals[boolNull.FormKey]).Data.ShouldBeNull();
        ((F4.GlobalInt)full.Globals[intSeven.FormKey]).Data.ShouldBe(7);

        using var overlay = F4.Fallout4Mod.CreateFromBinaryOverlay(path, F4.Fallout4Release.Fallout4);
        ((F4.IGlobalBoolGetter)overlay.Globals[boolNull.FormKey]).Data.ShouldBeNull();
        ((F4.IGlobalIntGetter)overlay.Globals[intSeven.FormKey]).Data.ShouldBe(7);
    }

    [Fact]
    public void Fallout3_NullData_RoundTrips()
    {
        using var tmp = GetTempFile();
        var mod = new F3.Fallout3Mod(ModKey.FromNameAndExtension("GlobalMissingFltv.esp"), F3.Fallout3Release.Fallout3);
        var shortNull = new F3.GlobalShort(mod) { EditorID = "ShortNull" };
        var floatNull = new F3.GlobalFloat(mod) { EditorID = "FloatNull" };
        var intSeven = new F3.GlobalInt(mod) { EditorID = "IntSeven", Data = 7 };
        mod.Globals.Add(shortNull);
        mod.Globals.Add(floatNull);
        mod.Globals.Add(intSeven);
        var path = Write(mod, tmp);

        var full = F3.Fallout3Mod.CreateFromBinary(path, F3.Fallout3Release.Fallout3);
        full.Globals.Count.ShouldBe(3);
        ((F3.GlobalShort)full.Globals[shortNull.FormKey]).Data.ShouldBeNull();
        ((F3.GlobalFloat)full.Globals[floatNull.FormKey]).Data.ShouldBeNull();
        ((F3.GlobalInt)full.Globals[intSeven.FormKey]).Data.ShouldBe(7);
    }

    [Fact]
    public void Oblivion_NullData_RoundTrips()
    {
        using var tmp = GetTempFile();
        var mod = new Ob.OblivionMod(ModKey.FromNameAndExtension("GlobalMissingFltv.esp"), Ob.OblivionRelease.Oblivion);
        var shortNull = new Ob.GlobalShort(mod) { EditorID = "ShortNull" };
        var floatNull = new Ob.GlobalFloat(mod) { EditorID = "FloatNull" };
        var intSeven = new Ob.GlobalInt(mod) { EditorID = "IntSeven", Data = 7 };
        mod.Globals.Add(shortNull);
        mod.Globals.Add(floatNull);
        mod.Globals.Add(intSeven);
        var path = Write(mod, tmp);

        var full = Ob.OblivionMod.CreateFromBinary(path, Ob.OblivionRelease.Oblivion);
        full.Globals.Count.ShouldBe(3);
        ((Ob.GlobalShort)full.Globals[shortNull.FormKey]).Data.ShouldBeNull();
        ((Ob.GlobalFloat)full.Globals[floatNull.FormKey]).Data.ShouldBeNull();
        ((Ob.GlobalInt)full.Globals[intSeven.FormKey]).Data.ShouldBe(7);
    }
}
