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

// The overridden-forms list (TES4 ONAM) of a Starfield plugin. The Creation Kit lists every record of another plugin
// under a parent record (here: topics in a quest), in an order of its own, and leaves out overrides of FormIDs the
// master doesn't have (in SFBGS050.esm, 14 of 6,195). Mutagen's default write (OverriddenFormsOption.Auto) writes a
// plugin's list as read while the records it covers are the ones read, updates it by the difference when they changed,
// and computes it for a new mod. The plugin below has quest 001000:Starfield.esm with topics 002002, 002001 and 002003,
// and its ONAM lists 002002, 002001 (not sorted, 002003 left out), as the CK would.
public class OverriddenFormsDefaultTests
{
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");
    private static readonly ModKey Plugin = ModKey.FromFileName("OnamTest.esm");

    private static byte[] Sub(string type, byte[] content) =>
        Encoding.ASCII.GetBytes(type).Concat(BitConverter.GetBytes((ushort)content.Length)).Concat(content).ToArray();

    private static byte[] Edid(string name) => Sub("EDID", Encoding.ASCII.GetBytes(name + "\0"));

    private static byte[] Record(string type, uint flags, uint formId, params byte[][] subs)
    {
        var content = subs.SelectMany(x => x).ToArray();
        var s = new MemoryStream();
        s.Write(Encoding.ASCII.GetBytes(type));
        s.Write(BitConverter.GetBytes(content.Length));
        s.Write(BitConverter.GetBytes(flags));
        s.Write(BitConverter.GetBytes(formId));
        s.Write(Convert.FromHexString("0000000046020000"));
        s.Write(content);
        return s.ToArray();
    }

    private static byte[] Group(byte[] label, int groupType, params byte[][] records)
    {
        var content = records.SelectMany(r => r).ToArray();
        var s = new MemoryStream();
        s.Write("GRUP"u8);
        s.Write(BitConverter.GetBytes(24 + content.Length));
        s.Write(label);
        s.Write(BitConverter.GetBytes(groupType));
        s.Write(new byte[8]);
        s.Write(content);
        return s.ToArray();
    }

    private static byte[] Plugin_(string masterSpelling, params uint[] onam)
    {
        var tes4 = Record("TES4", 0x1, 0,
            Sub("HEDR", Convert.FromHexString("8FC2753F04000000000800FF")),
            Sub("MAST", Encoding.ASCII.GetBytes(masterSpelling + "\0")),
            Sub("ONAM", onam.SelectMany(BitConverter.GetBytes).ToArray()));
        var quest = Record("QUST", 0, 0x00001000, Edid("TestQuest"));
        var children = Group(BitConverter.GetBytes(0x00001000), 10,
            Record("DIAL", 0, 0x00002002, Edid("TopicB")),
            Record("DIAL", 0, 0x00002001, Edid("TopicA")),
            Record("DIAL", 0, 0x00002003, Edid("TopicC")));
        return tes4.Concat(Group("QUST"u8.ToArray(), 0, quest, children)).ToArray();
    }

    private sealed record Setup(MockFileSystem Fs, string Path, LoadOrder<IModMasterStyledGetter> Lo);

