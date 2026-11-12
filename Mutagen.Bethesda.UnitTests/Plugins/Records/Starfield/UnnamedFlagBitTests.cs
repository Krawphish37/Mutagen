using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C149: flag bits that Starfield.esm or the official plugins set but that had no enum member, so ToString() printed a
// number. Names follow xEdit's Starfield definitions; bits xEdit calls 'Unknown N' are UnknownN. Each record below sets
// every new bit, is written, and is read back by both readers.
public class UnnamedFlagBitTests
{
    private static FormKey Fk => new(TestModKey, 0x800);

    private static void ShouldBeNamed<T>(T value, T expected) where T : struct, Enum
    {
        value.ShouldBe(expected);
        var text = value.ToString();
        (char.IsDigit(text[0]) || text[0] == '-').ShouldBeFalse($"{typeof(T).Name} printed as a number: {text}");
    }

    public static IEnumerable<object[]> NewMembers() => new (Type Enum, string Name, ulong Value)[]
    {
        (typeof(ActorValueInformation.Flag), "Unknown6", 0x40),
        (typeof(ActorValueInformation.Flag), "Unknown13", 0x2000),
        (typeof(ActorValueInformation.Flag), "Unknown16", 0x1_0000),
        (typeof(ActorValueInformation.Flag), "Unknown25", 0x200_0000),
        (typeof(ActorValueInformation.Flag), "Unknown28", 0x1000_0000),
        (typeof(Book.Flag), "AddPerk", 0x10),
        (typeof(Book.Flag), "Unknown5", 0x20),
        (typeof(CameraPath.Flags), "Unknown3", 0x8),
        (typeof(CameraShot.Flag), "Unknown10", 0x400),
        (typeof(Cell.Flag), "Unknown14", 0x4000),
        (typeof(Cell.Flag), "Unknown15", 0x8000),
        (typeof(ConstructibleObject.Flag), "Unknown1", 0x2),
        (typeof(Container.Flag), "Unknown3", 0x8),
        (typeof(DialogResponses.Flag), "Unknown9", 0x200),
        (typeof(DialogTopic.TopicFlag), "Unknown1", 0x2),
        (typeof(DialogTopic.TopicFlag), "Unknown2", 0x4),
        (typeof(EffectShader.Flag), "Unknown3", 0x8),
        (typeof(Faction.FactionFlag), "ReportCrimesByMembersAgainstMembers", 0x4_0000),
        (typeof(IdleAnimation.Flag), "Unknown4", 0x10),
        (typeof(IdleMarker.Flag), "Unknown3", 0x8),
        (typeof(Light.Flag), "Unknown0", 0x1),
        (typeof(Light.Flag), "Unknown10", 0x400),
        (typeof(NavmeshTriangle.Flag), "Unknown12", 0x1000),
        (typeof(Npc.Flag), "Unknown8", 0x100),
        (typeof(Package.InterruptFlag), "Unknown8", 0x100),
        (typeof(Package.InterruptFlag), "Unknown12", 0x1000),
        (typeof(Package.InterruptFlag), "Unknown13", 0x2000),
        (typeof(Package.InterruptFlag), "Unknown14", 0x4000),
        (typeof(Package.InterruptFlag), "Unknown15", 0x8000),
        (typeof(PackageRoot.Flag), "Unknown1", 0x2),
        (typeof(PackIn.Flag), "Unknown0", 0x1),
        (typeof(PackIn.Flag), "Unknown7", 0x80),
        (typeof(PackIn.Flag), "Unknown30", 0x4000_0000),
        (typeof(ObjectPaletteDefaults.Flag), "Unknown12", 0x1000),
        (typeof(ObjectPaletteDefaults.Flag), "Unknown13", 0x2000),
        (typeof(PlacedObject.ActionFlag), "UseDefault", 0x1),
        (typeof(PlacedObject.ActionFlag), "Activate", 0x2),
        (typeof(PlacedObject.ActionFlag), "Open", 0x4),
        (typeof(PlacedObject.ActionFlag), "OpenByDefault", 0x8),
        (typeof(Quest.Flag), "Unknown16", 0x1_0000),
        (typeof(Quest.Flag), "Unknown17", 0x2_0000),
        (typeof(Quest.Flag), "ShipDialogueOnly", 0x4_0000),
        (typeof(Quest.Flag), "CanBeRejected", 0x8_0000),
        (typeof(Quest.Flag), "AudioLogOnly", 0x10_0000),
        (typeof(Quest.Flag), "DoNotStopInterruptPackagesOnAliasFill", 0x20_0000),
        (typeof(Quest.Flag), "DoNotDumpOldLocationEventParameter", 0x40_0000),
        (typeof(AQuestAlias.Flag), "MatchingRefFromCrowd", 0x200_0000),
        (typeof(AQuestAlias.Flag), "QuestObjectCargo", 0x400_0000),
        (typeof(AQuestAlias.Flag), "SearchShipLocations", 0x800_0000),
        (typeof(AQuestAlias.Flag), "MatchingLocPlanetsAndSystemsOnly", 0x1000_0000),
        (typeof(AQuestAlias.Flag), "CurrentAliasSystem", 0x2000_0000),
        (typeof(AQuestAlias.Flag), "CurrentAliasPlanet", 0x4000_0000),
        (typeof(AQuestAlias.Flag), "Consumable", 0x8000_0000),
        (typeof(QuestStage.Flag), "StartQuestTimer", 0x20),
        (typeof(QuestStage.Flag), "ReadyToDisplayOnUi", 0x40),
        (typeof(Race.Flag), "Unknown55", 1UL << 55),
        (typeof(ASceneAction.Flag), "RunOnEndOfPhase", 0x4000),
        (typeof(ASceneAction.Flag), "AnimationOnlyMovement", 0x80_0000),
        (typeof(Scene.Flag), "Unknown7", 0x80),
        (typeof(Scene.Flag), "Unknown21", 0x20_0000),
        (typeof(SceneActor.BehaviorFlag), "ReactionRadiusPause", 0x100),
        (typeof(SceneActor.BehaviorFlag), "ReactionRadiusEnd", 0x200),
        (typeof(ScenePhase.Flag), "FxActionsDoAllOnce", 0x10),
        (typeof(TextureSet.Flag), "DefaultToBoxDecal", 0x8),
    }.Select(x => new object[] { x.Enum, x.Name, x.Value });

