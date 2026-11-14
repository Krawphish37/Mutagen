using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C218: names and values taken from the game's own reflection data (Starfield.exe 1.16.244, BSReflection dump; C30).
public class EngineNameTests
{
    // GetCauseofDeath (396) from Skill_HeavyWeaponCertification (PERK 147E38), with its damage cause set to 12 and 13.
    // The engine's DamageCauseType: ... Ship_Destroyed = 11, Vehicle_Impact = 12, Other = 13, MaxCount = 14.
    [Theory]
    [InlineData("0C", DamageCauseType.VehicleImpact)]
    [InlineData("0D", DamageCauseType.Other)]
    public void DamageCauseType_EngineValues(string cause, DamageCauseType expected)
    {
        var ctda = $"01-00-00-00-00-00-80-3F-8C-01-F9-44-{cause}-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-FF-FF-FF-FF";
        foreach (var info in ReadBoth<IDialogResponsesGetter>(Record("INFO", Subrecord("CTDA", ctda)), f => DialogResponses.CreateFromBinary(f), (s, p) => DialogResponsesBinaryOverlay.DialogResponsesFactory(s, p)))
        {
            info.Conditions.Single().Data.ShouldBeAssignableTo<IGetCauseofDeathConditionDataGetter>()!.FirstParameter.ShouldBe(expected);
            GameConstants.Starfield.MajorRecord(Write(w => info.WriteToBinary(w))).FindSubrecord(new RecordType("CTDA")).Content.ToArray().ShouldBe(Hex(ctda));
        }
        ((int)DamageCauseType.VehicleImpact).ShouldBe(12);
        ((int)DamageCauseType.Other).ShouldBe(13);
    }

    // The engine's PronounType: Unselected = 0, He_Him, She_Her, They_Them (as VoiceType.Pronoun already has it).
    [Theory]
    [InlineData("00", Pronoun.Unselected)]
    [InlineData("02", Pronoun.SheHer)]
    public void NpcPronoun_EngineNames(string pron, Pronoun expected)
    {
        ((int)Pronoun.Unselected).ShouldBe(0);
        var bytes = Record("NPC_", Subrecord("EDID", System.Text.Encoding.ASCII.GetBytes("TestNpc\0")), Subrecord("PRON", pron));
        foreach (var npc in ReadBoth<INpcGetter>(bytes, f => Npc.CreateFromBinary(f), (s, p) => NpcBinaryOverlay.NpcFactory(s, p)))
        {
            npc.Pronoun.ShouldBe(expected);
            GameConstants.Starfield.MajorRecord(Write(w => npc.WriteToBinary(w))).FindSubrecord(new RecordType("PRON")).Content.ToArray().ShouldBe(Hex(pron));
        }
    }

    // BGSAudio::WwiseSoundHook { StartEvent, StopEvent, pConditionForm, pEventForm }; a player dialogue action's WED0 is one
    // (xEdit: "WED0 - Sound"). PSDC's and TODD's reflection root classes are BGSParticleSystemDefineCollection and BGSTimeOfDayData.
    [Fact]
    public void Names()
    {
        typeof(ISoundReferenceGetter).GetProperty("StartEvent")!.PropertyType.ShouldBe(typeof(Guid));
        typeof(ISoundReferenceGetter).GetProperty("StopEvent")!.PropertyType.ShouldBe(typeof(Guid));
        typeof(ISoundReferenceGetter).GetProperty("Start").ShouldBeNull();
        typeof(IPlayerDialogueSceneActionGetter).GetProperty("Sound")!.PropertyType.ShouldBe(typeof(ISoundReferenceGetter));
        typeof(IPlayerDialogueSceneActionGetter).GetProperty("WED0").ShouldBeNull();
        ParticleSystemDefineCollection_Registration.TriggeringRecordType.ShouldBe(new RecordType("PSDC"));
        TimeOfDayData_Registration.TriggeringRecordType.ShouldBe(new RecordType("TODD"));
        typeof(IStarfieldModGetter).GetProperty("ParticleSystemDefineCollections").ShouldNotBeNull();
        typeof(IStarfieldModGetter).GetProperty("TimeOfDays").ShouldNotBeNull();
    }
}
