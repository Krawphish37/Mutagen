using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Script fragment indices were split into a UInt16 and an Int16 "Unknown". xEdit: quest fragments have Quest Stage
// (u32) and Quest Stage Index (u32); perk, terminal and terminal menu fragments have Fragment Index (u32). In
// Starfield.esm the upper halves are 0 on all 10,202 quest, 25 perk, 3 terminal and 349 terminal menu fragments, so
// they become UInt32 (same bytes). Quest StageIndex (Int32) is UInt32 as in xEdit (0 to 10+, never negative). The
// trailing byte (xEdit Unknown) is 1 on all of them. Real VMADs, both readers, written back byte for byte.
public class ScriptFragmentIndexTests
{
    // MagPerk_GRUNT (21D059:Starfield.esm)
    private const string PerkVmad = "060002000000032B00467261676D656E74733A5065726B733A50524B465F4D61675065726B5F4752554E545F303032314430353900010006005638315F303101010000FFFF20350300010000000000012B00467261676D656E74733A5065726B733A50524B465F4D61675065726B5F4752554E545F30303231443035391100467261676D656E745F456E7472795F3030";
    // RL083_Terminal_Desktop (1C25CD:Starfield.esm)
    private const string TerminalMenuVmad = "060002000000033800467261676D656E74733A5465726D696E616C733A5445524D5F524C3038335F5465726D696E616C5F4465736B746F705F3030314332354344000000010001000000013800467261676D656E74733A5465726D696E616C733A5445524D5F524C3038335F5465726D696E616C5F4465736B746F705F30303143323543441800467261676D656E745F5465726D696E616C4D656E755F3031";
    // GauntletQuest (1F0294:Starfield.esm): one fragment, stage 5 (Fragment_Stage_0005_Item_00)
    private const string QuestVmad = "0600020000000301002A00467261676D656E74733A5175657374733A51465F4761756E746C657451756573745F30303146303239340000000500000000000000012A00467261676D656E74733A5175657374733A51465F4761756E746C657451756573745F30303146303239341B00467261676D656E745F53746167655F303030355F4974656D5F30300000";

    private static void WritesBack(IMajorRecordGetter rec, string vmad)
    {
        var written = GameConstants.Starfield.MajorRecord(Write(w => ((Mutagen.Bethesda.Plugins.Binary.Translations.IBinaryItem)rec).WriteToBinary(w)));
        written.FindSubrecord(new RecordType("VMAD")).Content.ToArray().ShouldBe(Hex(vmad));
    }

    [Fact]
    public void Perk()
    {
        foreach (var perk in ReadBoth<IPerkGetter>(Record("PERK", Subrecord("VMAD", PerkVmad)), f => Mutagen.Bethesda.Starfield.Perk.CreateFromBinary(f), (s, p) => PerkBinaryOverlay.PerkFactory(s, p)))
        {
            var frag = perk.VirtualMachineAdapter!.ScriptFragments!.Fragments.ShouldHaveSingleItem();
            frag.Index.ShouldBe(0u);
            frag.Unknown2.ShouldBe((sbyte)1);
            frag.FragmentName.ShouldStartWith("Fragment_Entry_");
            WritesBack(perk, PerkVmad);
        }
    }

    [Fact]
    public void TerminalMenu()
    {
        foreach (var menu in ReadBoth<ITerminalMenuGetter>(Record("TMLM", Subrecord("VMAD", TerminalMenuVmad)), f => Mutagen.Bethesda.Starfield.TerminalMenu.CreateFromBinary(f), (s, p) => TerminalMenuBinaryOverlay.TerminalMenuFactory(s, p)))
        {
            var frag = menu.VirtualMachineAdapter!.ScriptFragments!.Fragments.ShouldHaveSingleItem();
            frag.FragmentIndex.ShouldBe(1u);
            frag.FragmentName.ShouldBe("Fragment_TerminalMenu_01");
            WritesBack(menu, TerminalMenuVmad);
        }
    }

    [Fact]
    public void Quest()
    {
        foreach (var quest in ReadBoth<IQuestGetter>(Record("QUST", Subrecord("VMAD", QuestVmad)), f => Mutagen.Bethesda.Starfield.Quest.CreateFromBinary(f), (s, p) => QuestBinaryOverlay.QuestFactory(s, p)))
        {
            var frag = quest.VirtualMachineAdapter!.Fragments.ShouldHaveSingleItem();
            frag.Stage.ShouldBe(5u);
            frag.StageIndex.ShouldBe(0u);
            frag.FragmentName.ShouldBe("Fragment_Stage_0005_Item_00");
            WritesBack(quest, QuestVmad);
        }
    }
}