    [Theory]
    [MemberData(nameof(NewMembers))]
    public void Member_HasXEditBit(Type enumType, string name, ulong value)
    {
        enumType.IsDefined(typeof(FlagsAttribute), false).ShouldBeTrue();
        var member = Enum.Parse(enumType, name);
        Convert.ToUInt64(Convert.ChangeType(member, Enum.GetUnderlyingType(enumType))).ShouldBe(value);
        Enum.ToObject(enumType, value).ToString().ShouldBe(name);
    }

    [Fact]
    public void ActorValueInformation_RoundTrips()
    {
        var flags = ActorValueInformation.Flag.Unknown6 | ActorValueInformation.Flag.Unknown13 | ActorValueInformation.Flag.Unknown16
            | ActorValueInformation.Flag.Unknown25 | ActorValueInformation.Flag.Unknown28;
        var rec = new ActorValueInformation(Fk, StarfieldRelease.Starfield) { Flags = flags };
        foreach (var r in ReadBoth<IActorValueInformationGetter>(Write(w => rec.WriteToBinary(w)),
                     f => ActorValueInformation.CreateFromBinary(f), (s, p) => ActorValueInformationBinaryOverlay.ActorValueInformationFactory(s, p)))
        {
            ShouldBeNamed(r.Flags!.Value, flags);
        }
    }

    [Fact]
    public void Book_RoundTrips()
    {
        // Skill_Magazine_TheNewAtlantian05 (BOOK 1FF722): a perk magazine with both bits
        var flags = Book.Flag.AddPerk | Book.Flag.Unknown5;
        var rec = new Book(Fk, StarfieldRelease.Starfield) { Flags = flags, Teaches = new BookPerk() };
        foreach (var r in ReadBoth<IBookGetter>(Write(w => rec.WriteToBinary(w)),
                     f => Book.CreateFromBinary(f), (s, p) => BookBinaryOverlay.BookFactory(s, p)))
        {
            ShouldBeNamed(r.Flags, flags);
            r.Teaches.ShouldBeAssignableTo<IBookPerkGetter>();
        }
    }

    [Fact]
    public void CameraPath_RoundTrips()
    {
        var rec = new CameraPath(Fk, StarfieldRelease.Starfield) { Zoom = CameraPath.Flags.Unknown3 };
        foreach (var r in ReadBoth<ICameraPathGetter>(Write(w => rec.WriteToBinary(w)),
                     f => CameraPath.CreateFromBinary(f), (s, p) => CameraPathBinaryOverlay.CameraPathFactory(s, p)))
        {
            ShouldBeNamed(r.Zoom, CameraPath.Flags.Unknown3);
        }
    }

