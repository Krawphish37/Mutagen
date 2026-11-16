using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Starfield.Internals;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// WEAP WAIM's Accuracy Bonus is a signed byte (xEdit: itS8). Two weapons of a 370-plugin Data folder store 0xE8
// (DarkStar_WEAP_crMech_CombatRightGun_Auto and ..LeftGun_Auto, 067409 and 06740A of kinggathcreations_spaceship.esm,
// as darkstarpatch_watchtower.esm overrides them): -24, which read as 232 while the field was a UInt8.
public class WeaponAccuracyBonusTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private static readonly MasterReferenceCollection Masters = new(TestModKey);

    private static ParsingMeta Meta() => new(
        GameConstants.Starfield,
        TestModKey,
        SeparatedMasterPackage.NotSeparate(Masters));

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

    [Fact]
    public void NegativeAccuracyBonus_IsWrittenAsItsByteAndReadBack()
    {
        var weapon = new Weapon(FormKey.Factory("123456:Test.esp"), StarfieldRelease.Starfield)
        {
            AccuracyBonus = -24,
        };
        var bytes = Write(weapon);

        var waim = RecordSpanExtensions.TryFindSubrecord(
            new ReadOnlyMemorySlice<byte>(bytes).Slice(GameConstants.Starfield.MajorConstants.HeaderLength),
            GameConstants.Starfield,
            RecordTypes.WAIM);
        waim.ShouldNotBeNull();
        // After the sighted transition seconds (4) and the two FormIDs (8)
        waim.Value.Content[12].ShouldBe((byte)0xE8);

        Weapon.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())))
            .AccuracyBonus.ShouldBe((sbyte)-24);
        WeaponBinaryOverlay.WeaponFactory(bytes, new BinaryOverlayFactoryPackage(Meta()))
            .AccuracyBonus.ShouldBe((sbyte)-24);
    }
}
