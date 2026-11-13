using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Cache.Internals;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing.AutoData;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Cache;

// C177: an untyped link lookup (a FormKey without a record type) cached every record of every plugin: 14-16.5 s and
// 8.4 GB for Starfield.esm. On overlays it now finds the record's type from the file's record headers and looks up only
// that type. These check it finds the same records: top-level, nested (a placed object in a cell), a type read as
// several classes (game settings), one missing; and in a load order, the winning override.
#pragma warning disable CS0618 // the untyped lookups are marked "last resort"
public class UntypedLookupThroughRecordTypesTests
{
    private static async Task<string> Write(IFileSystem fs, DirectoryPath dir, StarfieldMod mod, StarfieldMod? master = null)
    {
        var path = Path.Combine(dir, mod.ModKey.FileName);
        var write = mod.BeginWrite.ToPath(path);
        var withLo = master == null ? write.WithNoLoadOrder() : write.WithLoadOrder(master);
        await withLo.NoModKeySync().WithFileSystem(fs).WriteAsync();
        return path;
    }

    [Theory, MutagenAutoData]
    public async Task Overlay_UntypedLookup_FindsEveryKindOfRecord(IFileSystem fileSystem, DirectoryPath existingTempDir)
    {
        var mod = new StarfieldMod(new ModKey("UntypedLookup", ModType.Master), StarfieldRelease.Starfield);
        var keyword = mod.Keywords.AddNew("TestKeyword");
        var weapon = mod.Weapons.AddNew("TestWeapon");
        var setting = new GameSettingFloat(mod, "fTestSetting") { Data = 1.5f };
        mod.GameSettings.Add(setting);
        var cell = new Cell(mod) { EditorID = "TestCell" };
        var placed = new PlacedObject(mod) { EditorID = "TestPlaced" };
        cell.Temporary.Add(placed);
        mod.Cells.Add(new CellBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellBlock, SubBlocks = { new CellSubBlock { BlockNumber = 0, GroupType = GroupTypeEnum.InteriorCellSubBlock, Cells = { cell } } } });
        var path = await Write(fileSystem, existingTempDir, mod);

        using var overlay = StarfieldMod.Create(StarfieldRelease.Starfield).FromPath(path).WithNoLoadOrder().WithFileSystem(fileSystem).Construct();
        ModRecordIndex.TryGet(overlay, out _).ShouldBeTrue();
        var cache = overlay.ToImmutableLinkCache();
        foreach (var (formKey, getter) in new (FormKey, Type)[]
                 {
                     (keyword.FormKey, typeof(IKeywordGetter)), (weapon.FormKey, typeof(IWeaponGetter)),
                     (setting.FormKey, typeof(IGameSettingFloatGetter)), (cell.FormKey, typeof(ICellGetter)),
                     (placed.FormKey, typeof(IPlacedObjectGetter)),
                 })
        {
            cache.TryResolve(formKey, out var rec).ShouldBeTrue(formKey.ToString());
            rec!.FormKey.ShouldBe(formKey);
            getter.IsInstanceOfType(rec).ShouldBeTrue(getter.Name);
            cache.TryResolveIdentifier(formKey, out string? edid).ShouldBeTrue();
            edid.ShouldBe(rec.EditorID);
        }
        cache.TryResolve(new FormKey(mod.ModKey, 0xABCDEF), out _).ShouldBeFalse();

        // A mod in memory (not registered) still answers through the cache of every record
        ModRecordIndex.TryGet(mod, out _).ShouldBeFalse();
        mod.ToImmutableLinkCache().TryResolve(placed.FormKey, out var inMemory).ShouldBeTrue();
        inMemory!.FormKey.ShouldBe(placed.FormKey);
    }

    [Theory, MutagenAutoData]
    public async Task LoadOrder_UntypedLookup_ReturnsTheWinningOverride(IFileSystem fileSystem, DirectoryPath existingTempDir)
    {
        var master = new StarfieldMod(new ModKey("UntypedMaster", ModType.Master), StarfieldRelease.Starfield);
        var keyword = master.Keywords.AddNew("MasterKeyword");
        var other = master.Keywords.AddNew("OtherKeyword");
        var patch = new StarfieldMod(new ModKey("UntypedPatch", ModType.Plugin), StarfieldRelease.Starfield);
        patch.Keywords.GetOrAddAsOverride(keyword).EditorID = "PatchedKeyword";
        var patchWeapon = patch.Weapons.AddNew("PatchWeapon");
        await Write(fileSystem, existingTempDir, master);
        var patchPath = await Write(fileSystem, existingTempDir, patch, master);

        using var masterOverlay = StarfieldMod.Create(StarfieldRelease.Starfield)
            .FromPath(Path.Combine(existingTempDir, master.ModKey.FileName)).WithNoLoadOrder().WithFileSystem(fileSystem).Construct();
        using var patchOverlay = StarfieldMod.Create(StarfieldRelease.Starfield)
            .FromPath(patchPath).WithLoadOrder(master.ModKey, patch.ModKey).WithDataFolder(existingTempDir).WithFileSystem(fileSystem).Construct();
        var cache = new IStarfieldModGetter[] { masterOverlay, patchOverlay }.ToImmutableLinkCache();

        cache.TryResolve(keyword.FormKey, out var winner).ShouldBeTrue();
        winner!.EditorID.ShouldBe("PatchedKeyword");
        cache.TryResolve(other.FormKey, out var fromMaster).ShouldBeTrue();
        fromMaster!.EditorID.ShouldBe("OtherKeyword");
        cache.TryResolve(patchWeapon.FormKey, out var fromPatch).ShouldBeTrue();
        fromPatch.ShouldBeAssignableTo<IWeaponGetter>();
        cache.TryResolve(keyword.FormKey, out var origin, ResolveTarget.Origin).ShouldBeTrue();
        origin!.EditorID.ShouldBe("MasterKeyword");
        cache.TryResolve(new FormKey(master.ModKey, 0xABCDEF), out _).ShouldBeFalse();
    }
}
#pragma warning restore CS0618