    [Fact]
    public void CameraShot_RoundTrips()
    {
        var rec = new CameraShot(Fk, StarfieldRelease.Starfield) { Flags = CameraShot.Flag.Unknown10 };
        foreach (var r in ReadBoth<ICameraShotGetter>(Write(w => rec.WriteToBinary(w)),
                     f => CameraShot.CreateFromBinary(f), (s, p) => CameraShotBinaryOverlay.CameraShotFactory(s, p)))
        {
            ShouldBeNamed(r.Flags, CameraShot.Flag.Unknown10);
        }
    }

    [Fact]
    public void Cell_RoundTrips()
    {
        var flags = Cell.Flag.IsInteriorCell | Cell.Flag.Unknown14 | Cell.Flag.Unknown15;
        var rec = new Cell(Fk, StarfieldRelease.Starfield) { Flags = flags };
        foreach (var r in ReadBoth<ICellGetter>(Write(w => rec.WriteToBinary(w)),
                     f => Cell.CreateFromBinary(f), (s, p) => CellBinaryOverlay.CellFactory(s, p)))
        {
            ShouldBeNamed(r.Flags!.Value, flags);
        }
    }

    [Fact]
    public void ConstructibleObject_RoundTrips()
    {
        var rec = new ConstructibleObject(Fk, StarfieldRelease.Starfield) { Flags = ConstructibleObject.Flag.Unknown1 };
        foreach (var r in ReadBoth<IConstructibleObjectGetter>(Write(w => rec.WriteToBinary(w)),
                     f => ConstructibleObject.CreateFromBinary(f), (s, p) => ConstructibleObjectBinaryOverlay.ConstructibleObjectFactory(s, p)))
        {
            ShouldBeNamed(r.Flags!.Value, ConstructibleObject.Flag.Unknown1);
        }
    }

    [Fact]
    public void Container_RoundTrips()
    {
        var rec = new Container(Fk, StarfieldRelease.Starfield) { Flags = Container.Flag.Unknown3 };
        foreach (var r in ReadBoth<IContainerGetter>(Write(w => rec.WriteToBinary(w)),
                     f => Container.CreateFromBinary(f), (s, p) => ContainerBinaryOverlay.ContainerFactory(s, p)))
        {
            ShouldBeNamed(r.Flags, Container.Flag.Unknown3);
        }
    }

    [Fact]
    public void DialogResponses_RoundTrips()
    {
        var rec = new DialogResponses(Fk, StarfieldRelease.Starfield) { Flags = new DialogResponseFlags { Flags = DialogResponses.Flag.Unknown9 } };
        foreach (var r in ReadBoth<IDialogResponsesGetter>(Write(w => rec.WriteToBinary(w)),
                     f => DialogResponses.CreateFromBinary(f), (s, p) => DialogResponsesBinaryOverlay.DialogResponsesFactory(s, p)))
        {
            ShouldBeNamed(r.Flags!.Flags, DialogResponses.Flag.Unknown9);
        }
    }

    [Fact]
    public void DialogTopic_RoundTrips()
    {
        var flags = DialogTopic.TopicFlag.Unknown1 | DialogTopic.TopicFlag.Unknown2;
        var rec = new DialogTopic(Fk, StarfieldRelease.Starfield) { TopicFlags = flags };
        foreach (var r in ReadBoth<IDialogTopicGetter>(Write(w => rec.WriteToBinary(w)),
                     f => DialogTopic.CreateFromBinary(f), (s, p) => DialogTopicBinaryOverlay.DialogTopicFactory(s, p)))
        {
            ShouldBeNamed(r.TopicFlags, flags);
        }
    }

    [Fact]
    public void EffectShader_RoundTrips()
    {
        var rec = new EffectShader(Fk, StarfieldRelease.Starfield) { Flags = EffectShader.Flag.Unknown3 };
        foreach (var r in ReadBoth<IEffectShaderGetter>(Write(w => rec.WriteToBinary(w)),
                     f => EffectShader.CreateFromBinary(f), (s, p) => EffectShaderBinaryOverlay.EffectShaderFactory(s, p)))
        {
            ShouldBeNamed(r.Flags, EffectShader.Flag.Unknown3);
        }
    }

