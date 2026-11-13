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

// With the default read and write parameters, a full import wrote compressed records compressed anew, while an overlay
// wrote them back as read: the same data in other bytes wherever the plugin was compressed by another encoder (the
// NPC_ of 8 DarkStar patches, of darkstar.esm). A full import now keeps the compressed streams by default
// (BinaryReadParameters.KeepCompressedRecords), so both readers write the file's own bytes.
// The record: an NPC_ override in darkstarpatch_vigilance.esm (012F57:nfvc0003vigilance.esm), compressed, 300 bytes,
// which Mutagen's compressor encodes in 301.
public class CompressedRecordDefaultWriteTests
{
    private const string VigilanceTes4 = "544553349E00000001010000000000000000000046020000484544520C008FC2753FE603000000080004434E414D080044454641554C54004D4153540E00537461726669656C642E65736D004D4153540D0073666267733030642E65736D004D41535416006E66766330303033766967696C616E63652E65736D004D4153540D006461726B737461722E65736D004D4153540D0073666267733030332E65736D00424E414D05004D61696E00494E4343040000000000";
    private const string VigilanceNpc = "4E50435F2C01000000004400572F01020000000046020000E701000078DA8D8FBB4A03411885CFEC8E9822454AC1D2C64A166F1011CCECCE2A8B6677D91D3668B3040D7121242197DEC2C246C80B58DA58FA085ED30ADA083E8AE8CCA4700A054F71CEC7E1E79F7F7C1E7007E16EE6398EB3966745BBE834BBC7ADF4B4E8E76CD01E0FF3A0EB3687AD9C8D47BD7ED1E98D5880C80DF902FE52C4C521D5C43C37AD6052022E716B2D035802A169C8EA73A85E97814C78FB141F5B0422166C1B2F8315FC43BFCE25722D45B561C1F7A28402D922E2244ECF7167FD4CDD1BFCA0F9ACA6FCD1E89F0C7E36786AF0ABE6A9A7FCCDE8DF67BDABFCCA565ED3FB59C0C53C085DB541908A280638134CBA3CBB840939014070A43F71F16943C286852F959B549DB923697D46F5C69E28C3148F0F04C54D85C0E79E20504F8432E244C7379C03474E";

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    private static readonly LoadOrder<IModMasterStyledGetter> Lo = new(new IModMasterStyledGetter[]
    {
        new KeyedMasterStyle(ModKey.FromFileName("Starfield.esm"), MasterStyle.Full),
        new KeyedMasterStyle(ModKey.FromFileName("sfbgs00d.esm"), MasterStyle.Full),
        new KeyedMasterStyle(ModKey.FromFileName("nfvc0003vigilance.esm"), MasterStyle.Full),
        new KeyedMasterStyle(ModKey.FromFileName("darkstar.esm"), MasterStyle.Full),
        new KeyedMasterStyle(ModKey.FromFileName("sfbgs003.esm"), MasterStyle.Medium),
        new KeyedMasterStyle(ModKey.FromFileName("darkstarpatch_vigilance.esm"), MasterStyle.Small),
    });

    private static byte[] Group(string label, byte[] records)
    {
        var s = new MemoryStream();
        s.Write(Encoding.ASCII.GetBytes("GRUP"));
        s.Write(BitConverter.GetBytes(24 + records.Length));
        s.Write(Encoding.ASCII.GetBytes(label));
        s.Write(BitConverter.GetBytes(0));
        s.Write(new byte[8]);
        s.Write(records);
        return s.ToArray();
    }

    [Fact]
    public void CompressedRecord_DefaultWrite_SameBytesFromBothReaders()
    {
        var fs = new MockFileSystem();
        var path = Path.Combine("C:", "Data", "darkstarpatch_vigilance.esm");
        fs.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var file = Hex(VigilanceTes4).Concat(Group("NPC_", Hex(VigilanceNpc))).ToArray();
        fs.File.WriteAllBytes(path, file);
        var read = new BinaryReadParameters { FileSystem = fs, MasterFlagsLookup = Lo };
        var write = new BinaryWriteParameters { FileSystem = fs, MasterFlagsLookup = Lo };
        var outPath = Path.Combine("C:", "Out", "darkstarpatch_vigilance.esm");
        fs.Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        using (var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, read))
        {
            overlay.WriteToBinary(outPath, write);
        }
        fs.File.ReadAllBytes(outPath).ShouldBe(file);

        StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, read).WriteToBinary(outPath, write);
        fs.File.ReadAllBytes(outPath).ShouldBe(file);

        // Asked not to keep them, the full import compresses anew: same data, other bytes
        var lean = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, read with { KeepCompressedRecords = false });
        lean.WriteToBinary(outPath, write);
        var written = fs.File.ReadAllBytes(outPath);
        written.ShouldNotBe(file);
        StarfieldMod.CreateFromBinary(outPath, StarfieldRelease.Starfield, read).Npcs.Single()
            .Equals(lean.Npcs.Single()).ShouldBeTrue();
    }
}
