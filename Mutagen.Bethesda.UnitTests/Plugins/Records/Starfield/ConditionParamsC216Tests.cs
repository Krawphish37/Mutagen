using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C216, from the game's own condition function table (C209 dump, ConditionFunctions.txt) and object mod property tables:
// - GetIsUsedItemEquipType (479) parameter 1 is ptEquipSlot, an EQUP record (xEdit ptEquipType): a link to EquipType;
// - CountAquiredPerkRanksByType (878) parameter 3 is a perk skill group, kept in Parameter #3 (-1 = none);
// - the property slots the game keeps with no value get its DEPRECATED names.
public class ConditionParamsC216Tests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta() =>
        new(GameConstants.Starfield, TestModKey, SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey)));

    private static byte[] Ctda(ushort function, uint parameter1, int parameter3)
    {
        var content = new byte[32];
        BitConverter.GetBytes(function).CopyTo(content, 8);
        BitConverter.GetBytes(parameter1).CopyTo(content, 12);
        BitConverter.GetBytes(parameter3).CopyTo(content, 28);
        var sub = new MemoryStream();
        sub.Write("CTDA"u8);
        sub.Write(BitConverter.GetBytes((ushort)content.Length));
        sub.Write(content);
        return sub.ToArray();
    }

    private static byte[] Write(IConditionGetter condition)
    {
        var masters = new MasterReferenceCollection(TestModKey);
        var bundle = new WritingBundle(GameConstants.Starfield)
        {
            MasterReferences = masters,
            SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(masters),
        };
        var mem = new MemoryStream();
        using (var writer = new MutagenWriter(mem, bundle, dispose: false))
        {
            condition.WriteToBinary(writer);
        }
        return mem.ToArray();
    }

    private static IEnumerable<IConditionGetter> ReadBoth(byte[] bytes)
    {
        yield return Condition.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())), default);
        var meta = Meta();
        yield return ConditionBinaryOverlay.ConditionFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    [Fact]
    public void GetIsUsedItemEquipType_ParameterOneIsAnEquipTypeLink()
    {
        var bytes = Ctda(479, 0x000812, -1);
        foreach (var condition in ReadBoth(bytes))
        {
            var data = condition.Data.ShouldBeAssignableTo<IGetIsUsedItemEquipTypeConditionDataGetter>()!;
            data.FirstParameter.Link.FormKey.ShouldBe(new FormKey(TestModKey, 0x812));
            Write(condition).ShouldBe(bytes);
        }
        var built = new ConditionFloat { Data = new GetIsUsedItemEquipTypeConditionData() };
        ((GetIsUsedItemEquipTypeConditionData)built.Data).FirstParameter.Link.SetTo(new FormKey(TestModKey, 0x812));
        BitConverter.ToUInt32(Write(built), 6 + 12).ShouldBe(0x000812u);
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(2, PerkSkillGroup.Advanced)]
    public void CountAquiredPerkRanksByType_SkillGroupIsParameterThree(int stored, PerkSkillGroup? expected)
    {
        var bytes = Ctda(878, 3, stored);
        foreach (var condition in ReadBoth(bytes))
        {
            condition.Data.ShouldBeAssignableTo<ICountAquiredPerkRanksByTypeConditionDataGetter>()!.GetSkillGroup().ShouldBe(expected);
            Write(condition).ShouldBe(bytes);
        }
        var data = new CountAquiredPerkRanksByTypeConditionData();
        data.SetSkillGroup(expected);
        data.ParameterThree.ShouldBe(stored);
    }

    [Fact]
    public void DeprecatedSlotsCarryTheGamesIds()
    {
        static int Id(string fourCc) => BitConverter.ToInt32(System.Text.Encoding.ASCII.GetBytes(fourCc));
        ((int)Weapon.Property.Deprecated2).ShouldBe(Id("W002"));
        ((int)Weapon.Property.Deprecated10).ShouldBe(Id("W010"));
        ((int)Weapon.Property.Deprecated).ShouldBe(Id("WSWP"));
        ((int)Weapon.Property.DeprecatedZFPF).ShouldBe(Id("ZFPF"));
        ((int)Npc.Property.Deprecated).ShouldBe(Id("NMSW"));
        ((int)SpaceshipModification.Property.Deprecated).ShouldBe(Id("SMSW"));
        // the IDs the game's table has twice stay without a member
        Enum.IsDefined(typeof(Weapon.Property), Id("WRC4")).ShouldBeFalse();
        Enum.IsDefined(typeof(Weapon.Property), Id("WRC6")).ShouldBeFalse();
        Enum.IsDefined(typeof(Weapon.Property), Id("PUSE")).ShouldBeFalse();
    }
}