    [Fact]
    public void Faction_ReportCrimesByMembersAgainstMembers_IsBit18()
    {
        // CrimeFactionCrimsonFleet (FACT 010B30). The member was 0x30000, which is Ignore Piracy | Ignore Smuggling.
        var rec = new Faction(Fk, StarfieldRelease.Starfield) { Flags = Faction.FactionFlag.ReportCrimesByMembersAgainstMembers };
        var bytes = Write(w => rec.WriteToBinary(w));
        BitConverter.ToUInt32(GameConstants.Starfield.MajorRecord(bytes).FindSubrecord(new RecordType("DATA")).Content.Slice(0, 4)).ShouldBe(0x4_0000u);
        foreach (var r in ReadBoth<IFactionGetter>(bytes,
                     f => Faction.CreateFromBinary(f), (s, p) => FactionBinaryOverlay.FactionFactory(s, p)))
        {
            ShouldBeNamed(r.Flags, Faction.FactionFlag.ReportCrimesByMembersAgainstMembers);
        }
        (Faction.FactionFlag.IgnorePiracy | Faction.FactionFlag.IgnoreSmuggling).ToString().ShouldBe("IgnorePiracy, IgnoreSmuggling");
    }

    [Fact]
    public void IdleAnimation_RoundTrips()
    {
        var rec = new IdleAnimation(Fk, StarfieldRelease.Starfield) { Flags = IdleAnimation.Flag.Loose | IdleAnimation.Flag.Unknown4 };
        foreach (var r in ReadBoth<IIdleAnimationGetter>(Write(w => rec.WriteToBinary(w)),
                     f => IdleAnimation.CreateFromBinary(f), (s, p) => IdleAnimationBinaryOverlay.IdleAnimationFactory(s, p)))
        {
            ShouldBeNamed(r.Flags!.Value, IdleAnimation.Flag.Loose | IdleAnimation.Flag.Unknown4);
        }
    }

    [Fact]
    public void IdleMarker_RoundTrips()
    {
        var rec = new IdleMarker(Fk, StarfieldRelease.Starfield) { Flags = IdleMarker.Flag.Unknown3 };
        foreach (var r in ReadBoth<IIdleMarkerGetter>(Write(w => rec.WriteToBinary(w)),
                     f => IdleMarker.CreateFromBinary(f), (s, p) => IdleMarkerBinaryOverlay.IdleMarkerFactory(s, p)))
        {
            ShouldBeNamed(r.Flags!.Value, IdleMarker.Flag.Unknown3);
        }
    }

    [Fact]
    public void Light_RoundTrips()
    {
        var flags = Light.Flag.Unknown0 | Light.Flag.Unknown10;
        var rec = new Light(Fk, StarfieldRelease.Starfield) { Flags = flags };
        foreach (var r in ReadBoth<ILightGetter>(Write(w => rec.WriteToBinary(w)),
                     f => Light.CreateFromBinary(f), (s, p) => LightBinaryOverlay.LightFactory(s, p)))
        {
            ShouldBeNamed(r.Flags, flags);
        }
    }

    [Fact]
    public void NavmeshTriangle_RoundTrips()
    {
        var rec = new NavigationMesh(Fk, StarfieldRelease.Starfield)
        {
            NavmeshGeometry = new NavmeshGeometry { Triangles = { new NavmeshTriangle { Flags = NavmeshTriangle.Flag.Unknown12 } } },
        };
        foreach (var r in ReadBoth<INavigationMeshGetter>(Write(w => rec.WriteToBinary(w)),
                     f => NavigationMesh.CreateFromBinary(f), (s, p) => NavigationMeshBinaryOverlay.NavigationMeshFactory(s, p)))
        {
            ShouldBeNamed(r.NavmeshGeometry!.Triangles.Single().Flags, NavmeshTriangle.Flag.Unknown12);
        }
    }

    [Fact]
    public void Npc_RoundTrips()
    {
        // LvlRobotModelA_Industrial_Spaceport (NPC_ 003A5A) sets bit 8
        var rec = new Npc(Fk, StarfieldRelease.Starfield) { Flags = Npc.Flag.Unknown8 };
        foreach (var r in ReadBoth<INpcGetter>(Write(w => rec.WriteToBinary(w)),
                     f => Npc.CreateFromBinary(f), (s, p) => NpcBinaryOverlay.NpcFactory(s, p)))
        {
            ShouldBeNamed(r.Flags, Npc.Flag.Unknown8);
        }
    }

