using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Raw, unknown or wrongly named fields of seven small records get names and types (wbDefinitionsSF1 and the data):
// - MAAM SNAM: Inner/Outer Cone Angle were swapped, the two "SnapSteeringMultiplier" floats are the Acceleration and
//   Deceleration in degrees per second (8000 and -8000), MaxAimAssistDistance is the Actor Target Finder Max Distance,
//   and the last float (Unknown) is the Max Capsule Angle For Vertical Aiming Correction (10).
// - SPCH: SRAN is Sequential Scenes (4), SGEN is Don't Allow No Keyword Scenes (43).
// - VTYP: PNAM is the Pronoun Override enum (683, all 0 = Unselected).
// - PCBN: NAM3/NAM4 Count/Distribution Curve were marked unknown; the names were already xEdit's.
// - CSTY FNAM (unnamed in xEdit) is per-template-slot flags: bit i is set exactly where UNAM slot i holds the
//   Template Default Combat Style (160 of 161 combat styles; the other has all bits and no templates).
// - WATR WASH is the Sound (xEdit's default sound reference name).
// - SPGD: # of Subtextures X/Y are UInt32 in xEdit; Mutagen read them as floats (the only SPGD stores 4, which read
//   as 5.6E-45).
// Each test reads a whole record from Starfield.esm with both readers and writes it back byte for byte.
public class SmallRecordFieldNameTests
{
    private static void WritesBack(Action<MutagenWriter> write, string hex)
    {
        var written = GameConstants.Starfield.MajorRecord(Write(write));
        written.Content.ToArray().ShouldBe(Hex(hex));
    }

    // MeleeBase_AA (1E810B:Starfield.esm)
    private const string MeleeAim = "454449440D004D656C6565426173655F414100534E414D1D000000344300006143000061430000FA450000FAC50000E0400100002041";
    // RI02_Arthur_EasySpeechChallenge (25D687:Starfield.esm)
    private const string Arthur = "454449442000524930325F4172746875725F456173795370656563684368616C6C656E6765005350574902005E0153504C4F020063015352414E00005347454E00005350515504009CAC2A004B53495A0400010000004B57444104009BD6250053504D41100084D6250083D6250082D6250085D625004449464602000000";
    // OE_KT_MineAlone_SpeechChallenge (04C229:Starfield.esm)
    private const string MineAlone = "4544494420004F455F4B545F4D696E65416C6F6E655F5370656563684368616C6C656E6765005350574902000A0053504C4F020014005347454E000053505155040008C404004B53495A0400030000004B5744410C00273B0100A14E01002B3B010053504D4108002A3B010090BB27004449464602000000";
    // GenericMaleRough (00002D:Starfield.esm)
    private const string MaleRough = "45444944110047656E657269634D616C65526F75676800444E414D010000504E414D010000";
    // PCM_CellLoad_Hazards_GasVents_ExtremeHeat (1A2D2E:Starfield.esm)
    private const string GasVents = "454449442A0050434D5F43656C6C4C6F61645F48617A617264735F47617356656E74735F45787472656D654865617400424643423300424753506C616E6574436F6E74656E744D616E61676572436F6E74656E7450726F706572746965735F436F6D706F6E656E74005A4E414D040000000000594E414D010000584E414D040000000000574E414D040000000000564E414D010000554E414D0400000000004E414D310400000048434E414D32040004442A004E414D330400000000004E414D340400AAAAFF004E414D350400000000004E414D360400000000004E414D370400000000004E414D380100004E414D39040000000000434954430400000000004246434500004E414D310400020000004E414D320400000000004E414D330400AD9F1F004E414D340400AE9F1F004E414D35010000434954430400000000004B5744410400CA8421005052544E0400352D1A005052564E0400312D1A00";
    // _RENAME_TestSpaceParticles (249FB5:Starfield.esm)
    private const string SpaceParticles = "454449441B005F52454E414D455F5465737453706163655061727469636C657300444154416000000000000000000000000000FF7FFF7F9A99993F000000007B148E3F00000000000000000000000000000000FFFFFFFF0000803F00000000040000000000000004000000000000000200000000000000A00F0000FFFFFF7FAE47613E000000004D4E414D2000456666656374735C5370616365447573745061727469636C65732E4247454D00";

