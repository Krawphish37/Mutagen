using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Condition parameters xEdit names as enums that Mutagen kept as Int32: casting source (8 functions), GetCrime's
// crime type, GetCauseofDeath's damage cause, GetIsAlignment, IsInCriticalStage and IsWardState.
// Each CTDA is copied whole from Starfield.esm and read inside an INFO (INFO conditions have no CITC).
public class ConditionEnumTests
{
    private static IConditionDataGetter ReadOne(string ctda, out byte[] written)
    {
        IConditionDataGetter? data = null;
        written = Array.Empty<byte>();
        foreach (var info in ReadBoth<IDialogResponsesGetter>(Record("INFO", Subrecord("CTDA", ctda)), f => DialogResponses.CreateFromBinary(f), (s, p) => DialogResponsesBinaryOverlay.DialogResponsesFactory(s, p)))
        {
            data = info.Conditions.Single().Data;
            written = GameConstants.Starfield.MajorRecord(Write(w => info.WriteToBinary(w))).FindSubrecord(new RecordType("CTDA")).Content.ToArray();
            written.ShouldBe(Hex(ctda));
        }
        return data!;
    }

    [Fact]
    public void IsCurrentSpell_CastSource()
    {
        // INFO 1A93AF: IsCurrentSpell, spell 23AF02, casting source Voice
        var data = ReadOne("00-00-00-00-00-00-80-3F-53-02-00-00-02-AF-23-00-02-00-00-00-00-00-00-00-00-00-00-00-FF-FF-FF-FF", out _);
        data.ShouldBeAssignableTo<IIsCurrentSpellConditionDataGetter>()!.SecondParameter.ShouldBe(CastSource.Voice);
    }

    [Fact]
    public void GetCrime_CrimeType()
    {
        // INFO 2312DA: GetCrime, the player, Smuggling
        var data = ReadOne("00-00-00-00-00-00-80-3F-7A-00-00-00-14-00-00-00-07-00-00-00-00-00-00-00-00-00-00-00-FF-FF-FF-FF", out _);
        data.ShouldBeAssignableTo<IGetCrimeConditionDataGetter>()!.SecondParameter.ShouldBe(CrimeType.Smuggling);
    }

    [Fact]
    public void GetCauseofDeath_DamageCauseType()
    {
        // From Skill_HeavyWeaponCertification (PERK 147E38), a rank activity condition: GetCauseofDeath Gun, run on Target.
        // The function's padding bytes (F9-44) are kept.
        var data = ReadOne("01-00-00-00-00-00-80-3F-8C-01-F9-44-02-00-00-00-00-00-00-00-01-00-00-00-00-00-00-00-FF-FF-FF-FF", out _);
        data.ShouldBeAssignableTo<IGetCauseofDeathConditionDataGetter>()!.FirstParameter.ShouldBe(DamageCauseType.Gun);
    }

    [Fact]
    public void EnumValues()
    {
        ((int)CrimeType.None).ShouldBe(-1);
        ((int)DamageCauseType.ShipDestroyed).ShouldBe(11);
        ((int)CriticalStage.FreezeEnd).ShouldBe(6);
        ((int)Alignment.VeryEvil).ShouldBe(4);
        ((int)WardState.Break).ShouldBe(2);
        ((int)CastSource.Instant).ShouldBe(3);
    }
}