    [Fact]
    public void Package_RoundTrips()
    {
        // MS01SandboxInteriorNoUnlockDoors_After (PACK 0021B6): bits 12-15 together
        var interrupt = Package.InterruptFlag.Unknown8 | Package.InterruptFlag.Unknown12 | Package.InterruptFlag.Unknown13
            | Package.InterruptFlag.Unknown14 | Package.InterruptFlag.Unknown15;
        var rec = new Package(Fk, StarfieldRelease.Starfield)
        {
            InterruptFlags = interrupt,
            ProcedureTree = { new PackageBranch { BranchType = "Procedure", Root = new PackageRoot { Flags = PackageRoot.Flag.Unknown1 } } },
        };
        foreach (var r in ReadBoth<IPackageGetter>(Write(w => rec.WriteToBinary(w)),
                     f => Package.CreateFromBinary(f), (s, p) => PackageBinaryOverlay.PackageFactory(s, p)))
        {
            ShouldBeNamed(r.InterruptFlags, interrupt);
            ShouldBeNamed(r.ProcedureTree.Single().Root!.Flags, PackageRoot.Flag.Unknown1);
        }
    }

    [Fact]
    public void PackIn_RoundTrips()
    {
        // OPMinePod18MHexA01PackIn (PKIN 0071E9) sets bits 7-30
        var flags = PackIn.Flag.Unknown0 | PackIn.Flag.Unknown7 | PackIn.Flag.Unknown8 | PackIn.Flag.Unknown10 | PackIn.Flag.Unknown12
            | PackIn.Flag.Unknown13 | PackIn.Flag.Unknown16 | PackIn.Flag.Unknown17 | PackIn.Flag.Unknown18 | PackIn.Flag.Unknown19
            | PackIn.Flag.Unknown21 | PackIn.Flag.Unknown27 | PackIn.Flag.Unknown28 | PackIn.Flag.Unknown30;
        var opds = ObjectPaletteDefaults.Flag.ConformToSlope | ObjectPaletteDefaults.Flag.Unknown12 | ObjectPaletteDefaults.Flag.Unknown13;
        var rec = new PackIn(Fk, StarfieldRelease.Starfield)
        {
            Flags = flags,
            Components = { new ObjectPaletteDefaultsComponent { ObjectPaletteDefaults = new ObjectPaletteDefaults { Flags = opds } } },
        };
        foreach (var r in ReadBoth<IPackInGetter>(Write(w => rec.WriteToBinary(w)),
                     f => PackIn.CreateFromBinary(f), (s, p) => PackInBinaryOverlay.PackInFactory(s, p)))
        {
            ShouldBeNamed(r.Flags!.Value, flags);
            ShouldBeNamed(r.Components!.OfType<IObjectPaletteDefaultsComponentGetter>().Single().ObjectPaletteDefaults!.Flags, opds);
        }
    }

