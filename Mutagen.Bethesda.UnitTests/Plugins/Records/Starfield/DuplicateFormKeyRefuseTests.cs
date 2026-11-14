using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Mutagen.Bethesda.Environments.DI;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Masters.DI;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Order.DI;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.DI;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C204 (the user, 2026-11-14): a plugin whose records share a FormKey is refused by default, when it is opened, with
// every clashing pair named (cross-group clashes too, as MissionsGalore.esm's GLOB 03000869 / TMLM 02000869); a load order
// import drops it, and the plugins that master it, with the reasons listed, and loads the rest.
public class DuplicateFormKeyRefuseTests
{
    private static readonly ModKey Base = ModKey.FromFileName("Base.esm");
    private static readonly ModKey Dup = ModKey.FromFileName("Dup.esm");
    private static readonly ModKey Dependent = ModKey.FromFileName("Dependent.esm");

    private static byte[] Sub(string type, byte[] content) =>
        Encoding.ASCII.GetBytes(type).Concat(BitConverter.GetBytes((ushort)content.Length)).Concat(content).ToArray();

    private static byte[] Edid(string s) => Sub("EDID", Encoding.ASCII.GetBytes(s + "\0"));

    private static byte[] Record(string type, uint formId, byte[] content)
    {
        var s = new MemoryStream();
        s.Write(Encoding.ASCII.GetBytes(type));
        s.Write(BitConverter.GetBytes(content.Length));
        s.Write(BitConverter.GetBytes(0));
        s.Write(BitConverter.GetBytes(formId));
        s.Write(Convert.FromHexString("0000000046020000"));
        s.Write(content);
        return s.ToArray();
    }

    private static byte[] Group(string label, params byte[][] records)
    {
        var content = records.SelectMany(r => r).ToArray();
        var s = new MemoryStream();
        s.Write("GRUP"u8);
        s.Write(BitConverter.GetBytes(24 + content.Length));
        s.Write(Encoding.ASCII.GetBytes(label));
        s.Write(BitConverter.GetBytes(0));
        s.Write(new byte[8]);
        s.Write(content);
        return s.ToArray();
    }

    private static byte[] Tes4(params string[] masters) =>
        Record("TES4", 0, Sub("HEDR", Convert.FromHexString("8FC2753F0300000004080000"))
                .Concat(masters.SelectMany(m => Sub("MAST", Encoding.ASCII.GetBytes(m + "\0")))).ToArray())
            .Select((b, i) => i == 8 ? (byte)0x01 : b).ToArray();

    // Base.esm: a clean plugin. Dup.esm (master Base.esm, own index 01): KYWD 01000801 and 01000801 again in its group,
    // and KYWD 01000805 / FLST 02000805 (an index past the list reads as itself) across groups. Dependent.esm masters Dup.
    private static MockFileSystem Data(out string folder)
    {
        folder = Path.Combine("C:", "Data");
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory(folder);
        fs.File.WriteAllBytes(Path.Combine(folder, Base.FileName), Tes4().Concat(Group("KYWD",
            Record("KYWD", 0x00000801, Edid("BaseKeyword")))).ToArray());
        fs.File.WriteAllBytes(Path.Combine(folder, Dup.FileName), Tes4("Base.esm").Concat(Group("KYWD",
                Record("KYWD", 0x01000801, Edid("FirstKeyword")),
                Record("KYWD", 0x01000801, Edid("SecondKeyword")),
                Record("KYWD", 0x01000805, Edid("CrossKeyword"))))
            .Concat(Group("FLST", Record("FLST", 0x02000805, Edid("CrossList")))).ToArray());
        fs.File.WriteAllBytes(Path.Combine(folder, Dependent.FileName), Tes4("Base.esm", "Dup.esm").Concat(Group("KYWD",
            Record("KYWD", 0x02000801, Edid("DependentKeyword")))).ToArray());
        return fs;
    }

