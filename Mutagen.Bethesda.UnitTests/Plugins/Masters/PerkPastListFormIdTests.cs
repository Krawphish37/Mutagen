using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Masters;

// C69 (decision 6.1 revised): a perk effect's FormID whose master index points past the master list is written back as
// stored, and GetStoredFormIds reports it as stored. The overlay parses a perk's effects when the record is made, so the
// write never read the link again and C189's per-write flag missed it: the overlay wrote the plugin's own index and the
// report said 01000800. Real case: CrewSkillOverhaul.esm (one master) overrides Crew_WeightLifting (21B8D9:Starfield.esm),
// whose fourth rank's ability effect stores 02000800, its own SPEL Crew_Weightlifting_Rank04.
public class PerkPastListFormIdTests
{
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");

    // CrewSkillOverhaul.esm's TES4 and its PERK 21B8D9, as stored
    private static readonly byte[] Tes4 = Convert.FromHexString("544553343B00000001000000000000000000000046020000484544520C008FC2753F24000000010800FF4D4153540E00537461726669656C642E65736D00424E414D05004D61696E00494E4343040000000000");
    private static readonly byte[] PerkRecord = Convert.FromHexString("5045524BF301000000000000D9B82100000000002B020000454449441300437265775F5765696768744C696674696E670046554C4C0F00576569676874204C696674696E67004445534301000044415441040004000008474E414D1D0050617463685F506879736963616C5F5765696768744C696674696E67005052524B000050524B450300010000444154410400EDB1280050524B460000444553433900437265773A20496E63726561736520746865206361727279696E67206361706163697479206279206120746F74616C206F66203135206B67005052524600005052524B000050524B450300010100444154410400ECB1280050524B460000444553433900437265773A20496E63726561736520746865206361727279696E67206361706163697479206279206120746F74616C206F66203330206B67005052524600005052524B000050524B450300010200444154410400EEB1280050524B460000444553433900437265773A20496E63726561736520746865206361727279696E67206361706163697479206279206120746F74616C206F66203630206B67005052524600005052524B000050524B4503000103004441544104000008000250524B460000444553433A00437265773A20496E63726561736520746865206361727279696E67206361706163697479206279206120746F74616C206F6620313230206B6700505252460000");

    private static byte[] Group(string label, byte[] records)
    {
        var s = new MemoryStream();
        s.Write("GRUP"u8);
        s.Write(BitConverter.GetBytes(24 + records.Length));
        s.Write(Encoding.ASCII.GetBytes(label));
        s.Write(BitConverter.GetBytes(0));
        s.Write(new byte[8]);
        s.Write(records);
        return s.ToArray();
    }

    private sealed record Setup(MockFileSystem Fs, string Path, ModKey Plugin, LoadOrder<IModMasterStyledGetter> Lo);

