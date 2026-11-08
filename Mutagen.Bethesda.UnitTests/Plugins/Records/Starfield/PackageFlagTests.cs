using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield package flags (PKDT) per xEdit's Starfield definitions: After Combat Reset 0x20, Low Priority 0x800,
// Disable Headtracking 0x10000, Disable Advanced Traversals 0x80000, Weapon Drawn: Ready 0x20000000 (Fallout 4's
// Wear Sleep Outfit, which Mutagen wrote as 2000_0000 without 0x), Group Package 0x40000000, Weapon Drawn: Alert
// 0x80000000; interrupt flag React To Warn 0x0800. Starfield.esm: 75, 115, 74, 76 and 2,624 packages.
public class PackageFlagTests
{
    private static IEnumerable<IPackageGetter> ReadPackage(string pkdt) =>
        ReadBoth<IPackageGetter>(Record("PACK", Subrecord("PKDT", pkdt)),
            f => Package.CreateFromBinary(f),
            (s, p) => PackageBinaryOverlay.PackageFactory(s, p));

    [Theory]
    [InlineData("00-00-90-20-12-00-02-FF-70-FA-00-00", Package.Flag.WeaponDrawnReady | Package.Flag.IgnoreCombat | Package.Flag.WeaponDrawn)] // PACK 32985A
    [InlineData("00-00-80-80-13-00-02-1F-FF-FE-00-00", Package.Flag.WeaponDrawnAlert | Package.Flag.WeaponDrawn)] // PACK 3BB029
    [InlineData("00-20-00-40-12-00-01-00-FF-FE-00-00", Package.Flag.GroupPackage | Package.Flag.PreferredSpeed)] // PACK 361C0F
    [InlineData("00-00-08-00-12-00-02-00-FF-FE-00-00", Package.Flag.DisableAdvancedTraversals)] // PACK 3E2D83
    public void Flags_Read(string pkdt, Package.Flag expected)
    {
        foreach (var package in ReadPackage(pkdt))
        {
            package.Flags.ShouldBe(expected);
        }
    }

    [Fact]
    public void ReactToWarn_Reads()
    {
        // PACK 3FE6CF: interrupt flags 0xFEFF
        foreach (var package in ReadPackage("00-00-00-00-12-00-02-00-FF-FE-00-00"))
        {
            package.InterruptFlags.HasFlag(Package.InterruptFlag.ReactToWarn).ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData(Package.Flag.AfterCombatReset, 0x0000_0020u)]
    [InlineData(Package.Flag.LowPriority, 0x0000_0800u)]
    [InlineData(Package.Flag.DisableHeadtracking, 0x0001_0000u)]
    [InlineData(Package.Flag.WeaponDrawnReady, 0x2000_0000u)]
    [InlineData(Package.Flag.WeaponDrawnAlert, 0x8000_0000u)]
    public void Flag_Writes(Package.Flag flag, uint expected)
    {
        var package = new Package(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield) { Flags = flag };
        var written = GameConstants.Starfield.MajorRecord(Write(w => package.WriteToBinary(w)));
        BitConverter.ToUInt32(written.FindSubrecord(new RecordType("PKDT")).Content.Slice(0, 4)).ShouldBe(expected);
    }
}
