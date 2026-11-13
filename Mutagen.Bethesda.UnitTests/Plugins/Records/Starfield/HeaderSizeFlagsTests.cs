using System.IO.Abstractions.TestingHelpers;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C160: BinaryWriteParameters.HeaderSizeFlags, a Starfield plugin's size flags (Light 0x100, Medium 0x400, neither =
// Full) and Master flag (0x1) on write. Auto (the default) keeps the size the mod holds (as set or as read) and raises it
// only when the mod's own records don't fit (Light up to 0xFFF, Medium up to 0xFFFF: the FormID compaction ranges, also
// what xEdit checks); a new .esm/.esl gets the Master flag. Smallest picks the smallest size that fits (for new patches:
// Master + Light). NoCheck writes the flags as set; Small/Medium/Full force a size. Other games: Auto = NoCheck.
// Auto never lowers a size: a mod written smaller than it holds would no longer match the in-memory mod that callers pass
// as a master when writing its dependents.
public class HeaderSizeFlagsTests
{
    private const int Master = 0x1;
    private const int Light = 0x100;
    private const int Medium = 0x400;
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");

    private sealed record Setup(MockFileSystem Fs, ModKey Plugin, LoadOrder<IModMasterStyledGetter> Lo)
    {
        public string OutPath => Path.Combine("C:", "Out", Plugin.FileName);
    }