    [Fact]
    public void PlacedObject_Action_IsAFlagSet()
    {
        // Every XACT in Starfield.esm (1,681 references) and the official plugins (1,572) is 0xD
        var action = PlacedObject.ActionFlag.UseDefault | PlacedObject.ActionFlag.Open | PlacedObject.ActionFlag.OpenByDefault;
        ((uint)action).ShouldBe(0xDu);
        var rec = new PlacedObject(Fk, StarfieldRelease.Starfield) { Action = action };
        var bytes = Write(w => rec.WriteToBinary(w));
        BitConverter.ToUInt32(GameConstants.Starfield.MajorRecord(bytes).FindSubrecord(new RecordType("XACT")).Content).ShouldBe(0xDu);
        foreach (var r in ReadBoth<IPlacedObjectGetter>(bytes,
                     f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            ShouldBeNamed(r.Action!.Value, action);
            r.Action!.Value.ToString().ShouldBe("UseDefault, Open, OpenByDefault");
        }
    }

    [Fact]
    public void Quest_RoundTrips()
    {
        var data = Quest.Flag.Unknown16 | Quest.Flag.Unknown17 | Quest.Flag.ShipDialogueOnly | Quest.Flag.CanBeRejected
            | Quest.Flag.AudioLogOnly | Quest.Flag.DoNotStopInterruptPackagesOnAliasFill | Quest.Flag.DoNotDumpOldLocationEventParameter;
        var stage = QuestStage.Flag.StartQuestTimer | QuestStage.Flag.ReadyToDisplayOnUi;
        var refAlias = AQuestAlias.Flag.MatchingRefFromCrowd | AQuestAlias.Flag.QuestObjectCargo | AQuestAlias.Flag.Consumable;
        var locAlias = AQuestAlias.Flag.SearchShipLocations | AQuestAlias.Flag.MatchingLocPlanetsAndSystemsOnly
            | AQuestAlias.Flag.CurrentAliasSystem | AQuestAlias.Flag.CurrentAliasPlanet;
        var rec = new Quest(Fk, StarfieldRelease.Starfield)
        {
            Data = new QuestData { Flags = data },
            Stages = { new QuestStage { Flags = stage } },
            Aliases = new() { new QuestReferenceAlias { Flags = refAlias }, new QuestLocationAlias { Flags = locAlias } },
        };
        foreach (var r in ReadBoth<IQuestGetter>(Write(w => rec.WriteToBinary(w)),
                     f => Quest.CreateFromBinary(f), (s, p) => QuestBinaryOverlay.QuestFactory(s, p)))
        {
            ShouldBeNamed(r.Data!.Flags, data);
            ShouldBeNamed(r.Stages.Single().Flags, stage);
            ShouldBeNamed(r.Aliases!.OfType<IQuestReferenceAliasGetter>().Single().Flags!.Value, refAlias);
            ShouldBeNamed(r.Aliases!.OfType<IQuestLocationAliasGetter>().Single().Flags!.Value, locAlias);
        }
    }

    [Fact]
    public void Race_RoundTrips()
    {
        // HumanRace (RACE 00347D) sets bit 55
        var flags = Race.Flag.Playable | Race.Flag.Unknown55;
        var rec = new Race(Fk, StarfieldRelease.Starfield) { Flags = flags };
        foreach (var r in ReadBoth<IRaceGetter>(Write(w => rec.WriteToBinary(w)),
                     f => Race.CreateFromBinary(f), (s, p) => RaceBinaryOverlay.RaceFactory(s, p)))
        {
            ShouldBeNamed(r.Flags, flags);
        }
    }

    [Fact]
    public void Scene_RoundTrips()
    {
        var sceneFlags = Scene.Flag.Unknown7 | Scene.Flag.Unknown21;
        var behavior = SceneActor.BehaviorFlag.ReactionRadiusPause | SceneActor.BehaviorFlag.ReactionRadiusEnd;
        var rec = new Scene(Fk, StarfieldRelease.Starfield)
        {
            Flags = sceneFlags,
            Phases = new() { new ScenePhase { Name = "P", Flags = ScenePhase.Flag.FxActionsDoAllOnce } },
            Actors = new() { new SceneActor { BehaviorFlags = behavior } },
            Actions = new()
            {
                new StartSceneAction { Flags = ASceneAction.Flag.RunOnEndOfPhase },
                new MoveSceneAction { Flags = ASceneAction.Flag.AnimationOnlyMovement },
            },
        };
        foreach (var r in ReadBoth<ISceneGetter>(Write(w => rec.WriteToBinary(w)),
                     f => Scene.CreateFromBinary(f), (s, p) => SceneBinaryOverlay.SceneFactory(s, p)))
        {
            ShouldBeNamed(r.Flags!.Value, sceneFlags);
            ShouldBeNamed(r.Phases.Single().Flags!.Value, ScenePhase.Flag.FxActionsDoAllOnce);
            ShouldBeNamed(r.Actors.Single().BehaviorFlags, behavior);
            ShouldBeNamed(r.Actions!.OfType<IStartSceneActionGetter>().Single().Flags!.Value, ASceneAction.Flag.RunOnEndOfPhase);
            ShouldBeNamed(r.Actions!.OfType<IMoveSceneActionGetter>().Single().Flags!.Value, ASceneAction.Flag.AnimationOnlyMovement);
        }
    }

    [Fact]
    public void TextureSet_RoundTrips()
    {
        var rec = new TextureSet(Fk, StarfieldRelease.Starfield) { Flags = TextureSet.Flag.DefaultToBoxDecal };
        foreach (var r in ReadBoth<ITextureSetGetter>(Write(w => rec.WriteToBinary(w)),
                     f => TextureSet.CreateFromBinary(f), (s, p) => TextureSetBinaryOverlay.TextureSetFactory(s, p)))
        {
            ShouldBeNamed(r.Flags, TextureSet.Flag.DefaultToBoxDecal);
        }
    }
}
