using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Strings.DI;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Text stored in a plugin with no strings files (not localized) was decoded by the target language: code page 1252
// for English, "UTF-8, else a code page" for every other. The "else" never ran (Encoding.UTF8 puts U+FFFD in place of
// a byte that isn't UTF-8 instead of throwing), so economy.esm's Samson’s Art (byte 0x92) read "Samson�s Art" in
// German, Japanese, Polish or Chinese, and wrote back as "Samson?s Art". Such text is now read with the encoding writing
// uses for it, whatever the target language, and the fallback encodings use a UTF-8 that throws.
public class NonLocalizedTextDecodingTests
{
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    // "Samson’s Art" as economy.esm stores it (code page 1252: 0x92 for the apostrophe), and the same text in UTF-8
    private static readonly byte[] SamsonCp1252 = Hex("53616D736F6E92732041727400");
    private static readonly byte[] SamsonUtf8 = Encoding.UTF8.GetBytes("Samson’s Art");

    [Fact]
    public void FallbackEncodings_ReadNonUtf8BytesWithTheirCodePage()
    {
        var cp1252 = SamsonCp1252[..^1];
        MutagenEncoding._utf8_1252.GetString(cp1252).ShouldBe("Samson’s Art");
        MutagenEncoding._utf8_1250.GetString(cp1252).ShouldBe("Samson’s Art");
        // Valid UTF-8 still reads as UTF-8
        MutagenEncoding._utf8_1252.GetString(SamsonUtf8).ShouldBe("Samson’s Art");
        MutagenEncoding._utf8_932.GetString(Encoding.UTF8.GetBytes("イーオン")).ShouldBe("イーオン");
        // Shift-JIS bytes (not UTF-8) read with code page 932
        var sjis = CodePagesEncodingProvider.Instance.GetEncoding(932)!.GetBytes("イーオン");
        MutagenEncoding._utf8_932.GetString(sjis).ShouldBe("イーオン");
        // Writing through the fallback encodings is unchanged: UTF-8
        var buffer = new byte[64];
        var n = MutagenEncoding._utf8_1252.GetBytes("Samson’s Art", buffer);
        buffer[..n].ShouldBe(SamsonUtf8);
    }

    // economy.esm: its real header (not localized; masters Starfield.esm, sfbgs00d.esm, SFBGS007.esm) and its MISC
    // City_newatlantis_z_theartcollector_artwork (111EA8:Starfield.esm), FULL "Samson’s Art" in code page 1252
    private const string EconomyTes4 = "544553347D00000001000000000000000000000046020000484544520C008FC2753F5B08000000080002434E414D080044454641554C54004D4153540E00537461726669656C642E65736D004D4153540D0073666267733030642E65736D004D4153540D0053464247533030372E65736D004F4E414D08004CE122004DE12200424E414D05004D61696E00494E4343040000000000";
    private const string EconomyMisc = "4D4953434101000000000000A81E11000000000046020800454449442B00436974795F6E657761746C616E7469735F7A5F746865617274636F6C6C6563746F725F617274776F726B004F424E44180000803CBE00202DBD0000B3BB00803C3E00A08C3D00A0843E4F445459040000000000505454322000000000000000000000000000000000002115020000000000000000000000000046554C4C0D0053616D736F6E927320417274004D4F444C35005365744472657373696E675C436F6E74726162616E645C43425F426C61636B4D61726B6574416E7469717569746965732E6E696600464C4C44040001000000505553482800C74F02DD4E67CBD1A2CECBE319BCDAAB000000000000000000000000000000000000000000000000504453482800D549E3C1F4DA9DB773234AF2E5C3B1A20000000000000000000000000000000000000000000000004441544108009619000000000040";

    // apc_galaxysmartcontainers.esm: its real header and its BOOK APCG_BookVol1 (000A27:apc_galaxysmartcontainers.esm),
    // FULL " Galaxy Handbook — Vol 1: Intro" with an em dash in code page 1252 (0x97)
    private const string ApcTes4 = "544553345C00000001000000000000000000000046020000484544520C008FC2753F0A0B000000080002434E414D080044454641554C54004D4153540E00537461726669656C642E65736D004D4153540D0073666267733030642E65736D00424E414D05004D61696E00494E434304009E000000";
    private const string ApcBook = "424F4F4B5F04000000000000270A00020000000046020000454449440E00415043475F426F6F6B566F6C31004F424E4418008AC0B4BC00A0B8BD0098AFBA763FB63C00E0BF3D6890853E4F44545904000000000046554C4C20002047616C6178792048616E64626F6F6B209720566F6C20313A20496E74726F004D4F444C23005365744472657373696E675C426F6F6B735C426F6F6B4D656469756D30312E6E696600464C4C4404000100000044455343800357454C434F4D4520544F205448452057494C4C4E593420464C59494E4720464143544F524945530D0A0D0A466C79696E6720466163746F72696573207475726E7320796F757220656E7469726520756E69766572736520696E746F206F6E6520636F6E6E65637465642070726F64756374696F6E206E6574776F726B2E0D0A4275696C64206465766963657320616E797768657265202D206F6E20616E7920706C616E65742C20616E79206F7574706F73742C206576656E206F6E20796F75722073686970202D20616E642047616C617879206B65657073207468656D20776F726B696E6720746F676574686572207468726F7567682061207368617265642072656769737472792E0D0A0D0A5748415420544849532053595354454D20494E434C554445530D0A2D20536D61727420436F6E7461696E657273202853437329202D207265736F757263652073746F72616765202B2067616C6178792D7769646520737570706C790D0A2D2020536D6172742046616272696361746F727320284641427329202D204C616E6420616E6420536869702070726F64756374696F6E206F6620416D6D6F2C204D6564732C204368656D732E2020205374616E646172647320616E64205468726F7761626C657320696E636C7564696E6720582D546563682E0D0A2D20536D61727420457874726163746F72732C20477265656E686F75736520616E642052616E63686573202D2061646A75737461626C652D73706565642068617276657374696E6720666F7220706C616E65742D77696465207265736F75726365730D0A2D2047616C6178792D7769646520726F7574696E67202D206E6F20446973747269627574696F6E2043656E74657220616E64206E6F20636172676F206C696E6B732072657175697265640D0A2D20556E6966696564205265706F727473202B2043726564697420436F6E736F6C69646174696F6E0D0A0D0A484F5720544F2055534520544849532048414E44424F4F4B0D0A43686F6F7365206120766F6C756D6520746F206C6561726E3A0D0A2D207768617420656163682064657669636520646F65730D0A2D20686F7720746F20636F6E66696775726520697420656666696369656E746C790D0A2D2062657374206275696C64206F7264657220666F72206661737420726573756C74730D0A2D2074726F75626C6573686F6F74696E6720616E6420706572666F726D616E636520746970730D0A004B53495A0400020000004B57444108007AA411006CC81F004441544108000000000000000000444E414D11000000000000000000000000000000000000434E414D010000";

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

