using System.Buffers.Binary;
using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Starfield.Internals;
using Noggog;
using Xunit;
using StarfieldConstants = Mutagen.Bethesda.Starfield.Constants;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

public class ObjectTemplateLevelTests
{
    private static readonly MasterReferenceCollection Masters = new(StarfieldConstants.Starfield);

    private static ParsingMeta Meta() => new(
        GameConstants.Starfield,
        StarfieldConstants.Starfield,
        SeparatedMasterPackage.NotSeparate(Masters));

    private static Weapon MakeWeapon(ushort levelMin, ushort levelMax)
    {
        var weapon = new Weapon(new FormKey(StarfieldConstants.Starfield, 0x800), StarfieldRelease.Starfield)
        {
            EditorID = "TestWeapon",
        };
        weapon.ObjectTemplates = new ExtendedList<ObjectTemplate<Weapon.Property>>
        {
            new()
            {
                LevelMin = levelMin,
                LevelMax = levelMax,
                AddonIndex = -1,
            }
        };
        return weapon;
    }

    private static byte[] Write(IWeaponGetter item)
    {
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(
                   stream,
                   new WritingBundle(GameConstants.Starfield)
                   {
                       MasterReferences = Masters,
                       SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(Masters),
                   }))
        {
            WeaponBinaryWriteTranslation.Instance.Write(writer, item, default);
        }
        return stream.ToArray();
    }

    private static IWeaponGetter ParseDeep(byte[] bytes)
    {
        using var reader = new MutagenBinaryReadStream(new MemoryStream(bytes), Meta());
        return Weapon.CreateFromBinary(new MutagenFrame(reader));
    }

    private static IWeaponGetter ParseOverlay(byte[] bytes)
    {
        return WeaponBinaryOverlay.WeaponFactory(
            bytes,
            new BinaryOverlayFactoryPackage(Meta()));
    }

    private static byte[] ObtsContent(byte[] record)
    {
        var sub = RecordSpanExtensions.TryFindSubrecord(
            new ReadOnlyMemorySlice<byte>(record).Slice(GameConstants.Starfield.MajorConstants.HeaderLength),
            GameConstants.Starfield,
            RecordTypes.OBTS);
        sub.ShouldNotBeNull();
        return sub.Value.Content.ToArray();
    }

    [Theory]
    [InlineData((ushort)1, (ushort)255)]
    [InlineData((ushort)260, (ushort)300)]
    [InlineData((ushort)300, (ushort)1000)]
    public void Write_LevelsAreUInt16(ushort levelMin, ushort levelMax)
    {
        var obts = ObtsContent(Write(MakeWeapon(levelMin, levelMax)));
        BinaryPrimitives.ReadUInt16LittleEndian(obts.AsSpan(8)).ShouldBe(levelMin);
        BinaryPrimitives.ReadUInt16LittleEndian(obts.AsSpan(10)).ShouldBe(levelMax);
    }

    [Theory]
    [InlineData((ushort)1, (ushort)255)]
    [InlineData((ushort)260, (ushort)300)]
    [InlineData((ushort)300, (ushort)1000)]
    public void DeepParse_ReadsLevels(ushort levelMin, ushort levelMax)
    {
        var template = ParseDeep(Write(MakeWeapon(levelMin, levelMax))).ObjectTemplates!.Single();
        template.LevelMin.ShouldBe(levelMin);
        template.LevelMax.ShouldBe(levelMax);
        template.AddonIndex.ShouldBe((short)-1);
    }

    [Theory]
    [InlineData((ushort)1, (ushort)255)]
    [InlineData((ushort)260, (ushort)300)]
    [InlineData((ushort)300, (ushort)1000)]
    public void Overlay_ReadsLevels(ushort levelMin, ushort levelMax)
    {
        var template = ParseOverlay(Write(MakeWeapon(levelMin, levelMax))).ObjectTemplates!.Single();
        template.LevelMin.ShouldBe(levelMin);
        template.LevelMax.ShouldBe(levelMax);
        template.AddonIndex.ShouldBe((short)-1);
    }

    [Fact]
    public void Overlay_RoundTrip_KeepsLevelBytes()
    {
        var original = Write(MakeWeapon(260, 300));
        var rewritten = Write(ParseOverlay(original));
        rewritten.ShouldBe(original);
    }
}
