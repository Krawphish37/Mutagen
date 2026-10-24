using Mutagen.Bethesda.Plugins;
using Shouldly;
using Xunit;
using TempFile = Noggog.IO.TempFile;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

// Values stored as scaled integers (NPC PcLevelMult as level * 1000, Oblivion armor value
// as armor * 100) were written with a truncating cast. A raw value that is not exactly
// representable once divided (1012 / 1000f * 1000 = 1011.99994) lost one unit on every
// read/write round trip.
public class ScaledValueRoundTripTests
{
    public static readonly TheoryData<ushort> LevelMultRaws = new() { 251, 1000, 1012, 1017, 2500 };

    private static ModPath WritePath(TempFile tmp, ModKey modKey) => new(modKey, tmp.File.Path);

    [Theory]
    [MemberData(nameof(LevelMultRaws))]
    public void StarfieldPcLevelMultRoundTrips(ushort raw)
    {
        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var mod = new Mutagen.Bethesda.Starfield.StarfieldMod(ModKey.FromNameAndExtension("PcLevelMult.esp"), Mutagen.Bethesda.Starfield.StarfieldRelease.Starfield);
        var npc = mod.Npcs.AddNew();
        npc.Level = new Mutagen.Bethesda.Starfield.PcLevelMult() { LevelMult = raw / 1000f };
        var path = WritePath(tmp, mod.ModKey);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();

        var reloaded = Mutagen.Bethesda.Starfield.StarfieldMod.CreateFromBinary(path, Mutagen.Bethesda.Starfield.StarfieldRelease.Starfield);
        reloaded.Npcs.First().Level.ShouldBeOfType<Mutagen.Bethesda.Starfield.PcLevelMult>()
            .LevelMult.ShouldBe(raw / 1000f);
        using var overlay = Mutagen.Bethesda.Starfield.StarfieldMod.CreateFromBinaryOverlay(path, Mutagen.Bethesda.Starfield.StarfieldRelease.Starfield);
        overlay.Npcs.First().Level.ShouldBeAssignableTo<Mutagen.Bethesda.Starfield.IPcLevelMultGetter>()!
            .LevelMult.ShouldBe(raw / 1000f);
    }

    [Theory]
    [MemberData(nameof(LevelMultRaws))]
    public void Fallout4PcLevelMultRoundTrips(ushort raw)
    {
        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var mod = new Mutagen.Bethesda.Fallout4.Fallout4Mod(ModKey.FromNameAndExtension("PcLevelMult.esp"), Mutagen.Bethesda.Fallout4.Fallout4Release.Fallout4);
        var npc = mod.Npcs.AddNew();
        npc.Level = new Mutagen.Bethesda.Fallout4.PcLevelMult() { LevelMult = raw / 1000f };
        var path = WritePath(tmp, mod.ModKey);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();

        var reloaded = Mutagen.Bethesda.Fallout4.Fallout4Mod.CreateFromBinary(path, Mutagen.Bethesda.Fallout4.Fallout4Release.Fallout4);
        reloaded.Npcs.First().Level.ShouldBeOfType<Mutagen.Bethesda.Fallout4.PcLevelMult>()
            .LevelMult.ShouldBe(raw / 1000f);
    }

    [Theory]
    [MemberData(nameof(LevelMultRaws))]
    public void SkyrimPcLevelMultRoundTrips(ushort raw)
    {
        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var mod = new Mutagen.Bethesda.Skyrim.SkyrimMod(ModKey.FromNameAndExtension("PcLevelMult.esp"), Mutagen.Bethesda.Skyrim.SkyrimRelease.SkyrimSE);
        var npc = mod.Npcs.AddNew();
        npc.Configuration.Level = new Mutagen.Bethesda.Skyrim.PcLevelMult() { LevelMult = raw / 1000f };
        var path = WritePath(tmp, mod.ModKey);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();

        var reloaded = Mutagen.Bethesda.Skyrim.SkyrimMod.CreateFromBinary(path, Mutagen.Bethesda.Skyrim.SkyrimRelease.SkyrimSE);
        reloaded.Npcs.First().Configuration.Level.ShouldBeOfType<Mutagen.Bethesda.Skyrim.PcLevelMult>()
            .LevelMult.ShouldBe(raw / 1000f);
    }

    [Fact]
    public void StarfieldLeveledNpcByteChanceNoneWritesNearestValue()
    {
        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var mod = new Mutagen.Bethesda.Starfield.StarfieldMod(ModKey.FromNameAndExtension("ChanceNone.esp"), Mutagen.Bethesda.Starfield.StarfieldRelease.Starfield);
        var lvln = mod.LeveledNpcs.AddNew();
        // Below this form version the chance is stored as a byte out of 255
        lvln.FormVersion = Mutagen.Bethesda.Starfield.LeveledNpcBinaryCreateTranslation.FloatChanceNoneVersion - 1;
        // 0.29 * 255 = 73.95
        lvln.ChanceNone = 0.29f;
        var path = WritePath(tmp, mod.ModKey);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();

        var reloaded = Mutagen.Bethesda.Starfield.StarfieldMod.CreateFromBinary(path, Mutagen.Bethesda.Starfield.StarfieldRelease.Starfield);
        reloaded.LeveledNpcs.First().ChanceNone.ShouldBe(74 / 255f);
    }

    [Theory]
    [InlineData((ushort)53)]
    [InlineData((ushort)59)]
    [InlineData((ushort)105)]
    [InlineData((ushort)100)]
    public void OblivionArmorValueRoundTrips(ushort raw)
    {
        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var mod = new Mutagen.Bethesda.Oblivion.OblivionMod(ModKey.FromNameAndExtension("ArmorValue.esp"), Mutagen.Bethesda.Oblivion.OblivionRelease.Oblivion);
        var armor = mod.Armors.AddNew();
        armor.Data = new Mutagen.Bethesda.Oblivion.ArmorData() { ArmorValue = raw / 100f };
        var path = WritePath(tmp, mod.ModKey);
        mod.BeginWrite.ToPath(path).WithNoLoadOrder().NoModKeySync().Write();

        var reloaded = Mutagen.Bethesda.Oblivion.OblivionMod.CreateFromBinary(path, Mutagen.Bethesda.Oblivion.OblivionRelease.Oblivion);
        reloaded.Armors.First().Data!.ArmorValue.ShouldBe(raw / 100f);
    }
}