    private static byte[] RecordBytes(byte[] file, string type)
    {
        int p = 24 + BitConverter.ToInt32(file, 4);
        while (p + 24 <= file.Length)
        {
            var sig = Encoding.ASCII.GetString(file, p, 4);
            int size = BitConverter.ToInt32(file, p + 4);
            if (sig == "GRUP") { p += 24; continue; }
            if (sig == type) return file.AsSpan(p, 24 + size).ToArray();
            p += 24 + size;
        }
        throw new InvalidOperationException(type + " not written");
    }

    private static readonly LoadOrder<IModMasterStyledGetter> Lo = new(new IModMasterStyledGetter[]
    {
        new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
        new KeyedMasterStyle(ModKey.FromFileName("sfbgs00d.esm"), MasterStyle.Full),
        new KeyedMasterStyle(ModKey.FromFileName("SFBGS007.esm"), MasterStyle.Small),
        new KeyedMasterStyle(ModKey.FromFileName("economy.esm"), MasterStyle.Full),
        new KeyedMasterStyle(ModKey.FromFileName("apc_galaxysmartcontainers.esm"), MasterStyle.Full),
    });

    public static IEnumerable<object?[]> Languages() => new[]
    {
        new object?[] { null },
        new object?[] { Language.English },
        new object?[] { Language.German },
        new object?[] { Language.Japanese },
        new object?[] { Language.Polish },
        new object?[] { Language.ChineseSimplified },
    };

    private static IEnumerable<IStarfieldModGetter> ReadBoth(MockFileSystem fs, string path, Language? language)
    {
        var param = new BinaryReadParameters
        {
            FileSystem = fs,
            MasterFlagsLookup = Lo,
            StringsParam = language == null ? null : new StringsReadParameters { TargetLanguage = language },
        };
        yield return StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, param);
        yield return StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, param);
    }

    private static byte[] WriteBack(MockFileSystem fs, IStarfieldModGetter mod, string type)
    {
        var outPath = Path.Combine("C:", "Out", mod.ModKey.FileName);
        fs.Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        mod.WriteToBinary(outPath, new BinaryWriteParameters
        {
            FileSystem = fs,
            MasterFlagsLookup = Lo,
            MastersListContent = MastersListContentOption.NoCheck,
            MastersListOrdering = new MastersListOrderingByLoadOrder(mod.MasterReferences.Select(m => m.Master)),
        });
        return RecordBytes(fs.File.ReadAllBytes(outPath), type);
    }

    private static (MockFileSystem Fs, string Path) Plugin(string fileName, string tes4, string label, string record)
    {
        var fs = new MockFileSystem();
        var path = Path.Combine("C:", "Data", fileName);
        fs.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        fs.File.WriteAllBytes(path, Hex(tes4).Concat(Group(label, Hex(record))).ToArray());
        return (fs, path);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void StoredText_ReadsTheSameInEveryLanguage_AndWritesBack(Language? language)
    {
        var (fs, path) = Plugin("economy.esm", EconomyTes4, "MISC", EconomyMisc);
        foreach (var mod in ReadBoth(fs, path, language))
        {
            mod.MiscItems.ShouldHaveSingleItem().Name!.String.ShouldBe("Samson’s Art");
            WriteBack(fs, mod, "MISC").ShouldBe(Hex(EconomyMisc));
        }

        (fs, path) = Plugin("apc_galaxysmartcontainers.esm", ApcTes4, "BOOK", ApcBook);
        foreach (var mod in ReadBoth(fs, path, language))
        {
            mod.Books.ShouldHaveSingleItem().Name!.String.ShouldBe(" Galaxy Handbook — Vol 1: Intro");
            WriteBack(fs, mod, "BOOK").ShouldBe(Hex(ApcBook));
        }
    }

    // A caller's own non-localized encoding is still used, in reading and (given the same) in writing
    [Fact]
    public void NonLocalizedEncodingOverride_StillApplies()
    {
        var (fs, path) = Plugin("economy.esm", EconomyTes4, "MISC", EconomyMisc);
        var mod = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, new BinaryReadParameters
        {
            FileSystem = fs,
            MasterFlagsLookup = Lo,
            StringsParam = new StringsReadParameters { NonLocalizedEncodingOverride = MutagenEncoding._utf8 },
        });
        mod.MiscItems.ShouldHaveSingleItem().Name!.String.ShouldBe("Samson�s Art");
    }
}