    private static Setup Make(ModKey plugin, byte[] file)
    {
        var fs = new MockFileSystem();
        var path = System.IO.Path.Combine("C:", "Data", plugin.FileName);
        fs.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        fs.File.WriteAllBytes(path, file);
        var lo = new LoadOrder<IModMasterStyledGetter>(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
            new KeyedMasterStyle(plugin, MasterStyle.Full),
        });
        return new Setup(fs, path, plugin, lo);
    }

    private static BinaryReadParameters ReadParams(Setup s) => new() { FileSystem = s.Fs, MasterFlagsLookup = s.Lo };

    private static byte[] Write(Setup s, IStarfieldModGetter mod)
    {
        var outPath = System.IO.Path.Combine("C:", "Out", s.Plugin.FileName);
        s.Fs.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath)!);
        mod.WriteToBinary(outPath, new BinaryWriteParameters { FileSystem = s.Fs, MasterFlagsLookup = s.Lo });
        return s.Fs.File.ReadAllBytes(outPath);
    }

    // The bytes of the first record of the given type in a plugin's file (after the group of that label; not compressed)
    private static byte[] RecordBytes(byte[] file, string type)
    {
        var sig = Encoding.ASCII.GetBytes(type);
        var at = 0;
        while (true)
        {
            var next = file.AsSpan(at).IndexOf(sig);
            next.ShouldBeGreaterThanOrEqualTo(0, $"no {type} record");
            at += next;
            // a group's label is preceded by GRUP and its size; a record's signature is followed by its size
            if (at < 8 || Encoding.ASCII.GetString(file, at - 8, 4) != "GRUP") break;
            at += 4;
        }
        var size = BitConverter.ToInt32(file, at + 4);
        return file.AsSpan(at, 24 + size).ToArray();
    }

    // Each subrecord's content by type and turn
    private static Dictionary<(string, int), byte[]> Subrecords(byte[] record)
    {
        var ret = new Dictionary<(string, int), byte[]>();
        var turns = new Dictionary<string, int>();
        int p = 24;
        while (p + 6 <= record.Length)
        {
            var sig = Encoding.ASCII.GetString(record, p, 4);
            var len = BitConverter.ToUInt16(record, p + 4);
            var turn = turns.GetValueOrDefault(sig);
            turns[sig] = turn + 1;
            ret[(sig, turn)] = record.AsSpan(p + 6, len).ToArray();
            p += 6 + len;
        }
        return ret;
    }

    // Every FormID GetStoredFormIds reports is the value stored at its place, and the past-the-list ones say so
    private static void ReportedAsStored(Setup s, IMajorRecordGetter record, IModGetter mod, byte[] bytes, uint pastList, int expected)
    {
        var subs = Subrecords(bytes);
        var positions = record.GetStoredFormIds(mod, s.Lo);
        foreach (var p in positions.Where(p => p.Subrecord != null))
        {
            p.Offset.ShouldBeGreaterThanOrEqualTo(0);
            BitConverter.ToUInt32(subs[(p.Subrecord!.Value.Type, p.SubrecordIndex)], p.Offset)
                .ShouldBe(p.Raw, $"{p.Subrecord} {p.SubrecordIndex} {p.Offset}");
        }
        var past = positions.Where(p => p.Raw == pastList).ToList();
        past.Count.ShouldBe(expected);
        past.ShouldAllBe(p => p.Form == StoredFormIdForm.PastTheList);
    }

    [Fact]
    public void CrewSkillOverhaul_CrewWeightLifting_RoundTripsAndReportsAsStored_BothReaders()
    {
        var plugin = ModKey.FromFileName("CrewSkillOverhaul.esm");
        var s = Make(plugin, Tes4.Concat(Group("PERK", PerkRecord)).ToArray());
        var full = StarfieldMod.CreateFromBinary(s.Path, StarfieldRelease.Starfield, ReadParams(s));
        var abilities = full.Perks.Single().Ranks.SelectMany(r => r.Effects).OfType<IPerkAbilityEffectGetter>().ToList();
        abilities.Count.ShouldBe(4);
        abilities[3].Ability.FormKey.ShouldBe(new FormKey(plugin, 0x800));
        ReportedAsStored(s, full.Perks.Single(), full, PerkRecord, 0x02000800, 1);
        RecordBytes(Write(s, full), "PERK").ShouldBe(PerkRecord);

        using var overlay = StarfieldMod.CreateFromBinaryOverlay(s.Path, StarfieldRelease.Starfield, ReadParams(s));
        ReportedAsStored(s, overlay.Perks.Single(), overlay, PerkRecord, 0x02000800, 1);
        RecordBytes(Write(s, overlay), "PERK").ShouldBe(PerkRecord);
    }

    // Synthetic: a Full plugin with one master (own index 01) whose perk stores 02000801 in an ability effect (DATA), a
    // quest effect (DATA) and an entry point's spell (EPFD): written by Mutagen with 01000801, then stored past the list
    [Fact]
    public void PerkEffectLinksPastTheList_RoundTripAndReportAsStored_BothReaders()
    {
        var plugin = ModKey.FromFileName("PastListPerk.esm");
        var mod = new StarfieldMod(plugin, StarfieldRelease.Starfield);
        mod.ModHeader.Flags = StarfieldModHeader.HeaderFlag.Master;
        var own = new FormKey(plugin, 0x801);
        var rank = new PerkRank { Description = "d" };
        rank.Effects.Add(new PerkAbilityEffect { Ability = own.ToLink<ISpellGetter>() });
        rank.Effects.Add(new PerkQuestEffect { Quest = own.ToLink<IQuestGetter>(), Stage = 10 });
        var choice = new PerkEntryPointAddActivateChoice();
        choice.Spell.SetTo(own);
        rank.Effects.Add(choice);
        var perk = new Perk(new FormKey(plugin, 0x802), StarfieldRelease.Starfield) { EditorID = "PastListPerk" };
        // a link to Starfield.esm makes it the one master
        rank.Effects.Add(new PerkAbilityEffect { Ability = new FormKey(StarfieldEsm, 0x14).ToLink<ISpellGetter>() });
        perk.Ranks.Add(rank);
        mod.Perks.Add(perk);
        var canonical = Write(Make(plugin, Array.Empty<byte>()), mod);
        var record = RecordBytes(canonical, "PERK");
        var stored = (byte[])canonical.Clone();
        var recordAt = stored.AsSpan().IndexOf(record);
        var links = 0;
        for (int i = recordAt + 24; i + 4 <= recordAt + record.Length; i++)
        {
            if (BitConverter.ToUInt32(stored, i) != 0x01000801) continue;
            BitConverter.GetBytes(0x02000801u).CopyTo(stored, i);
            links++;
        }
        links.ShouldBe(3);
        var storedRecord = RecordBytes(stored, "PERK");

        var s = Make(plugin, stored);
        var full = StarfieldMod.CreateFromBinary(s.Path, StarfieldRelease.Starfield, ReadParams(s));
        full.Perks.Single().Ranks.Single().Effects.OfType<IPerkAbilityEffectGetter>().First().Ability.FormKey.ShouldBe(own);
        ReportedAsStored(s, full.Perks.Single(), full, storedRecord, 0x02000801, 3);
        RecordBytes(Write(s, full), "PERK").ShouldBe(storedRecord);
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(s.Path, StarfieldRelease.Starfield, ReadParams(s));
        ReportedAsStored(s, overlay.Perks.Single(), overlay, storedRecord, 0x02000801, 3);
        RecordBytes(Write(s, overlay), "PERK").ShouldBe(storedRecord);
    }
}
