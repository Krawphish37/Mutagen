using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// The reflection format (BETH/STRT/TYPE/CLAS, then OBJT or DIFF with LIST, MAPC, USER and USRD chunks) holds the
// subrecords REFL, PTCL, RDIF, PSDF, PCCC and XNSE. ReflectionData decodes all 36,899 of them in Starfield.esm to the
// last byte. The FormIDs inside them (pointers to forms, and XNSE's plain RefID) are read as FormLinks and written
// back with the writer's master list.
public class ReflectionDataTests
{
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");
    private static readonly ModKey OtherEsm = ModKey.FromFileName("Other.esm");

    // landscape_caves_mine (000AFA:Starfield.esm) REFL: one keyword, Cave_Mine_Swap_Red (000AF6:Starfield.esm)
    public const string FormFolderKeywordListRefl = "424554480800000004000000060000005354525431000000424753466F726D466F6C6465724B6579776F72644C697374004B6579776F72647300466F726D466F6C6465725061746800545950450400000001000000434C4153240000000000000000000000000002001900000003FFFFFF400110002200000002FFFFFF500108004F424A541B0000000000000015004C616E6473636170655C43617665735C4D696E65004C4953541000000005FFFFFF010000000DFFFFFFF60A0000";

    // GravJumpCalculationCurve (014C2D:Starfield.esm) REFL: a BSFloatCurve, which serializes itself (USER chunk)
    private const string CurveTableRefl = "4245544808000000040000000900000053545254AA0000004247534375727665466F726D004253466C6F6174437572766500436F6E74726F6C73004D6178496E707574004D696E496E70757400496E70757444697374616E6365004D617856616C7565004D696E56616C75650044656661756C7456616C75650054797065004564676500497353616D706C65496E746572706F6C6174696E67004375727665004253466C6F617443757276653A3A436F6E74726F6C00496E7075740056616C756500545950450400000003000000434C4153840000000D000000000000000C000A001A00000003FFFFFF080018002300000011FFFFFF200004002C00000011FFFFFF240004003500000011FFFFFF280004004300000011FFFFFF2C0004004C00000011FFFFFF300004005500000011FFFFFF340004006200000002FFFFFF380001006700000002FFFFFF390001006C00000010FFFFFF3A000100434C415318000000000000000000000000000100820000000D00000018014000434C4153240000008800000000000000000002009E00000011FFFFFF00000400A400000011FFFFFF040004004F424A540400000000000000555345523B0000000D0000000D0000000000803F000000000000803F0000803F000000000000003F0C00437562696342657A696572000600436C616D700001010000004C495354280000008800000004000000" + "8295433EF4FDD43DE290313FBEBBBB3E62123E3F8B6C073F0000803F0000803F";

    // SunPresetVectera (08AC41:Starfield.esm) RDIF: a diff against its parent, SunPresetTypeGTemplate (0E66B6:Starfield.esm)
    private const string SunPresetRdif = "424554480800000004000000080000005354525460010000425347616C6178793A3A42475353756E507265736574466F726D0070506172656E7400584D464C4F41543400780079007A00770053756E436F6C6F720053756E496C6C756D696E616E63650053756E476C617265436F6C6F720053756E4469736B546578747572650053756E4469736B53637265656E53697A654D696E0053756E4469736B53637265656E53697A654D617800425347616C6178793A3A42475353756E507265736574466F726D3A3A4461776E4475736B53657474696E677300446972656374696F6E616C436F6C6F72005472616E736974696F6E5374617274416E676C65005472616E736974696F6E456E64416E676C65004475736B4461776E50726573657400425347616C6178793A3A42475353756E507265736574466F726D3A3A4E6967687453657474696E677300446972656374696F6E616C496C6C756D696E616E636500476C617265436F6C6F72004E696768745072657365740054595045040000000400000043" + "4C415330000000080100000000000000000300C000000023000000000010003201000011FFFFFF10000400490100002300000014001000434C4153780000000000000000000000000009001B00000005FFFFFF180108003400000023000000200110003D00000011FFFFFF300104004C00000023000000340110005A00000002FFFFFF480108006900000011FFFFFF500104007E00000011FFFFFF54010400F90000009300000058011800540100000801000070012400434C41533C0000002300000000000000080004002C00000011FFFFFF000004002E00000011FFFFFF040004003000000011FFFFFF080004003200000011FFFFFF0C000400434C415330000000930000000000000000000300C00000002300000000001000D100000011FFFFFF10000400E600000011FFFFFF1400040044494646300000000000000000000DFFFFFFB6660E00070000000000EEB66D3F0100C75B473F0200A9AA283F03000000803FFFFFFFFFFFFF";

