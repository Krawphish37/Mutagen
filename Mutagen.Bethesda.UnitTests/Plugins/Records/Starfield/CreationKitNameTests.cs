using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Names read in the Creation Kit 1.16.244.0 (nothing saved there), tested on real bytes.
public class CreationKitNameTests
{
    // SpaceshipPartLinkedVehicle (000039:SFBGS004.esm): Type = Form Link (TNAM 30), FNAM 47 = the greyed target type
    // drop-down under Type.
    private const string LinkedVehicleKeyword =
        "454449441B00537061636573686970506172744C696E6B656456656869636C6500434E414D0400FFFFFF00544E414D04001E000000464E414D04002F000000";

    // EncShip_UCCitizen_F02 (0189BB:Starfield.esm): the External Data Source component, as stored. EXBS "UC CITIZEN"
    // is the Template Subdata of the Ship Modules row on the CK's Data Templates tab.
    private const string ExternalDataSource =
        "42464342290042475345787465726E616C436F6D706F6E656E7444617461536F757263655F436F6D706F6E656E7400455844430400B7A70C004558445A0400090000004558434E1400426C75657072696E745F436F6D706F6E656E7400455843490400B7A70C004558434E2500424753537061636573686970576561706F6E42696E64696E67735F436F6D706F6E656E7400455843490400B7A70C004558434E19004247534B6579776F7264466F726D5F436F6D706F6E656E7400455843490400B7A70C004558434E1700544553436F6E7461696E65725F436F6D706F6E656E7400455843490400B7A70C004558434E2500424753416464546F496E76656E746F72794F6E44657374726F795F436F6D706F6E656E7400455843490400B7A70C004558434E160054455346756C6C4E616D655F436F6D706F6E656E7400455843490400B7A70C004558434E200042475353706163657368697045717569706D656E745F436F6D706F6E656E7400455843490400B7A70C004558434E1F0042475353706163657368697048756C6C436F64655F436F6D706F6E656E7400455843490400B7A70C004558434E1B0042475350726F706572747953686565745F436F6D706F6E656E7400455843490400B7A70C00455841430400020000004558415319004247534B6579776F7264466F726D5F436F6D706F6E656E7400455841531700544553436F6E7461696E65725F436F6D706F6E656E7400455842530B00554320434954495A454E00424643450000";

    // SE_AG01 (0013DD:Starfield.esm): the location alias "OrbitLocation" (ALLS .. ALED), ALFG 2 = "Force to Player
    // location in Cruise Mode" (ticked in the CK on this alias, not on DerelictShipInterior).
    private const string OrbitLocationAlias =
        "414C4C5304000F000000" +
        "414C49440E004F726269744C6F636174696F6E00" +
        "464E414D040001000120" +
        "414C464704000200000" + "0" +
        "435444412000000300000000000032020000D1532200000000000000000000000000FFFFFFFF" +
        "414C5359040008000000" +
        "414C4B460400" + "04650100" +
        "414C45440000";

