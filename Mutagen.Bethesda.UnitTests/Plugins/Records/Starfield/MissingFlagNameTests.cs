using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Flag names xEdit has that Mutagen lacked or got wrong. Values are real Starfield.esm subrecords; xEdit agrees on each.
// - Key.Flag had no explicit values (0 and 1), so a key's FLAG 0x01 read as NonInstancedKey; it is Allow Quest Item Crafting.
// - MiscItem FLAG was raw bytes; it is the same list as KEYM.
// - MiscItem.MajorFlag had the bit numbers 11 and 13 as values; the flags are 0x800 and 0x2000.
// - Message Allow Back Out, MaterialType Is Slippery, destructible Resist All Except, Object Cleared, Fallback: Spawn At Bounds Center.
public class MissingFlagNameTests
{
    [Fact]
    public void Key_AllowQuestItemCrafting()
    {
        // FLAG of DR020WeaponRackKey (KEYM 00485E)
        foreach (var key in ReadBoth<IKeyGetter>(Record("KEYM", Subrecord("FLAG", "01-00-00-00")), f => Key.CreateFromBinary(f), (s, p) => KeyBinaryOverlay.KeyFactory(s, p)))
        {
            key.Flags.ShouldBe(Key.Flag.AllowQuestItemCrafting);
        }
    }

    [Fact]
    public void MiscItem_Flags()
    {
        // FLAG of CommitmentGift_Andreja_MiscObj (MISC 0B8573): both flags
        var bytes = Record("MISC", Subrecord("FLAG", "03-00-00-00"));
        foreach (var misc in ReadBoth<IMiscItemGetter>(bytes, f => MiscItem.CreateFromBinary(f), (s, p) => MiscItemBinaryOverlay.MiscItemFactory(s, p)))
        {
            misc.Flags.ShouldBe(MiscItem.Flag.AllowQuestItemCrafting | MiscItem.Flag.NonInstancedKey);
            var written = GameConstants.Starfield.MajorRecord(Write(w => misc.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("FLAG")).Content.ToArray().ShouldBe(Hex("03-00-00-00"));
        }
    }

    [Fact]
    public void MiscItem_MajorFlags_AreBits()
    {
        ((int)MiscItem.MajorFlag.CalcFromComponents).ShouldBe(0x800);
        ((int)MiscItem.MajorFlag.PackInUseOnly).ShouldBe(0x2000);
        var bytes = Record("MISC", Subrecord("FLAG", "00-00-00-00"));
        BitConverter.GetBytes(0x2000).CopyTo(bytes, 8); // record header flags: Pack-In Use Only
        foreach (var misc in ReadBoth<IMiscItemGetter>(bytes, f => MiscItem.CreateFromBinary(f), (s, p) => MiscItemBinaryOverlay.MiscItemFactory(s, p)))
        {
            ((MiscItem.MajorFlag)misc.MajorRecordFlagsRaw).HasFlag(MiscItem.MajorFlag.PackInUseOnly).ShouldBeTrue();
            ((MiscItem.MajorFlag)misc.MajorRecordFlagsRaw).HasFlag(MiscItem.MajorFlag.CalcFromComponents).ShouldBeFalse();
        }
    }

    [Fact]
    public void Message_AllowBackOut()
    {
        // DNAM of RL039RecoveryElevatorFirstFloorName (MESG 005310): Message Box + Allow Back Out
        foreach (var msg in ReadBoth<IMessageGetter>(Record("MESG", Subrecord("DNAM", "05-00-00-00")), f => Message.CreateFromBinary(f), (s, p) => MessageBinaryOverlay.MessageFactory(s, p)))
        {
            msg.Flags.ShouldBe(Message.Flag.MessageBox | Message.Flag.AllowBackOut);
        }
    }

    [Fact]
    public void MaterialType_IsSlippery()
    {
        // FNAM of MaterialPHYGlassGenericSmallSlippery (MATT 0EA684)
        foreach (var matt in ReadBoth<IMaterialTypeGetter>(Record("MATT", Subrecord("FNAM", "08-00-00-00")), f => MaterialType.CreateFromBinary(f), (s, p) => MaterialTypeBinaryOverlay.MaterialTypeFactory(s, p)))
        {
            matt.Flags.ShouldBe(MaterialType.Flag.IsSlippery);
        }
    }

    [Fact]
    public void Destructible_FlagValues()
    {
        ((int)Destructible.DestructibleFlag.ResistAllExcept).ShouldBe(0x04);
        ((int)Destructible.DestructionStageDataFlag.ObjectCleared).ShouldBe(0x20);
        ((int)Destructible.DestructionStageDataFlag.FallbackSpawnAtBoundsCenter).ShouldBe(0x40);
    }
}
