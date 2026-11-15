using System.IO.Abstractions.TestingHelpers;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C48: the TES4 header flags xEdit names Optimized (0x10) and Blueprint (0x800) are members of
// StarfieldModHeader.HeaderFlag. The BlueprintShips-*.esm plugins set 0x800 (flags 0x801 or 0x881).
public class ModHeaderFlagTests
{
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");
    private static readonly ModKey Plugin = ModKey.FromFileName("BlueprintTest.esm");
    private static readonly string OutPath = Path.Combine("C:", "Out", Plugin.FileName);

    [Fact]
    public void Values_MatchXEdit()
    {
        ((int)StarfieldModHeader.HeaderFlag.Optimized).ShouldBe(0x10);
        ((int)StarfieldModHeader.HeaderFlag.Blueprint).ShouldBe(0x800);
    }

    [Theory]
    [InlineData(0x801, "Master, Blueprint")]
    [InlineData(0x881, "Master, Localized, Blueprint")]
    [InlineData(0x11, "Master, Optimized")]
    public void RawFlags_PrintByName(int raw, string expected)
    {
        ((StarfieldModHeader.HeaderFlag)raw).ToString().ShouldBe(expected);
    }

    private static (MockFileSystem Fs, LoadOrder<IModMasterStyledGetter> Lo) Setup()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory(Path.Combine("C:", "Out"));
        var lo = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
            new KeyedMasterStyle(Plugin, MasterStyle.Full),
        });
        return (fs, lo);
    }

    private static StarfieldMod NewMod(StarfieldModHeader.HeaderFlag flags)
    {
        var mod = new StarfieldMod(Plugin, StarfieldRelease.Starfield);
        mod.ModHeader.Flags = flags;
        mod.Keywords.Add(new Keyword(new FormKey(Plugin, 0x800), StarfieldRelease.Starfield) { EditorID = "Kw800" });
        mod.Keywords.Add(new Keyword(new FormKey(StarfieldEsm, 0xABC), StarfieldRelease.Starfield) { EditorID = "StarfieldKeyword" });
        return mod;
    }

    [Theory]
    [InlineData(StarfieldModHeader.HeaderFlag.Master | StarfieldModHeader.HeaderFlag.Blueprint)]
    [InlineData(StarfieldModHeader.HeaderFlag.Master | StarfieldModHeader.HeaderFlag.Optimized)]
    public void Flags_WriteAndReadBack(StarfieldModHeader.HeaderFlag flags)
    {
        var (fs, lo) = Setup();
        NewMod(flags).WriteToBinary(OutPath, new BinaryWriteParameters
        {
            FileSystem = fs,
            MasterFlagsLookup = lo,
            MastersListOrdering = new MastersListOrderingByLoadOrder(new[] { StarfieldEsm, Plugin }),
            HeaderSizeFlags = HeaderSizeFlagsOption.NoCheck,
        });
        BitConverter.ToInt32(fs.File.ReadAllBytes(OutPath), 8).ShouldBe((int)flags);

        var readParams = new BinaryReadParameters { FileSystem = fs, MasterFlagsLookup = lo };
        StarfieldMod.CreateFromBinary(OutPath, StarfieldRelease.Starfield, readParams)
            .ModHeader.Flags.ShouldBe(flags);
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(OutPath, StarfieldRelease.Starfield, readParams);
        overlay.ModHeader.Flags.ShouldBe(flags);
    }

    [Fact]
    public void Blueprint_KeptWhenSizeFlagsAreSet()
    {
        // HeaderSizeFlags Auto (the default) sets the size and Master flags and keeps the other bits
        var (fs, lo) = Setup();
        NewMod(StarfieldModHeader.HeaderFlag.Blueprint).WriteToBinary(OutPath, new BinaryWriteParameters
        {
            FileSystem = fs,
            MasterFlagsLookup = lo,
            MastersListOrdering = new MastersListOrderingByLoadOrder(new[] { StarfieldEsm, Plugin }),
        });
        var flags = (StarfieldModHeader.HeaderFlag)BitConverter.ToInt32(fs.File.ReadAllBytes(OutPath), 8);
        flags.HasFlag(StarfieldModHeader.HeaderFlag.Blueprint).ShouldBeTrue();
    }
}
