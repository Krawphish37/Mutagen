using System.IO.Abstractions.TestingHelpers;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C181: SeparatedMasterPackage had a hard-coded list of 10 official plugins ("Hack until this is better understood")
// whose own FormIDs were written in their own style (FD/FE) when the file couldn't tell. The list is gone; no name
// decides it. With decision A3 (C163) a Small or Medium mod's new own FormIDs take the form of its size (FE + small
// index, FD + medium index), as SF1Edit writes new records; FormIDs read from a file keep their stored form.
public class OwnFormIdDefaultTests
{
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");

    private static uint WrittenOwnFormId(string fileName, bool medium)
    {
        var fs = new MockFileSystem();
        var modKey = ModKey.FromFileName(fileName);
        var mod = new StarfieldMod(modKey, StarfieldRelease.Starfield);
        if (medium) mod.IsMediumMaster = true; else mod.IsSmallMaster = true;
        mod.Keywords.Add(new Keyword(new FormKey(modKey, 0x800), StarfieldRelease.Starfield) { EditorID = "Own" });
        mod.Keywords.Add(new Keyword(new FormKey(StarfieldEsm, 0xABC), StarfieldRelease.Starfield) { EditorID = "Override" });
        var lo = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
        });
        var path = Path.Combine("C:", "Out", fileName);
        fs.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        mod.WriteToBinary(path, new BinaryWriteParameters
        {
            FileSystem = fs,
            MasterFlagsLookup = lo,
            HeaderSizeFlags = HeaderSizeFlagsOption.NoCheck,
        });
        var bytes = fs.File.ReadAllBytes(path);
        // the KYWD group follows the TES4; its records sorted by FormID: the override (00000ABC) first, then the own one
        int p = 24 + BitConverter.ToInt32(bytes, 4) + 24;
        var ids = new List<uint>();
        while (p < bytes.Length)
        {
            ids.Add(BitConverter.ToUInt32(bytes, p + 12));
            p += 24 + BitConverter.ToInt32(bytes, p + 4);
        }
        return ids.Single(x => (x & 0xFFF) == 0x800);
    }

    [Theory]
    [InlineData("SFBGS003.esm", true)]   // was on the list
    [InlineData("OldMars.esm", false)]   // was on the list
    [InlineData("MyPatch.esm", true)]
    [InlineData("MyPatch.esm", false)]
    public void NewMod_OwnFormIds_TakeTheSizesForm_WhateverItsName(string fileName, bool medium)
    {
        // No Small or Medium masters, so the mod's own index in its form is 0: FD00 + 0800 (Medium), FE000 + 800 (Small)
        WrittenOwnFormId(fileName, medium).ShouldBe(medium ? 0xFD000800u : 0xFE000800u);
    }
}
