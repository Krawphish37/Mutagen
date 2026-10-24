using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins;
using Shouldly;
using Xunit;
using TempFile = Noggog.IO.TempFile;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Skyrim;

// GameSettingBool.Data is a bool?. A GMST written without a DATA subrecord should read
// back as null from both the full import and the binary overlay. The overlay's custom
// getter returned false instead, because its conditional was typed bool.
public class GameSettingBoolDataTests
{
    private static SkyrimMod WriteModWithGameSettingBool(TempFile tmp, bool? value, out FormKey formKey)
    {
        var mod = new SkyrimMod(ModKey.FromNameAndExtension("GameSettingBoolData.esp"), SkyrimRelease.SkyrimSE);
        var gmst = new GameSettingBool(mod.GetNextFormKey(), SkyrimRelease.SkyrimSE)
        {
            Data = value,
            EditorID = "bGameSettingBoolData",
        };
        mod.GameSettings.Add(gmst);
        formKey = gmst.FormKey;

        var path = new ModPath(mod.ModKey, tmp.File.Path);
        mod.BeginWrite
            .ToPath(path)
            .WithNoLoadOrder()
            .NoModKeySync()
            .Write();
        return mod;
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public void WriteThenReadBinary_RoundTripsData(bool? value)
    {
        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var written = WriteModWithGameSettingBool(tmp, value, out var formKey);
        var path = new ModPath(written.ModKey, tmp.File.Path);

        var reloaded = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE);
        var reloadedGmst = (GameSettingBool)reloaded.GameSettings.Records.First(g => g.FormKey == formKey);

        reloadedGmst.Data.ShouldBe(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public void WriteThenReadBinaryOverlay_RoundTripsData(bool? value)
    {
        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var written = WriteModWithGameSettingBool(tmp, value, out var formKey);
        var path = new ModPath(written.ModKey, tmp.File.Path);

        using var reloaded = SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
        var reloadedGmst = (IGameSettingBoolGetter)reloaded.GameSettings.Records.First(g => g.FormKey == formKey);

        reloadedGmst.Data.ShouldBe(value);
        ((GameSettingBool)reloadedGmst.DeepCopy()).Data.ShouldBe(value);
    }
}