    [Fact]
    public void MeleeAimAssistModel_Names()
    {
        foreach (var maam in ReadBoth<IMeleeAimAssistModelGetter>(Record("MAAM", Hex(MeleeAim)), f => MeleeAimAssistModel.CreateFromBinary(f), (s, p) => MeleeAimAssistModelBinaryOverlay.MeleeAimAssistModelFactory(s, p)))
        {
            maam.InnerConeAngleDegrees.ShouldBe(180f);
            maam.OuterConeAngleDegrees.ShouldBe(225f);
            maam.SteeringDegreesPerSec.ShouldBe(225f);
            maam.AccelerationDegreesPerSec.ShouldBe(8000f);
            maam.DecelerationDegreesPerSec.ShouldBe(-8000f);
            maam.ActorTargetFinderMaxDistance.ShouldBe(7f);
            maam.MeleeAimAssistEnabled.ShouldBeTrue();
            maam.MaxCapsuleAngleForVerticalAimingCorrectionDegrees.ShouldBe(10f);
            WritesBack(w => maam.WriteToBinary(w), MeleeAim);
        }
    }

    [Fact]
    public void SpeechChallenge_Markers()
    {
        foreach (var spch in ReadBoth<ISpeechChallengeGetter>(Record("SPCH", Hex(Arthur)), f => SpeechChallenge.CreateFromBinary(f), (s, p) => SpeechChallengeBinaryOverlay.SpeechChallengeFactory(s, p)))
        {
            spch.SequentialScenes.ShouldBeTrue();
            spch.DontAllowNoKeywordScenes.ShouldBeTrue();
            WritesBack(w => spch.WriteToBinary(w), Arthur);
        }
        foreach (var spch in ReadBoth<ISpeechChallengeGetter>(Record("SPCH", Hex(MineAlone)), f => SpeechChallenge.CreateFromBinary(f), (s, p) => SpeechChallengeBinaryOverlay.SpeechChallengeFactory(s, p)))
        {
            spch.SequentialScenes.ShouldBeFalse();
            spch.DontAllowNoKeywordScenes.ShouldBeTrue();
            WritesBack(w => spch.WriteToBinary(w), MineAlone);
        }
    }

    [Fact]
    public void VoiceType_PronounOverride()
    {
        foreach (var vtyp in ReadBoth<IVoiceTypeGetter>(Record("VTYP", Hex(MaleRough)), f => VoiceType.CreateFromBinary(f), (s, p) => VoiceTypeBinaryOverlay.VoiceTypeFactory(s, p)))
        {
            vtyp.PronounOverride.ShouldBe(VoiceType.Pronoun.Unselected);
            WritesBack(w => vtyp.WriteToBinary(w), MaleRough);
        }
    }

    [Fact]
    public void PlanetContentManagerBranchNode_Curves()
    {
        foreach (var pcbn in ReadBoth<IPlanetContentManagerBranchNodeGetter>(Record("PCBN", Hex(GasVents)), f => PlanetContentManagerBranchNode.CreateFromBinary(f), (s, p) => PlanetContentManagerBranchNodeBinaryOverlay.PlanetContentManagerBranchNodeFactory(s, p)))
        {
            // PCM_CellLoad_SpawnGroupCountCurveSmall (1F9FAD:Starfield.esm), PCM_CellLoad_SpawnGroupDistCurveSmall (1F9FAE:Starfield.esm)
            pcbn.CountCurve.FormKey.ID.ShouldBe(0x1F9FADu);
            pcbn.DistributionCurve.FormKey.ID.ShouldBe(0x1F9FAEu);
            WritesBack(w => pcbn.WriteToBinary(w), GasVents);
        }
    }

    [Fact]
    public void ShaderParticleGeometry_SubtextureCounts()
    {
        foreach (var spgd in ReadBoth<IShaderParticleGeometryGetter>(Record("SPGD", Hex(SpaceParticles)), f => ShaderParticleGeometry.CreateFromBinary(f), (s, p) => ShaderParticleGeometryBinaryOverlay.ShaderParticleGeometryFactory(s, p)))
        {
            spgd.NumSubtexturesX.ShouldBe(4u);
            spgd.NumSubtexturesY.ShouldBe(4u);
            WritesBack(w => spgd.WriteToBinary(w), SpaceParticles);
        }
    }