    private static Setup Make(string fileName, MasterStyle style = MasterStyle.Full)
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory(Path.Combine("C:", "Out"));
        var plugin = ModKey.FromFileName(fileName);
        var lo = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
            new KeyedMasterStyle(plugin, style),
        });
        return new Setup(fs, plugin, lo);
    }

    private static StarfieldMod NewMod(ModKey modKey, params uint[] ownIds)
    {
        var mod = new StarfieldMod(modKey, StarfieldRelease.Starfield);
        foreach (var id in ownIds)
        {
            mod.Keywords.Add(new Keyword(new FormKey(modKey, id), StarfieldRelease.Starfield) { EditorID = $"Kw{id:X}" });
        }
        // An override of a Starfield.esm record, so the mod has a master
        mod.Keywords.Add(new Keyword(new FormKey(StarfieldEsm, 0xABC), StarfieldRelease.Starfield) { EditorID = "StarfieldKeyword" });
        return mod;
    }

    // Writes the mod and returns the written header flags
    private static int Write(Setup s, IStarfieldModGetter mod, HeaderSizeFlagsOption? option = null)
    {
        var param = new BinaryWriteParameters
        {
            FileSystem = s.Fs,
            MasterFlagsLookup = s.Lo,
            MastersListOrdering = new MastersListOrderingByLoadOrder(new[] { StarfieldEsm, s.Plugin }),
        };
        if (option is { } o) param = param with { HeaderSizeFlags = o };
        mod.WriteToBinary(s.OutPath, param);
        return BitConverter.ToInt32(s.Fs.File.ReadAllBytes(s.OutPath), 8);
    }

    private static StarfieldMod Read(Setup s) =>
        StarfieldMod.CreateFromBinary(s.OutPath, StarfieldRelease.Starfield,
            new BinaryReadParameters { FileSystem = s.Fs, MasterFlagsLookup = s.Lo });

    private static int SizeFlags(int flags) => flags & (Light | Medium);

    [Theory]
    [InlineData(0x900u, Light)]
    [InlineData(0xFFFu, Light)]
    [InlineData(0x1000u, Medium)]
    [InlineData(0xFFFFu, Medium)]
    [InlineData(0x10000u, 0)]
    public void Smallest_NewEsm_GetsMasterAndSmallestSize(uint highestOwnId, int size)
    {
        var s = Make("NewPatch.esm");
        var flags = Write(s, NewMod(s.Plugin, 0x800, highestOwnId), HeaderSizeFlagsOption.Smallest);
        (flags & Master).ShouldBe(Master);
        SizeFlags(flags).ShouldBe(size);
        // and it reads back with its own records where they were
        var read = Read(s);
        read.Keywords.Select(k => k.FormKey).ShouldContain(new FormKey(s.Plugin, highestOwnId));
    }

    [Fact]
    public void Smallest_NewEsp_GetsSizeButNoMaster()
    {
        var s = Make("NewPatch.esp");
        var flags = Write(s, NewMod(s.Plugin, 0x800), HeaderSizeFlagsOption.Smallest);
        (flags & Master).ShouldBe(0);
        SizeFlags(flags).ShouldBe(Light);
    }

    [Fact]
    public void Smallest_OnlyOverrides_IsLight()
    {
        var s = Make("OverridesOnly.esm");
        var flags = Write(s, NewMod(s.Plugin), HeaderSizeFlagsOption.Smallest);
        flags.ShouldBe(Master | Light);
    }

    [Fact]
    public void Smallest_ReplacesTheSizeTheCallerSet()
    {
        var s = Make("Patch.esm");
        var mod = NewMod(s.Plugin, 0x800);
        mod.IsMediumMaster = true;
        SizeFlags(Write(s, mod, HeaderSizeFlagsOption.Smallest)).ShouldBe(Light);
    }

    [Fact]
    public void Auto_NewEsm_GetsMasterAndKeepsItsSize()
    {
        var s = Make("Patch.esm");
        // no size flag: Full, kept (Auto doesn't lower it)
        Write(s, NewMod(s.Plugin, 0x800)).ShouldBe(Master);
        var medium = NewMod(s.Plugin, 0x800);
        medium.IsMediumMaster = true;
        Write(s, medium).ShouldBe(Master | Medium);
        var light = NewMod(s.Plugin, 0x800);
        light.IsSmallMaster = true;
        Write(s, light).ShouldBe(Master | Light);
    }

    [Fact]
    public void Auto_NewEsp_NoMasterFlag()
    {
        var s = Make("Patch.esp");
        Write(s, NewMod(s.Plugin, 0x800)).ShouldBe(0);
    }

    [Fact]
    public void Auto_NewLightMod_ThatDoesNotFit_IsRaised()
    {
        var s = Make("Patch.esm");
        var mod = NewMod(s.Plugin, 0x800, 0x1500);
        mod.IsSmallMaster = true;
        Write(s, mod).ShouldBe(Master | Medium);
        mod.Keywords.Add(new Keyword(new FormKey(s.Plugin, 0x12345), StarfieldRelease.Starfield));
        Write(s, mod).ShouldBe(Master);
    }

    [Fact]
    public void ReadMod_KeepsItsFlags()
    {
        // A Medium master whose records would fit Light: written Medium on purpose, read, written again with defaults
        var s = Make("Medium.esm", MasterStyle.Medium);
        // (an explicit size sets only the size flags)
        Write(s, NewMod(s.Plugin, 0x800), HeaderSizeFlagsOption.Medium).ShouldBe(Medium);
        var first = s.Fs.File.ReadAllBytes(s.OutPath);
        var read = Read(s);
        Write(s, read).ShouldBe(Medium);
        s.Fs.File.ReadAllBytes(s.OutPath).ShouldBe(first);
    }

    [Fact]
    public void ReadModWithoutMasterFlag_StaysWithoutIt()
    {
        var s = Make("Plain.esm");
        Write(s, NewMod(s.Plugin, 0x800), HeaderSizeFlagsOption.NoCheck);
        // flags as the new mod held them: none
        var read = Read(s);
        read.IsMaster.ShouldBeFalse();
        (Write(s, read) & Master).ShouldBe(0);
    }

    [Fact]
    public void ReadLightMod_ThatOutgrowsLight_BecomesMedium()
    {
        var s = Make("Grows.esm", MasterStyle.Small);
        Write(s, NewMod(s.Plugin, 0x800), HeaderSizeFlagsOption.Smallest).ShouldBe(Master | Light);
        var read = Read(s);
        read.Keywords.Add(new Keyword(new FormKey(s.Plugin, 0x1500), StarfieldRelease.Starfield) { EditorID = "Big" });
        var lo = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
            new KeyedMasterStyle(s.Plugin, MasterStyle.Medium),
        });
        var grown = s with { Lo = lo };
        Write(grown, read).ShouldBe(Master | Medium);
        Read(grown).Keywords.Select(k => k.FormKey).ShouldContain(new FormKey(s.Plugin, 0x1500));
    }

    [Fact]
    public void NoCheck_WritesTheFlagsAsSet()
    {
        var s = Make("AsSet.esm");
        var mod = NewMod(s.Plugin, 0x800);
        Write(s, mod, HeaderSizeFlagsOption.NoCheck).ShouldBe(0);
        mod.IsSmallMaster = true;
        mod.Keywords.Add(new Keyword(new FormKey(s.Plugin, 0x1FFF), StarfieldRelease.Starfield));
        // the compaction check still applies to the size as set
        Should.Throw<FormIDCompactionOutOfBoundsException>(() => Write(s, mod, HeaderSizeFlagsOption.NoCheck));
    }

    [Fact]
    public void ExplicitSize_IsForced_AndChecked()
    {
        var s = Make("Forced.esm");
        SizeFlags(Write(s, NewMod(s.Plugin, 0x800), HeaderSizeFlagsOption.Full)).ShouldBe(0);
        SizeFlags(Write(s, NewMod(s.Plugin, 0x800), HeaderSizeFlagsOption.Medium)).ShouldBe(Medium);
        Should.Throw<FormIDCompactionOutOfBoundsException>(() => Write(s, NewMod(s.Plugin, 0x1500), HeaderSizeFlagsOption.Small));
    }

    [Fact]
    public void OtherGames_AutoChangesNothing()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory(Path.Combine("C:", "Out"));
        var modKey = ModKey.FromFileName("SkyrimPatch.esm");
        var mod = new Mutagen.Bethesda.Skyrim.SkyrimMod(modKey, Mutagen.Bethesda.Skyrim.SkyrimRelease.SkyrimSE);
        mod.Keywords.Add(new Mutagen.Bethesda.Skyrim.Keyword(new FormKey(modKey, 0x900), Mutagen.Bethesda.Skyrim.SkyrimRelease.SkyrimSE));
        var path = Path.Combine("C:", "Out", modKey.FileName);
        ((IModGetter)mod).WriteToBinary(path, new BinaryWriteParameters { FileSystem = fs });
        BitConverter.ToInt32(fs.File.ReadAllBytes(path), 8).ShouldBe(0);
    }
}