    private static BinaryReadParameters Params(MockFileSystem fs, DuplicateFormKeyOption dup = DuplicateFormKeyOption.Refuse) => new()
    {
        FileSystem = fs,
        DuplicateFormKeys = dup,
        MasterFlagsLookup = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(Base, MasterStyle.Full),
            new KeyedMasterStyle(Dup, MasterStyle.Full),
            new KeyedMasterStyle(Dependent, MasterStyle.Full),
        }),
    };

    [Fact]
    public void Plugin_WithSharedFormKeys_RefusedWhenOpened_BothReaders_EveryPairNamed()
    {
        var fs = Data(out var folder);
        var path = Path.Combine(folder, Dup.FileName);
        foreach (var read in new Func<object>[]
                 {
                     () => StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, Params(fs)),
                     () => StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, Params(fs)),
                 })
        {
            var ex = Should.Throw<DuplicateFormKeyException>(read);
            ex.ModKey.ShouldBe(Dup);
            ex.Pairs.ShouldBe(new[]
            {
                new DuplicateFormKeyPair(new FormKey(Dup, 0x801),
                    new RecordType("KYWD"), "FirstKeyword", 0x01000801, new RecordType("KYWD"), "SecondKeyword", 0x01000801),
                new DuplicateFormKeyPair(new FormKey(Dup, 0x805),
                    new RecordType("KYWD"), "CrossKeyword", 0x01000805, new RecordType("FLST"), "CrossList", 0x02000805),
            });
            ex.Message.ShouldContain("Dup.esm refused");
            ex.Message.ShouldContain("FLST 02000805 CrossList");
        }
    }

    [Fact]
    public void Plugin_WithSharedFormKeys_KeepLater_ReadsAsTheGameDoes()
    {
        var fs = Data(out var folder);
        var path = Path.Combine(folder, Dup.FileName);
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, Params(fs, DuplicateFormKeyOption.KeepLater));
        overlay.Keywords.Select(k => k.EditorID).ShouldBe(new[] { "SecondKeyword", "CrossKeyword" });
        var full = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, Params(fs, DuplicateFormKeyOption.KeepLater));
        full.Keywords.Select(k => k.EditorID).ShouldBe(new[] { "SecondKeyword", "CrossKeyword" });
    }

    [Fact]
    public void LoadOrder_DropsTheRefusedPluginAndItsDependents_LoadsTheRest()
    {
        var fs = Data(out var folder);
        using var lo = LoadOrder.Import<IStarfieldModGetter>(folder, new[] { Base, Dup, Dependent }, GameRelease.Starfield, fs);
        lo.ListedOrder.Select(l => (l.ModKey, l.Mod != null)).ShouldBe(new[] { (Base, true), (Dup, false), (Dependent, false) });
        var errors = lo.GetLoadErrors();
        errors.Select(e => e.ModKey).ShouldBe(new[] { Dup, Dependent });
        errors[0].Exception.ShouldBeOfType<DuplicateFormKeyException>();
        errors[0].Reason.ShouldStartWith("refused: 2 pairs of its records share a FormKey");
        errors[1].Exception.ShouldBeNull();
        errors[1].Reason.ShouldBe("master Dup.esm was refused");
    }

    [Fact]
    public void LoadOrder_Throw_KeepsTheOldBehaviour()
    {
        var fs = Data(out var folder);
        var release = new GameReleaseInjection(GameRelease.Starfield);
        var dataDir = new DataDirectoryInjection(folder);
        var importer = new LoadOrderImporter<IStarfieldModGetter>(
            fs,
            dataDir,
            new LoadOrderListingsInjection(new[] { Base, Dup }.Select(k => new LoadOrderListing(k, true))),
            new ModImporter<IStarfieldModGetter>(fs, release),
            new MasterFlagsLookupProvider(release, fs, dataDir));
        var ex = Should.Throw<Exception>(() => importer.Import(new BinaryReadParameters
        {
            FileSystem = fs,
            LoadOrderReadFailures = PluginReadFailureOption.Throw,
        }));
        Flatten(ex).OfType<DuplicateFormKeyException>().ShouldNotBeEmpty(ex.ToString());
    }

    private static IEnumerable<Exception> Flatten(Exception ex)
    {
        yield return ex;
        IEnumerable<Exception> inner = ex is AggregateException agg ? agg.InnerExceptions : (ex.InnerException == null ? Array.Empty<Exception>() : new[] { ex.InnerException });
        foreach (var i in inner)
        {
            foreach (var e in Flatten(i)) yield return e;
        }
    }
}