    // csSpacer_Heavy (0D2143:Starfield.esm): template slots 1 (Avoid Threats) and 4 (Ranged) hold another style, the
    // other 15 hold the template default, so FNAM is 0x1FFED
    private const string SpacerHeavy = "454449440F0063735370616365725F486561767900435347443C000000803F000000000000003F00000000000000000000004000000000CDCCCC3D0000000000000000000000000000000000000000000020410000803F43534D4528000000000000000000000000000000803F0000803F0000803F0000803F000000000000803F000000004353524118009999993E9999993ECCCCCC3DCDCC4C3E0000003F0000803F43534352340000000000000000000000803F0000803F0000000000000000000000000000000000000000000000000000803F000000000000000043534C5224000000003F00000000000000000000803F0000803F0000803F00000000000000000000000043534356080000000000000000004353464C200000000000000000000000000000000000000000000000000000000000000000004353544E20000000803F0000803F00007A440000803F0000004000000041000020410000A0404353534778000000003F0000003FCDCC4C3E0000803F0000803F0000803F0000803F0000803F0000803F0000803F0000803F0000803F0000803F0000803F0000803F0000803F0000803F000000000000803F00007042010100009A99993ECDCC4C3E9A99993E0000803ECDCC4C3D3333333F0000803E3333333FCDCC4C3D4353534D40000000A040000070410000A0400000A0400000204100000000000000000000A04000004040000020410000404000004040000070410000000000000000000000004353535254000000003F0000003F0000003F9A99993ECDCC4C3E9A99993E0000803ECDCC4C3D3333333F0000803E3333333FCDCC4C3D9A99993ECDCC4C3E9A99993E0000803ECDCC4C3D3333333F0000803E3333333FCDCC4C3D4353534128000000003F9A99993ECDCC4C3E9A99993E0000803ECDCC4C3D3333333F0000803E3333333FCDCC4C3D435353440C000000FA4400007A450080BB4544415441040021000000464E414D0400EDFF0100544E414D04003A470500554E414D44003A4705000F562C003A4705003A4705000F562C003A4705003A4705003A4705003A4705003A4705003A4705003A4705003A4705003A4705003A4705003A4705003A470500";

    // WaterSulfuric (0017E9:Starfield.esm)
    private const string Sulfuric = "454449440E00576174657253756C667572696300414E414D01005D464E414D010000574153482800000000000000000000000000000000000000000000000000000000000000000000000000FBEB1F00584E414D0400BF4F0200594E414D04004F761F00444154410000444E414D9800CCCC5C409A99993E9A99993D0AD7233C00000000B4C89A410000A041000000008C8D2500CDCC4C3F000080BF000048440000803F62BC7F3FB1DE643F37557F3FCDCCCC3E0000003F9A99793F0000803FCDCC4C3D0000204200009C4200003243E3A59B3C3108AC3C0AD7A33CAA60143E32C4B13C7346943C008059440000114300000000BC7453452A18A545000000460000803FCDCC4C3D474E414D0C000000000000000000000000004E414D300C000AD7A33D0AD7A3BC000000004E414D310C00000000000000000000000000454E414D0400C53B2C00484E414D0400C43B2C004A4E414D0400C83B2C004C4E414D0400C73B2C004D4E414D0400C63B2C00514E414D040064991000554E414D0400CD3B2C00";

    [Fact]
    public void CombatStyle_DefaultTemplateFlags()
    {
        foreach (var csty in ReadBoth<ICombatStyleGetter>(Record("CSTY", Hex(SpacerHeavy)), f => CombatStyle.CreateFromBinary(f), (s, p) => CombatStyleBinaryOverlay.CombatStyleFactory(s, p)))
        {
            var all = (CombatStyle.TemplateFlag)0x1FFFF;
            csty.DefaultTemplateFlags.ShouldBe(all & ~CombatStyle.TemplateFlag.AvoidThreats & ~CombatStyle.TemplateFlag.Ranged);
            var templates = csty.Templates.ShouldNotBeNull();
            templates.AvoidThreats.FormKey.ShouldNotBe(csty.TemplateDefaultCombatStyle.FormKey);
            templates.Melee.FormKey.ShouldBe(csty.TemplateDefaultCombatStyle.FormKey);
            WritesBack(w => csty.WriteToBinary(w), SpacerHeavy);
        }
    }

    [Fact]
    public void Water_Sound()
    {
        foreach (var water in ReadBoth<IWaterGetter>(Record("WATR", Hex(Sulfuric)), f => Water.CreateFromBinary(f), (s, p) => WaterBinaryOverlay.WaterFactory(s, p)))
        {
            water.Sound.ShouldNotBeNull();
            WritesBack(w => water.WriteToBinary(w), Sulfuric);
        }
    }
}