    private static Setup Make(byte[] file)
    {
        var fs = new MockFileSystem();
        var path = Path.Combine("C:", "Data", Plugin.FileName);
        fs.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        fs.File.WriteAllBytes(path, file);
        var lo = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
            new KeyedMasterStyle(Plugin, MasterStyle.Full),
        });
        return new Setup(fs, path, lo);
    }

    private static StarfieldMod ReadFull(Setup s) =>
        StarfieldMod.CreateFromBinary(s.Path, StarfieldRelease.Starfield,
            new BinaryReadParameters { FileSystem = s.Fs, MasterFlagsLookup = s.Lo });

    private static IStarfieldModDisposableGetter ReadOverlay(Setup s) =>
        StarfieldMod.CreateFromBinaryOverlay(s.Path, StarfieldRelease.Starfield,
            new BinaryReadParameters { FileSystem = s.Fs, MasterFlagsLookup = s.Lo });

    // Writes with the default parameters (apart from the load order) and returns the written TES4 ONAM and MAST
    private static (uint[] Onam, string[] Masters) Write(Setup s, IStarfieldModGetter mod,
        OverriddenFormsOption? option = null)
    {
        var outPath = Path.Combine("C:", "Out", Plugin.FileName);
        s.Fs.Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        var param = new BinaryWriteParameters
        {
            FileSystem = s.Fs,
            MasterFlagsLookup = s.Lo,
            MastersListOrdering = new MastersListOrderingByLoadOrder(new[] { StarfieldEsm, Plugin }),
        };
        if (option is { } o) param = param with { OverriddenFormsOption = o };
        mod.WriteToBinary(outPath, param);
        var bytes = s.Fs.File.ReadAllBytes(outPath);
        var size = BitConverter.ToInt32(bytes, 4);
        var onam = new List<uint>();
        var masters = new List<string>();
        for (int p = 24; p < 24 + size;)
        {
            var sig = Encoding.ASCII.GetString(bytes, p, 4);
            int len = BitConverter.ToUInt16(bytes, p + 4);
            if (sig == "ONAM") for (int i = 0; i < len; i += 4) onam.Add(BitConverter.ToUInt32(bytes, p + 6 + i));
            if (sig == "MAST") masters.Add(Encoding.ASCII.GetString(bytes, p + 6, len - 1));
            p += 6 + len;
        }
        return (onam.ToArray(), masters.ToArray());
    }

    [Fact]
    public void Unchanged_KeepsTheListAsRead_BothReadersAndCopies()
    {
        var s = Make(Plugin_("Starfield.esm", 0x00002002, 0x00002001));
        var full = ReadFull(s);
        using var overlay = ReadOverlay(s);
        foreach (var mod in new IStarfieldModGetter[] { full, overlay, (IStarfieldModGetter)full.DeepCopy(), (IStarfieldModGetter)overlay.DeepCopy() })
        {
            Write(s, mod).Onam.ShouldBe(new uint[] { 0x00002002, 0x00002001 });
        }
    }

    [Fact]
    public void ChangedOverrides_UpdateTheListByTheDifference()
    {
        var s = Make(Plugin_("Starfield.esm", 0x00002002, 0x00002001));
        var mod = ReadFull(s);
        var quest = mod.Quests.Single();
        quest.DialogTopics.Remove(quest.DialogTopics.Single(t => t.FormKey.ID == 0x002001));
        quest.DialogTopics.Add(new DialogTopic(new FormKey(StarfieldEsm, 0x002004), StarfieldRelease.Starfield) { EditorID = "TopicD" });
        // 002001 left, 002004 came; the list keeps its order and still leaves out 002003, as read
        Write(s, mod).Onam.ShouldBe(new uint[] { 0x00002002, 0x00002004 });
    }

    [Fact]
    public void NewMod_GainsTheComputedList()
    {
        var s = Make(Plugin_("Starfield.esm"));
        var mod = new StarfieldMod(Plugin, StarfieldRelease.Starfield);
        var quest = new Quest(new FormKey(StarfieldEsm, 0x001000), StarfieldRelease.Starfield) { EditorID = "TestQuest" };
        quest.DialogTopics.Add(new DialogTopic(new FormKey(StarfieldEsm, 0x002002), StarfieldRelease.Starfield) { EditorID = "TopicB" });
        quest.DialogTopics.Add(new DialogTopic(new FormKey(StarfieldEsm, 0x002001), StarfieldRelease.Starfield) { EditorID = "TopicA" });
        quest.DialogTopics.Add(new DialogTopic(mod.GetNextFormKey(), StarfieldRelease.Starfield) { EditorID = "OwnTopic" });
        mod.Quests.Add(quest);
        mod.Keywords.Add(new Keyword(new FormKey(StarfieldEsm, 0x003000), StarfieldRelease.Starfield) { EditorID = "TopLevelOverride" });
        // Only the topics overriding Starfield.esm under the quest; sorted (the quest and the keyword are top level)
        Write(s, mod).Onam.ShouldBe(new uint[] { 0x00002001, 0x00002002 });
    }

    [Fact]
    public void ExplicitOptions_WorkAsBefore()
    {
        var s = Make(Plugin_("Starfield.esm", 0x00002002, 0x00002001));
        var mod = ReadFull(s);
        mod.Quests.Single().DialogTopics.Add(new DialogTopic(new FormKey(StarfieldEsm, 0x002004), StarfieldRelease.Starfield) { EditorID = "TopicD" });
        Write(s, mod, OverriddenFormsOption.NoCheck).Onam.ShouldBe(new uint[] { 0x00002002, 0x00002001 });
        Write(s, mod, OverriddenFormsOption.Iterate).Onam.ShouldBe(new uint[] { 0x00002001, 0x00002002, 0x00002003, 0x00002004 });
    }

    // A mod rebuilt from another form (JSON, a database) gets the read state back through the public API
    [Fact]
    public void Baseline_GetAndSet_RebuiltMod()
    {
        var s = Make(Plugin_("Starfield.esm", 0x00002002, 0x00002001));
        var read = ReadFull(s);
        var baseline = read.GetOverriddenFormsBaseline();
        baseline.ShouldNotBeNull();
        baseline.Select(x => x.ID).ShouldBe(new uint[] { 0x002001, 0x002002, 0x002003 });
        using (var overlay = ReadOverlay(s))
        {
            overlay.GetOverriddenFormsBaseline()!.Select(x => x.ID).ShouldBe(new uint[] { 0x002001, 0x002002, 0x002003 });
        }

        StarfieldMod Rebuild()
        {
            var mod = new StarfieldMod(Plugin, StarfieldRelease.Starfield);
            mod.ModHeader.SetOverriddenForms(new[] { new FormKey(StarfieldEsm, 0x002002), new FormKey(StarfieldEsm, 0x002001) });
            var quest = new Quest(new FormKey(StarfieldEsm, 0x001000), StarfieldRelease.Starfield) { EditorID = "TestQuest" };
            foreach (var id in new uint[] { 0x002002, 0x002001, 0x002003 })
            {
                quest.DialogTopics.Add(new DialogTopic(new FormKey(StarfieldEsm, id), StarfieldRelease.Starfield));
            }
            mod.Quests.Add(quest);
            return mod;
        }

        var withBaseline = Rebuild();
        withBaseline.SetOverriddenFormsBaseline(baseline);
        Write(s, withBaseline).Onam.ShouldBe(new uint[] { 0x00002002, 0x00002001 });
        // Without it the rebuilt mod is a new mod: the list is computed
        Write(s, Rebuild()).Onam.ShouldBe(new uint[] { 0x00002001, 0x00002002, 0x00002003 });
    }

    // A master's spelling doesn't depend on which FormKey the writer meets first: the mod's own master list spells
    // it, else the load order does
    [Fact]
    public void MasterSpelling_FromTheModsListOrTheLoadOrder()
    {
        var s = Make(Plugin_("starfield.esm", 0x00002002, 0x00002001));
        Write(s, ReadFull(s)).Masters.ShouldBe(new[] { "starfield.esm" });
        var mod = new StarfieldMod(Plugin, StarfieldRelease.Starfield);
        mod.Keywords.Add(new Keyword(new FormKey(ModKey.FromFileName("starfield.esm"), 0x003000), StarfieldRelease.Starfield));
        Write(s, mod).Masters.ShouldBe(new[] { "Starfield.esm" });
    }
}