    private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    [Fact]
    public void Keyword_FormLinkTargetType()
    {
        foreach (var kywd in ReadBoth<IKeywordGetter>(Record("KYWD", Hex(LinkedVehicleKeyword)), f => Keyword.CreateFromBinary(f), (s, p) => KeywordBinaryOverlay.KeywordFactory(s, p)))
        {
            kywd.FormLinkTargetType.ShouldBe(47u);
            var written = GameConstants.Starfield.MajorRecord(Write(w => kywd.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(LinkedVehicleKeyword));
        }
    }

    [Fact]
    public void ExternalDataSource_TemplateSubdata()
    {
        var component = Hex(ExternalDataSource);
        var bytes = Record("GBFM", Subrecord("EDID", System.Text.Encoding.ASCII.GetBytes("EncShip_UCCitizen_F02\0")), component);
        foreach (var gbfm in ReadBoth<IGenericBaseFormGetter>(bytes, f => GenericBaseForm.CreateFromBinary(f), (s, p) => GenericBaseFormBinaryOverlay.GenericBaseFormFactory(s, p)))
        {
            var comp = gbfm.Components.OfType<IExternalDataSourceComponentGetter>().Single();
            comp.TemplateSubdata.ShouldBe("UC CITIZEN");
            comp.Sources.Count.ShouldBe(9);
            comp.EXASs.ShouldBe(new[] { "BGSKeywordForm_Component", "TESContainer_Component" });
            var written = GameConstants.Starfield.MajorRecord(Write(w => gbfm.WriteToBinary(w)));
            Contains(written.Content.ToArray(), component).ShouldBeTrue();
        }
    }

    [Fact]
    public void LocationAlias_ForceToPlayerLocationInCruiseMode()
    {
        var alias = Hex(OrbitLocationAlias);
        var bytes = Record("QUST", Subrecord("EDID", System.Text.Encoding.ASCII.GetBytes("SE_AG01\0")), alias);
        foreach (var qust in ReadBoth<IQuestGetter>(bytes, f => Quest.CreateFromBinary(f), (s, p) => QuestBinaryOverlay.QuestFactory(s, p)))
        {
            var orbit = qust.Aliases!.OfType<IQuestLocationAliasGetter>().Single();
            orbit.Name.ShouldBe("OrbitLocation");
            orbit.ExtraFlags.ShouldBe(QuestLocationAlias.ExtraFlag.ForceToPlayerLocationInCruiseMode);
            var written = GameConstants.Starfield.MajorRecord(Write(w => qust.WriteToBinary(w)));
            Contains(written.Content.ToArray(), alias).ShouldBeTrue();
        }
    }

    [Fact]
    public void LocationAlias_ExtraFlags_KeepUnknownBits()
    {
        var alias = Hex(OrbitLocationAlias.Replace("414C464704000200000" + "0", "414C46470400" + "0A000080"));
        var bytes = Record("QUST", Subrecord("EDID", System.Text.Encoding.ASCII.GetBytes("SE_AG01\0")), alias);
        foreach (var qust in ReadBoth<IQuestGetter>(bytes, f => Quest.CreateFromBinary(f), (s, p) => QuestBinaryOverlay.QuestFactory(s, p)))
        {
            var orbit = qust.Aliases!.OfType<IQuestLocationAliasGetter>().Single();
            ((uint)(int)orbit.ExtraFlags!.Value).ShouldBe(0x8000000Au);
            var written = GameConstants.Starfield.MajorRecord(Write(w => qust.WriteToBinary(w)));
            Contains(written.Content.ToArray(), alias).ShouldBeTrue();
        }
    }

    // SFFL_SE_EC01 (040703:SFBGS00D.esm): the ref collection alias SpaceCellObjectRefs (ALCS .. ALED), ALAM 2.0 = the
    // CK's "Orbit Altitude Mult" on the Fill Type tab. Its only FormID (ALCO 1D8555) is a Starfield.esm one (index 00).
    private const string SpaceCellObjectRefsAlias =
        "414C435304000000000041" + "4C4D49010001" +
        "414C414D040000000040" +
        "414C535404000000000041" + "4C494414005370616365436" + "56C6C4F626A6563745265667300" +
        "464E414D040008000800" +
        "414C4647040000000000" +
        "414C434F040055851D00" +
        "414C434104006300000041" + "4C434C040000000000" +
        "5654434B040000000000" +
        "414C45440000";

    [Fact]
    public void CollectionAlias_OrbitAltitudeMult()
    {
        var alias = Hex(SpaceCellObjectRefsAlias);
        var bytes = Record("QUST", Subrecord("EDID", System.Text.Encoding.ASCII.GetBytes("SFFL_SE_EC01\0")), alias);
        foreach (var qust in ReadBoth<IQuestGetter>(bytes, f => Quest.CreateFromBinary(f), (s, p) => QuestBinaryOverlay.QuestFactory(s, p)))
        {
            var collection = qust.Aliases!.OfType<IQuestCollectionAliasGetter>().Single().Collection.Single();
            collection.OrbitAltitudeMult.ShouldBe(2.0f);
            collection.ReferenceAlias.Name.ShouldBe("SpaceCellObjectRefs");
            var written = GameConstants.Starfield.MajorRecord(Write(w => qust.WriteToBinary(w)));
            Contains(written.Content.ToArray(), alias).ShouldBeTrue();
        }
    }

    // SCEN VNAM: the five "Set all" overrides of Edit Actor Behavior (Death, Combat, Player Dialogue, Observe Combat,
    // Reaction Radius). 3 = not set.
    // - MQ101_012_VascoShipScene (0058BD:Starfield.esm): 3,3,3,3,0 (the CK shows only Reaction Radius checked, Normal);
    // - MQ101_005_FaceGenScene (005743:Starfield.esm): 3,3,0,3,0;
    // - 2,2,3,1,0: a real shape (13 scenes) with End and Pause;
    // - CF01_Stage130_Kibwe_OfferScene (00D360:Starfield.esm): an empty VNAM.
    [Theory]
    [InlineData("MQ101_012_VascoShipScene", "0300000003000000030000000300000000000000")]
    [InlineData("MQ101_005_FaceGenScene", "0300000003000000000000000300000000000000")]
    [InlineData("EndPauseShape", "0200000002000000030000000100000000000000")]
    [InlineData("UnknownValue", "0300000003000000030000000300000007000000")]
    [InlineData("CF01_Stage130_Kibwe_OfferScene", "")]
    public void Scene_ActorBehaviorOverrides(string edid, string vnam)
    {
        var bytes = Record("SCEN",
            Subrecord("EDID", System.Text.Encoding.ASCII.GetBytes(edid + "\0")),
            Subrecord("VNAM", Hex(vnam)));
        foreach (var scen in ReadBoth<ISceneGetter>(bytes, f => Scene.CreateFromBinary(f), (s, p) => SceneBinaryOverlay.SceneFactory(s, p)))
        {
            if (vnam.Length == 0)
            {
                scen.VNAMDataTypeState.HasFlag(Scene.VNAMDataType.Break0).ShouldBeTrue();
            }
            else
            {
                var values = Enumerable.Range(0, 5).Select(i => (Scene.ActorBehaviorSetting)BitConverter.ToInt32(Hex(vnam), i * 4)).ToArray();
                new[] { scen.DeathBehavior, scen.CombatBehavior, scen.PlayerDialogueBehavior, scen.ObserveCombatBehavior, scen.ReactionRadiusBehavior }
                    .ShouldBe(values);
            }
            var written = GameConstants.Starfield.MajorRecord(Write(w => scen.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("VNAM")).Content.ToArray().ShouldBe(Hex(vnam));
        }
        if (edid == "MQ101_012_VascoShipScene")
        {
            var vasco = Scene.CreateFromBinary(new Mutagen.Bethesda.Plugins.Binary.Streams.MutagenFrame(
                new Mutagen.Bethesda.Plugins.Binary.Streams.MutagenMemoryReadStream(bytes, Meta())));
            vasco.DeathBehavior.ShouldBe(Scene.ActorBehaviorSetting.NotSet);
            vasco.ReactionRadiusBehavior.ShouldBe(Scene.ActorBehaviorSetting.Normal);
        }
    }
}
