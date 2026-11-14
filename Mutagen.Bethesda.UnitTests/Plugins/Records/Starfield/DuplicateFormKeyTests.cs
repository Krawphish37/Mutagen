using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// MissionsGalore.esm has two masters (Starfield.esm, ShipTerminals.esm), so its own records are index 02; but 219 of
// them are stored with index 03, past the master list, which also means the plugin itself. GLOB 02000867
// (ShipTerminalMod_Show_ModSettings_MissionsGalore, 1.0) and GLOB 03000867
// (MissionsGalore_Cargo02Local01_TimeRemainingDays, 120.0) both read as 000867:MissionsGalore.esm, and both readers
// threw ("Two records with the same FormKey"). The game loads records in file order, so it ends up with the later one.
// Now the group holds the later one (in its own place), the earlier one is kept aside and written back where it was,
// and index-03 headers are written as read. The plugin below is MissionsGalore's real TES4 and six of its GLOBs: an
// override of a Starfield.esm global, 02000867, 03000801, 03000866, 03000867, 03000868.
public class DuplicateFormKeyTests
{
    private static readonly ModKey Plugin = ModKey.FromFileName("MissionsGalore.esm");
    private const string Tes4 = "544553345300000001000000000000000000000046020000484544520C008FC2753F9F010000E20800FF4D4153540E00537461726669656C642E65736D004D4153541200536869705465726D696E616C732E65736D00424E414D05004D61696E00494E4343040000000000";
    private static readonly string[] Globals = { "474C4F42320000004000000022143900000000002B0200004544494422004D425F4164646974696F6E616C4D697373696F6E734368616E63655F436172676F00464C545604000000C842", "474C4F42400000004000000067080002000000002B020000454449443000536869705465726D696E616C4D6F645F53686F775F4D6F6453657474696E67735F4D697373696F6E7347616C6F726500464C545604000000803F", "474C4F423D0000000000000001080003000000002E020000454449442D004D697373696F6E7347616C6F72655F426F756E7479537061636530324661725F52657761726441637475616C00464C5456040000000000", "474C4F423E0000000000000066080003000000002E020000454449442E004D697373696F6E7347616C6F72655F436172676F30324C6F63616C30315F54696D65486F75727341637475616C00464C545604000000F042", "474C4F42400000000000000067080003000000002E0200004544494430004D697373696F6E7347616C6F72655F436172676F30324C6F63616C30315F54696D6552656D61696E696E674461797300464C545604000000F042", "474C4F42410000000000000068080003000000002E0200004544494431004D697373696F6E7347616C6F72655F436172676F30324C6F63616C30315F54696D6552656D61696E696E67486F75727300464C545604000000F042" };

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    private static byte[] File()
    {
        var records = Globals.SelectMany(Hex).ToArray();
        var group = new MemoryStream();
        group.Write("GRUP"u8);
        group.Write(BitConverter.GetBytes(24 + records.Length));
        group.Write("GLOB"u8);
        group.Write(BitConverter.GetBytes(0));
        group.Write(new byte[8]);
        group.Write(records);
        return Hex(Tes4).Concat(group.ToArray()).ToArray();
    }

    private sealed record Setup(MockFileSystem Fs, string Path, LoadOrder<IModMasterStyledGetter> Lo);

    private static Setup Make()
    {
        var fs = new MockFileSystem();
        var path = Path.Combine("C:", "Data", Plugin.FileName);
        fs.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        fs.File.WriteAllBytes(path, File());
        var lo = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(ModKey.FromFileName("Starfield.esm"), MasterStyle.Full),
            new KeyedMasterStyle(ModKey.FromFileName("ShipTerminals.esm"), MasterStyle.Full),
            new KeyedMasterStyle(Plugin, MasterStyle.Full),
        });
        return new Setup(fs, path, lo);
    }

    [Fact]
    public void BothReaders_KeepTheLaterRecord_AndWriteTheFileBack()
    {
        var s = Make();
        var found = new List<string>();
        var old = DuplicateRecords.Found;
        DuplicateRecords.Found = found.Add;
        try
        {
            var param = new BinaryReadParameters { FileSystem = s.Fs, MasterFlagsLookup = s.Lo, DuplicateFormKeys = DuplicateFormKeyOption.KeepLater };
            var full = StarfieldMod.CreateFromBinary(s.Path, StarfieldRelease.Starfield, param);
            using var overlay = StarfieldMod.CreateFromBinaryOverlay(s.Path, StarfieldRelease.Starfield, param);
            foreach (var mod in new IStarfieldModGetter[] { full, overlay, (IStarfieldModGetter)full.DeepCopy() })
            {
                var key = new FormKey(Plugin, 0x867);
                mod.Globals.Count.ShouldBe(5);
                mod.Globals[key].EditorID.ShouldBe("MissionsGalore_Cargo02Local01_TimeRemainingDays");
                // In file order, the later 000867 in its own place
                mod.Globals.Select(g => g.EditorID).ShouldBe(new[]
                {
                    "MB_AdditionalMissionsChance_Cargo",
                    "MissionsGalore_BountySpace02Far_RewardActual",
                    "MissionsGalore_Cargo02Local01_TimeHoursActual",
                    "MissionsGalore_Cargo02Local01_TimeRemainingDays",
                    "MissionsGalore_Cargo02Local01_TimeRemainingHours",
                });
                var shadowed = mod.GetShadowedDuplicates().ShouldHaveSingleItem();
                shadowed.FormKey.ShouldBe(key);
                shadowed.EditorID.ShouldBe("ShipTerminalMod_Show_ModSettings_MissionsGalore");

                var outPath = Path.Combine("C:", "Out", Plugin.FileName);
                s.Fs.Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                mod.WriteToBinary(outPath, new BinaryWriteParameters
                {
                    FileSystem = s.Fs,
                    MasterFlagsLookup = s.Lo,
                    // ShipTerminals.esm is listed but no record here references it: keep it (C146 does so by default)
                    MastersListContent = MastersListContentOption.NoCheck,
                });
                s.Fs.File.ReadAllBytes(outPath).ShouldBe(File());
            }
            found.Count.ShouldBe(2); // once per reader (the overlay finds it when its group is first read)
        }
        finally
        {
            DuplicateRecords.Found = old;
        }
    }

    // A record whose header FormID has an index past the master list keeps it on write (it means the same record)
    [Fact]
    public void HeaderIndexPastTheMasters_IsWrittenAsRead()
    {
        var s = Make();
        var param = new BinaryReadParameters { FileSystem = s.Fs, MasterFlagsLookup = s.Lo, DuplicateFormKeys = DuplicateFormKeyOption.KeepLater };
        var full = StarfieldMod.CreateFromBinary(s.Path, StarfieldRelease.Starfield, param);
        var record = full.Globals[new FormKey(Plugin, 0x801)];
        var outPath = Path.Combine("C:", "Out2", Plugin.FileName);
        s.Fs.Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        full.WriteToBinary(outPath, new BinaryWriteParameters
        {
            FileSystem = s.Fs, MasterFlagsLookup = s.Lo, MastersListContent = MastersListContentOption.NoCheck,
        });
        var bytes = s.Fs.File.ReadAllBytes(outPath);
        Convert.ToHexString(bytes).ShouldContain("01080003"); // 03000801, as stored
        record.EditorID.ShouldBe("MissionsGalore_BountySpace02Far_RewardActual");
    }
}