    // (3E8CA3:Starfield.esm) REFR XNSE: a navmesh spline chunk whose reference (3E8CA2:Starfield.esm) is a plain UInt32
    private const string PlacedObjectXnse = "4245544808000000040000000700000053545254860000004E61764D65736853706C696E654578747261446174613A3A4368756E6B44617461005265666572656E636573004E61764D65736841726561466C61670053706C696E65436F6C6F725F5061636B6564004E61764D65736853706C696E654578747261446174613A3A4368756E6B446174615265660053706C696E655369646500526566494400545950450400000002000000434C4153300000000000000000000000000003002200000003FFFFFF000010002D00000002FFFFFF100002003D0000000DFFFFFF14000400434C4153240000005000000000000000000002007500000002FFFFFF00000100800000000DFFFFFF040004004F424A5415000000000000000B00507265666572506174680042332D004C4953541400000050000000010000000600537461727400A28C3E00";

    // AKDecalWallPaintWhite (001DA3:Starfield.esm) MTPT REFL: a material path, no FormIDs
    private const string MaterialPathRefl = "42455448080000000400000005000000535452541D0000004247534D6174657269616C50617468466F726D004D6174657269616C00545950450400000001000000434C4153180000000000000000000000000001001400000002FFFFFF180108004F424A54390000000000000033004172636869746563747572655C436974795C416B696C615C446563616C5C414B57616C6C5061696E7457686974652E6D617400";

    private static ParsingMeta MetaWith(params ModKey[] masters)
    {
        var coll = new MasterReferenceCollection(TestModKey, masters.Select(m => new MasterReference { Master = m }));
        return new ParsingMeta(GameConstants.Starfield, TestModKey, SeparatedMasterPackage.NotSeparate(coll));
    }

