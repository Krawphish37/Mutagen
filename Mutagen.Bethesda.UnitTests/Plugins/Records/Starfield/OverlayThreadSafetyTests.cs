using System.Collections.Concurrent;
using System.IO.Abstractions.TestingHelpers;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C179: an overlay (CreateFromBinaryOverlay) may be read from several threads at once. Its records are cut out of the
// file under a lock on the shared stream (PluginBinaryOverlay.LockExtractMemory) and each group and record overlay is
// its own object, so threads don't share parse state. This reads one overlay from 8 threads at once, every record
// copied out (DeepCopy reads every field), and holds each copy against a single-threaded read.
public class OverlayThreadSafetyTests
{
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");

    private static (MockFileSystem Fs, string Path, LoadOrder<IModMasterStyledGetter> Lo) MakePlugin()
    {
        var modKey = ModKey.FromFileName("Threads.esm");
        var mod = new StarfieldMod(modKey, StarfieldRelease.Starfield);
        var keywords = new List<Keyword>();
        for (uint i = 0; i < 300; i++)
        {
            var k = new Keyword(new FormKey(modKey, 0x800 + i), StarfieldRelease.Starfield) { EditorID = $"Kw{i}", Notes = $"note {i}" };
            mod.Keywords.Add(k);
            keywords.Add(k);
        }
        for (uint i = 0; i < 300; i++)
        {
            var w = new Weapon(new FormKey(modKey, 0x1000 + i), StarfieldRelease.Starfield)
            {
                EditorID = $"Weapon{i}",
                Name = $"Weapon {i}",
                Keywords = new() { keywords[(int)i].ToLink(), keywords[(int)(299 - i)].ToLink() },
            };
            mod.Weapons.Add(w);
        }
        for (uint i = 0; i < 300; i++)
        {
            mod.MiscItems.Add(new MiscItem(new FormKey(modKey, 0x2000 + i), StarfieldRelease.Starfield) { EditorID = $"Misc{i}", Value = (int)i });
        }
        mod.Keywords.Add(new Keyword(new FormKey(StarfieldEsm, 0xABC), StarfieldRelease.Starfield) { EditorID = "Override" });
        var fs = new MockFileSystem();
        var path = System.IO.Path.Combine("C:", "Data", modKey.FileName);
        fs.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var lo = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
            new KeyedMasterStyle(modKey, MasterStyle.Full),
        });
        mod.WriteToBinary(path, new BinaryWriteParameters { FileSystem = fs, MasterFlagsLookup = lo });
        return (fs, path, lo);
    }

    [Fact]
    public void OneOverlay_ReadFromEightThreads_GivesTheSameRecords()
    {
        var (fs, path, lo) = MakePlugin();
        var param = new BinaryReadParameters { FileSystem = fs, MasterFlagsLookup = lo };

        // Single-threaded reference
        Dictionary<FormKey, IMajorRecord> expected;
        using (var reference = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, param))
        {
            expected = reference.EnumerateMajorRecords().ToDictionary(r => r.FormKey, r => (IMajorRecord)((IStarfieldMajorRecordGetter)r).DeepCopy());
        }
        expected.Count.ShouldBe(901);

        using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield, param);
        var failures = new ConcurrentBag<string>();
        var counts = new ConcurrentBag<int>();
        using var start = new ManualResetEventSlim(false);
        var threads = Enumerable.Range(0, 8).Select(t => new Thread(() =>
        {
            start.Wait();
            try
            {
                for (int pass = 0; pass < 3; pass++)
                {
                    int n = 0;
                    // Half the threads walk the mod, half go through the groups, so both paths run at once
                    IEnumerable<IMajorRecordGetter> records = (t % 2 == 0)
                        ? overlay.EnumerateMajorRecords()
                        : overlay.Weapons.Cast<IMajorRecordGetter>().Concat(overlay.MiscItems).Concat(overlay.Keywords);
                    foreach (var rec in records)
                    {
                        n++;
                        var copy = ((IStarfieldMajorRecordGetter)rec).DeepCopy();
                        if (!expected.TryGetValue(rec.FormKey, out var want) || !want.Equals(copy))
                            failures.Add($"thread {t} pass {pass}: {rec.FormKey}");
                    }
                    counts.Add(n);
                }
            }
            catch (Exception ex)
            {
                failures.Add($"thread {t}: {ex.GetType().Name}: {ex.Message}");
            }
        })).ToList();
        threads.ForEach(x => x.Start());
        start.Set();
        threads.ForEach(x => x.Join());

        failures.ShouldBeEmpty();
        counts.Count.ShouldBe(24);
        counts.ShouldAllBe(c => c == 901);
    }
}
