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

// C180: two headers Mutagen refused.
// - Both the Small (0x100) and the Medium (0x400) flag: now read as Small, as xEdit does (Light checked first when it
//   gives the plugin a slot). Writing both is refused as xEdit refuses to save one; the default write (Auto) writes Small.
// - A master that is in neither the load order nor the known masters: still a MissingModException (its style decides
//   every other master's index; the game and xEdit don't load such a plugin either), now naming the plugin, the master
//   and the remedy (WithKnownMasters / MasterFlagsLookup).
public class HeaderBothSizesAndMissingMasterTests
{
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");
    private const int Light = 0x100;
    private const int Medium = 0x400;

    private static LoadOrder<IModMasterStyledGetter> Lo(params (ModKey Key, MasterStyle Style)[] mods) =>
        new(mods.Select(m => (IModMasterStyledGetter)new KeyedMasterStyle(m.Key, m.Style)));

    private static (MockFileSystem Fs, string Path, ModKey Plugin) BothFlagsPlugin()
    {
        var fs = new MockFileSystem();
        var plugin = ModKey.FromFileName("Both.esm");
        var mod = new StarfieldMod(plugin, StarfieldRelease.Starfield) { IsSmallMaster = true };
        mod.Keywords.Add(new Keyword(new FormKey(plugin, 0x800), StarfieldRelease.Starfield) { EditorID = "Own" });
        mod.Keywords.Add(new Keyword(new FormKey(StarfieldEsm, 0xABC), StarfieldRelease.Starfield) { EditorID = "Override" });
        var path = Path.Combine("C:", "Data", plugin.FileName);
        fs.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        mod.WriteToBinary(path, new BinaryWriteParameters { FileSystem = fs, MasterFlagsLookup = Lo((StarfieldEsm, MasterStyle.Full)) });
        // Set the Medium flag too
        var bytes = fs.File.ReadAllBytes(path);
        BitConverter.GetBytes(BitConverter.ToInt32(bytes, 8) | Medium).CopyTo(bytes, 8);
        fs.File.WriteAllBytes(path, bytes);
        return (fs, path, plugin);
    }

    [Fact]
    public void BothSizeFlags_ReadAsSmall_BothReaders()
    {
        var (fs, path, plugin) = BothFlagsPlugin();
        var param = new BinaryReadParameters { FileSystem = fs, MasterFlagsLookup = Lo((StarfieldEsm, MasterStyle.Full)) };
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, param);
        var full = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, param);
        foreach (var mod in new IStarfieldModGetter[] { overlay, full })
        {
            mod.GetMasterStyle().ShouldBe(MasterStyle.Small);
            mod.Keywords.Select(k => k.FormKey).ShouldBe(new[] { new FormKey(plugin, 0x800), new FormKey(StarfieldEsm, 0xABC) }, ignoreOrder: true);
        }
    }

    [Fact]
    public void BothSizeFlags_WriteRefusedAsIs_DefaultWritesSmall()
    {
        var (fs, path, _) = BothFlagsPlugin();
        var lo = Lo((StarfieldEsm, MasterStyle.Full));
        var mod = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield, new BinaryReadParameters { FileSystem = fs, MasterFlagsLookup = lo });
        var outPath = Path.Combine("C:", "Out", "Both.esm");
        fs.Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        var ex = Should.Throw<ModHeaderMalformedException>(() => mod.WriteToBinary(outPath,
            new BinaryWriteParameters { FileSystem = fs, MasterFlagsLookup = lo, HeaderSizeFlags = HeaderSizeFlagsOption.NoCheck }));
        ex.Message.ShouldContain("both the Small (Light) and the Medium flag");

        mod.WriteToBinary(outPath, new BinaryWriteParameters { FileSystem = fs, MasterFlagsLookup = lo });
        var flags = BitConverter.ToInt32(fs.File.ReadAllBytes(outPath), 8);
        (flags & (Light | Medium)).ShouldBe(Light);
    }

    [Fact]
    public void MissingMaster_Throws_NamingPluginMasterAndRemedy()
    {
        var fs = new MockFileSystem();
        var plugin = ModKey.FromFileName("Child.esm");
        var master = ModKey.FromFileName("Gone.esm");
        var mod = new StarfieldMod(plugin, StarfieldRelease.Starfield);
        mod.Keywords.Add(new Keyword(new FormKey(master, 0x800), StarfieldRelease.Starfield) { EditorID = "OverrideOfGone" });
        var path = Path.Combine("C:", "Data", plugin.FileName);
        fs.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        mod.WriteToBinary(path, new BinaryWriteParameters { FileSystem = fs, MasterFlagsLookup = Lo((master, MasterStyle.Full)) });

        var ex = Should.Throw<MissingModException>(() => StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield,
            new BinaryReadParameters { FileSystem = fs, MasterFlagsLookup = Lo((StarfieldEsm, MasterStyle.Full)) }));
        ex.Message.ShouldContain("Child.esm");
        ex.Message.ShouldContain("Gone.esm");
        ex.Message.ShouldContain("WithKnownMasters");

        // With the master's style given, it reads
        using var read = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield,
            new BinaryReadParameters { FileSystem = fs, MasterFlagsLookup = Lo((master, MasterStyle.Full)) });
        read.Keywords.Single().FormKey.ShouldBe(new FormKey(master, 0x800));
    }
}