    public static IEnumerable<IReflectionGetter> ReadReflection(byte[] subrecord, params ModKey[] masters)
    {
        yield return Reflection.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(subrecord, MetaWith(masters))));
        var meta = MetaWith(masters);
        yield return ReflectionBinaryOverlay.ReflectionFactory(new OverlayStream(subrecord, meta), new BinaryOverlayFactoryPackage(meta));
    }

    public static byte[] WriteWith(Action<MutagenWriter> write, params ModKey[] masters)
    {
        var coll = new MasterReferenceCollection(TestModKey, masters.Select(m => new MasterReference { Master = m }));
        var bundle = new WritingBundle(GameConstants.Starfield)
        {
            MasterReferences = coll,
            SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(coll),
        };
        var memStream = new MemoryStream();
        using (var writer = new MutagenWriter(memStream, bundle, dispose: false))
        {
            write(writer);
        }
        return memStream.ToArray();
    }

    [Fact]
    public void FormFolderKeywordList_HeaderStringsClassesAndObject()
    {
        var data = ReflectionData.TryParse(Hex(FormFolderKeywordListRefl))!;
        data.FullyDecoded.ShouldBeTrue();
        data.Version.ShouldBe(4u);
        data.ChunkCount.ShouldBe(6u);
        data.Chunks.Select(c => c.Signature).ShouldBe(new[] { "BETH", "STRT", "TYPE", "CLAS", "OBJT", "LIST" });
        data.Strings.ShouldBe(new Dictionary<uint, string>
        {
            [0x00] = "BGSFormFolderKeywordList",
            [0x19] = "Keywords",
            [0x22] = "FormFolderPath",
        });
        data.ClassCount.ShouldBe(1u);
        var cls = data.Classes.Single();
        cls.Name.ShouldBe("BGSFormFolderKeywordList");
        cls.Form.ShouldBe(0u);
        cls.Flags.ShouldBe((ReflectionClassFlag)0);
        cls.Fields.Select(f => (f.Name, f.TypeName, f.MemoryOffset, f.MemorySize)).ShouldBe(new[]
        {
            ("Keywords", "List", (ushort)0x140, (ushort)0x10),
            ("FormFolderPath", "String", (ushort)0x150, (ushort)0x8),
        });

        var obj = data.Objects.Single().ShouldBeOfType<ReflectionObject>();
        obj.IsDiff.ShouldBeFalse();
        var keywords = obj.Members[0].Value.ShouldBeOfType<ReflectionList>();
        keywords.ItemTypeId.ShouldBe((uint)ReflectionBuiltInType.Ref);
        var keyword = keywords.Items.Single().ShouldBeOfType<ReflectionRef>();
        keyword.TargetTypeId.ShouldBe((uint)ReflectionBuiltInType.UInt32);
        keyword.Target.ShouldBeOfType<ReflectionScalar>().Value.ShouldBe(0x000AF6u);
        obj.Members[1].Value.ShouldBeOfType<ReflectionScalar>().Value.ShouldBe(@"Landscape\Caves\Mine");

        data.FormIds.ShouldBe(new[] { new ReflectionFormIdLocation(184, 0x000AF6, "BGSFormFolderKeywordList.Keywords[0]") });
        ReflectionData.FindFormIds(Hex(FormFolderKeywordListRefl)).ShouldBe(data.FormIds);
    }

    [Fact]
    public void CurveTable_SelfSerializedCurve()
    {
        var data = ReflectionData.TryParse(Hex(CurveTableRefl))!;
        data.FullyDecoded.ShouldBeTrue();
        data.Chunks.Select(c => c.Signature).ShouldBe(new[] { "BETH", "STRT", "TYPE", "CLAS", "CLAS", "CLAS", "OBJT", "USER", "LIST" });
        data.GetClass(0x0D)!.Flags.ShouldBe(ReflectionClassFlag.User | ReflectionClassFlag.Struct);
        var form = data.Objects.Single().ShouldBeOfType<ReflectionObject>();
        form.Class.Name.ShouldBe("BGSCurveForm");
        var user = form.Members.Single().Value.ShouldBeOfType<ReflectionUser>();
        user.Class.Name.ShouldBe("BSFloatCurve");
        user.ChildChunkCount.ShouldBe(1u);
        var curve = user.Values.Single().ShouldBeOfType<ReflectionObject>();
        object Member(string name) => curve.Members.Single(m => m.Field.Name == name).Value.ShouldBeOfType<ReflectionScalar>().Value;
        Member("MaxInput").ShouldBe(1f);
        Member("DefaultValue").ShouldBe(0.5f);
        Member("Type").ShouldBe("CubicBezier");
        Member("Edge").ShouldBe("Clamp");
        Member("IsSampleInterpolating").ShouldBe(true);
        var controls = curve.Members.Single(m => m.Field.Name == "Controls").Value.ShouldBeOfType<ReflectionList>();
        controls.Items.Count.ShouldBe(4);
        var last = controls.Items[3].ShouldBeOfType<ReflectionObject>();
        last.Class.Name.ShouldBe("BSFloatCurve::Control");
        last.Members.Select(m => m.Value.ShouldBeOfType<ReflectionScalar>().Value).ShouldBe(new object[] { 1f, 1f });
        data.FormIds.ShouldBeEmpty();
    }

    [Fact]
    public void SunPreset_Diff()
    {
        var data = ReflectionData.TryParse(Hex(SunPresetRdif))!;
        data.FullyDecoded.ShouldBeTrue();
        var diff = data.Objects.Single().ShouldBeOfType<ReflectionObject>();
        diff.IsDiff.ShouldBeTrue();
        diff.Class.Name.ShouldBe("BSGalaxy::BGSSunPresetForm");
        diff.Members.Select(m => (m.FieldIndex, m.Field.Name)).ShouldBe(new[] { (0, "pParent"), (7, "DuskDawnPreset") });
        var dawn = diff.Members[1].Value.ShouldBeOfType<ReflectionObject>();
        dawn.IsDiff.ShouldBeTrue();
        var color = dawn.Members.Single().Value.ShouldBeOfType<ReflectionObject>();
        color.Class.Name.ShouldBe("XMFLOAT4");
        color.Members.Select(m => m.Field.Name).ShouldBe(new[] { "x", "y", "z", "w" });
        color.Members[3].Value.ShouldBeOfType<ReflectionScalar>().Value.ShouldBe(1f);
        data.FormIds.ShouldBe(new[] { new ReflectionFormIdLocation(714, 0x0E66B6, "BSGalaxy::BGSSunPresetForm.pParent") });
    }

    [Fact]
    public void PlacedObject_NavMeshSplineRefIdIsAFormId()
    {
        var data = ReflectionData.TryParse(Hex(PlacedObjectXnse))!;
        data.FullyDecoded.ShouldBeTrue();
        var chunk = data.Objects.Single().ShouldBeOfType<ReflectionObject>();
        chunk.Class.Name.ShouldBe("NavMeshSplineExtraData::ChunkData");
        chunk.Members[1].Value.ShouldBeOfType<ReflectionScalar>().Value.ShouldBe("PreferPath");
        data.FormIds.ShouldBe(new[] { new ReflectionFormIdLocation(323, 0x3E8CA2, "NavMeshSplineExtraData::ChunkData.References[0].RefID") });
        ReflectionData.FindFormIds(Hex(PlacedObjectXnse)).ShouldBe(data.FormIds);
    }

    [Fact]
    public void NoFormIds_AndNotReflection()
    {
        var data = ReflectionData.TryParse(Hex(MaterialPathRefl))!;
        data.FullyDecoded.ShouldBeTrue();
        data.Objects.Single().ShouldBeOfType<ReflectionObject>().Members.Single().Value
            .ShouldBeOfType<ReflectionScalar>().Value.ShouldBe(@"Architecture\City\Akila\Decal\AKWallPaintWhite.mat");
        ReflectionData.FindFormIds(Hex(MaterialPathRefl)).ShouldBeEmpty();
        ReflectionData.TryParse(new byte[] { 1, 2, 3, 4 }).ShouldBeNull();
        ReflectionData.FindFormIds(new byte[] { 1, 2, 3, 4 }).ShouldBeEmpty();
    }

    [Fact]
    public void Reflection_ReadsFormLinksAndWritesBack()
    {
        var subrecord = Subrecord("REFL", FormFolderKeywordListRefl);
        foreach (var refl in ReadReflection(subrecord, StarfieldEsm))
        {
            refl.Data.ToArray().ShouldBe(Hex(FormFolderKeywordListRefl));
            refl.FormLinks.Select(l => l.FormKey).ShouldBe(new[] { new FormKey(StarfieldEsm, 0x000AF6) });
            refl.EnumerateFormLinks().Select(l => l.FormKey).ShouldBe(new[] { new FormKey(StarfieldEsm, 0x000AF6) });
            refl.Parse()!.FormIds.Single().FormID.ShouldBe(0x000AF6u);
            WriteWith(w => refl.WriteToBinary(w), StarfieldEsm).ShouldBe(subrecord);
        }
    }

    [Fact]
    public void Reflection_WritesFormIdsWithTheWritersMasters()
    {
        var subrecord = Subrecord("REFL", FormFolderKeywordListRefl);
        var expected = (byte[])subrecord.Clone();
        expected[6 + 184 + 3] = 0x01; // Starfield.esm is now the second master
        foreach (var refl in ReadReflection(subrecord, StarfieldEsm))
        {
            WriteWith(w => refl.WriteToBinary(w), OtherEsm, StarfieldEsm).ShouldBe(expected);
        }
    }

    [Fact]
    public void Reflection_RemapLinks()
    {
        var refl = Reflection.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(Subrecord("REFL", FormFolderKeywordListRefl), MetaWith(StarfieldEsm))));
        refl.RemapLinks(new Dictionary<FormKey, FormKey> { [new FormKey(StarfieldEsm, 0x000AF6)] = new FormKey(StarfieldEsm, 0x000AF7) });
        var written = WriteWith(w => refl.WriteToBinary(w), StarfieldEsm);
        written.AsSpan(6 + 184, 4).ToArray().ShouldBe(new byte[] { 0xF7, 0x0A, 0x00, 0x00 });
        written.AsSpan(0, 6 + 184).ToArray().ShouldBe(Subrecord("REFL", FormFolderKeywordListRefl).AsSpan(0, 6 + 184).ToArray());
    }

    [Fact]
    public void Reflection_FormLinksMustMatchData()
    {
        var refl = new Reflection
        {
            Data = Hex(FormFolderKeywordListRefl),
        };
        // No FormLinks: the data's own FormIDs are written as they are
        WriteWith(w => refl.WriteToBinary(w), StarfieldEsm).ShouldBe(Subrecord("REFL", FormFolderKeywordListRefl));
        refl.FormLinks.Add(new FormLink<IStarfieldMajorRecordGetter>(new FormKey(StarfieldEsm, 1)));
        refl.FormLinks.Add(new FormLink<IStarfieldMajorRecordGetter>(new FormKey(StarfieldEsm, 2)));
        Should.Throw<ArgumentException>(() => WriteWith(w => refl.WriteToBinary(w), StarfieldEsm));
    }
}

internal static class ReflectionTestExt
{
    // A reflection subrecord that decodes fully and whose FormLinks are these FormIDs (read without masters, so only IDs)
    public static void ShouldHaveFormIds(this IReflectionGetter? reflection, params uint[] ids)
    {
        reflection.ShouldNotBeNull();
        reflection!.Parse()!.FullyDecoded.ShouldBeTrue();
        reflection.FormLinks.Select(l => l.FormKey.ID).ShouldBe(ids);
    }

    public static ReflectionObject RootObject(this IReflectionGetter? reflection)
    {
        reflection.ShouldNotBeNull();
        return reflection!.Parse()!.Objects.Single().ShouldBeOfType<ReflectionObject>();
    }
}
