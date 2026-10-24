using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

/// <summary>
/// Each record is hand built with the subrecord layout found in Starfield.esm, pointing at the
/// record type the game data really uses.  The link the record exposes must be declared with a
/// type that accepts that target, so typed resolution finds it.
/// </summary>
public class DeclaredLinkTypeTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Master);
    private const ushort FormVersion = 575;

    private const uint SnapTemplateBehaviorId = 0x801;
    private const uint ArmorModificationId = 0x802;
    private const uint ResourceId = 0x803;
    private const uint ImageSpaceId = 0x804;
    private const uint ImageSpaceAdapterId = 0x805;
    private const uint TerminalMenuId = 0x806;
    private const uint GlobalId = 0x807;
    private const uint QuestId = 0x808;
    private const uint ObjectEffectId = 0x809;
    private const uint LeveledSpaceCellId = 0x80A;
    private const uint CellId = 0x80B;
    private const uint NpcId = 0x80C;

    private static FormKey Key(uint id) => new(TestModKey, id);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Starfield, TestModKey, masters);
    }

    private static ILinkCache TargetCache()
    {
        var release = StarfieldRelease.Starfield;
        var mod = new StarfieldMod(TestModKey, release);
        mod.SnapTemplateBehaviors.Add(new SnapTemplateBehavior(Key(SnapTemplateBehaviorId), release));
        mod.ObjectModifications.Add(new ArmorModification(Key(ArmorModificationId), release));
        mod.Resources.Add(new Resource(Key(ResourceId), release));
        mod.ImageSpaces.Add(new ImageSpace(Key(ImageSpaceId), release));
        mod.ImageSpaceAdapters.Add(new ImageSpaceAdapter(Key(ImageSpaceAdapterId), release));
        mod.TerminalMenus.Add(new TerminalMenu(Key(TerminalMenuId), release));
        mod.Globals.Add(new Global(Key(GlobalId), release));
        mod.Quests.Add(new Quest(Key(QuestId), release));
        mod.ObjectEffects.Add(new ObjectEffect(Key(ObjectEffectId), release));
        mod.LeveledSpaceCells.Add(new LeveledSpaceCell(Key(LeveledSpaceCellId), release));
        mod.Npcs.Add(new Npc(Key(NpcId), release));
        mod.Cells.Add(new CellBlock()
        {
            BlockNumber = 0,
            GroupType = GroupTypeEnum.InteriorCellBlock,
            SubBlocks = new ExtendedList<CellSubBlock>()
            {
                new CellSubBlock()
                {
                    BlockNumber = 0,
                    GroupType = GroupTypeEnum.InteriorCellSubBlock,
                    Cells = new ExtendedList<Cell>()
                    {
                        new Cell(Key(CellId), release)
                    }
                }
            }
        });
        return mod.ToImmutableLinkCache();
    }

    private static void WriteSubrecord(MemoryStream stream, string type, byte[] content)
    {
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
    }

    private static byte[] Bytes(params object[] parts)
    {
        var ret = new MemoryStream();
        foreach (var part in parts)
        {
            switch (part)
            {
                case uint u: ret.Write(BitConverter.GetBytes(u)); break;
                case int i: ret.Write(BitConverter.GetBytes(i)); break;
                case ushort s: ret.Write(BitConverter.GetBytes(s)); break;
                case short s: ret.Write(BitConverter.GetBytes(s)); break;
                case byte b: ret.WriteByte(b); break;
                case float f: ret.Write(BitConverter.GetBytes(f)); break;
                case string str: ret.Write(Encoding.ASCII.GetBytes(str + "\0")); break;
                default: throw new ArgumentException(part.GetType().Name);
            }
        }
        return ret.ToArray();
    }

    private static byte[] MakeRecord(string recordType, uint formId, params (string Type, byte[] Data)[] subrecords)
    {
        var content = new MemoryStream();
        WriteSubrecord(content, "EDID", Bytes($"Test{recordType}"));
        foreach (var sub in subrecords)
        {
            WriteSubrecord(content, sub.Type, sub.Data);
        }
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes(recordType));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(formId));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(FormVersion));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static IEnumerable<IMajorRecordGetter> ReadBoth(
        byte[] bytes,
        Func<MutagenFrame, IMajorRecordGetter> direct,
        Func<OverlayStream, BinaryOverlayFactoryPackage, IMajorRecordGetter> overlay)
    {
        yield return direct(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return overlay(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    private static void LinksResolveTo(IMajorRecordGetter record, uint targetId, Type targetGetter, int expectedCount = 1)
    {
        var cache = TargetCache();
        var links = record.EnumerateFormLinks().Where(l => l.FormKey == Key(targetId)).ToArray();
        links.Length.ShouldBe(expectedCount);
        foreach (var link in links)
        {
            link.Type.IsAssignableFrom(targetGetter).ShouldBeTrue(
                $"{record.GetType().Name} link to {targetGetter.Name} is declared as {link.Type.Name}");
            cache.TryResolve(link.FormKey, link.Type, out var resolved).ShouldBeTrue(
                $"{record.GetType().Name} link declared as {link.Type.Name} did not resolve");
            resolved!.FormKey.ShouldBe(Key(targetId));
        }
    }

    [Fact]
    public void StaticSnapBehaviorIsSnapTemplateBehavior()
    {
        var bytes = MakeRecord("STAT", 0x900, ("SNBH", Bytes(SnapTemplateBehaviorId)));
        foreach (var rec in ReadBoth(bytes, f => Static.CreateFromBinary(f),
                     (s, p) => StaticBinaryOverlay.StaticFactory(s, p)))
        {
            LinksResolveTo(rec, SnapTemplateBehaviorId, typeof(ISnapTemplateBehaviorGetter));
        }
    }

    [Fact]
    public void QualityModsAcceptArmorModification()
    {
        var bytes = MakeRecord("ARMO", 0x901,
            ("BFCB", Bytes("BGSQualityUpgrade_Component")),
            ("QUPA", Bytes(ArmorModificationId)),
            ("BFCE", Array.Empty<byte>()));
        foreach (var rec in ReadBoth(bytes, f => Armor.CreateFromBinary(f),
                     (s, p) => ArmorBinaryOverlay.ArmorFactory(s, p)))
        {
            LinksResolveTo(rec, ArmorModificationId, typeof(IArmorModificationGetter));
        }
    }

    [Fact]
    public void ConstructibleObjectComponentAcceptsResource()
    {
        var bytes = MakeRecord("COBJ", 0x902, ("FVPA", Bytes(ResourceId, 1u, 0u)));
        foreach (var rec in ReadBoth(bytes, f => ConstructibleObject.CreateFromBinary(f),
                     (s, p) => ConstructibleObjectBinaryOverlay.ConstructibleObjectFactory(s, p)))
        {
            LinksResolveTo(rec, ResourceId, typeof(IResourceGetter));
        }
    }

    [Fact]
    public void ImageSpaceReflectionParentIsImageSpace()
    {
        var bytes = MakeRecord("IMGS", 0x903, ("RFDP", Bytes(ImageSpaceId)));
        foreach (var rec in ReadBoth(bytes, f => ImageSpace.CreateFromBinary(f),
                     (s, p) => ImageSpaceBinaryOverlay.ImageSpaceFactory(s, p)))
        {
            LinksResolveTo(rec, ImageSpaceId, typeof(IImageSpaceGetter));
        }
    }

    [Fact]
    public void PhotoModeFeatureImageSpaceIsImageSpaceAdapter()
    {
        var bytes = MakeRecord("PMFT", 0x904, ("INAM", Bytes(ImageSpaceAdapterId)));
        foreach (var rec in ReadBoth(bytes, f => PhotoModeFeature.CreateFromBinary(f),
                     (s, p) => PhotoModeFeatureBinaryOverlay.PhotoModeFeatureFactory(s, p)))
        {
            LinksResolveTo(rec, ImageSpaceAdapterId, typeof(IImageSpaceAdapterGetter));
        }
    }

    [Fact]
    public void TerminalMenuSubmenuIsTerminalMenu()
    {
        var bytes = MakeRecord("TMLM", 0x905,
            ("BSIZ", Bytes(0u)),
            ("ISIZ", Bytes(1u)),
            ("ITXT", Bytes("Open")),
            // ISET: type 1 = submenu (TNAM)
            ("ISET", Bytes((ushort)1, (ushort)0, (byte)0, (byte)0, (byte)0, (byte)0)),
            ("ITID", Bytes((ushort)1)),
            ("TNAM", Bytes(TerminalMenuId)));
        foreach (var rec in ReadBoth(bytes, f => TerminalMenu.CreateFromBinary(f),
                     (s, p) => TerminalMenuBinaryOverlay.TerminalMenuFactory(s, p)))
        {
            LinksResolveTo(rec, TerminalMenuId, typeof(ITerminalMenuGetter));
        }
    }

    [Fact]
    public void QuestAliasTerminalMenusAreTerminalMenus()
    {
        var bytes = MakeRecord("QUST", 0x906,
            ("ANAM", Bytes(1u)),
            ("ALST", Bytes(0u)),
            ("ALID", Bytes("Alias")),
            ("ALTM", Bytes(TerminalMenuId)),
            ("ALED", Array.Empty<byte>()));
        foreach (var rec in ReadBoth(bytes, f => Quest.CreateFromBinary(f),
                     (s, p) => QuestBinaryOverlay.QuestFactory(s, p)))
        {
            LinksResolveTo(rec, TerminalMenuId, typeof(ITerminalMenuGetter));
        }
    }

    [Fact]
    public void QuestQdupsAcceptAnyRecordType()
    {
        // QDUP holds (source form, duplicated form) pairs of any record type
        var bytes = MakeRecord("QUST", 0x907,
            ("QDUP", Bytes(GlobalId, QuestId)));
        foreach (var rec in ReadBoth(bytes, f => Quest.CreateFromBinary(f),
                     (s, p) => QuestBinaryOverlay.QuestFactory(s, p)))
        {
            LinksResolveTo(rec, GlobalId, typeof(IGlobalGetter));
            LinksResolveTo(rec, QuestId, typeof(IQuestGetter));
        }
    }

    [Fact]
    public void LeveledSpaceCellEntriesAreSpaceCellSpawns()
    {
        static byte[] Lvlo(uint id) => Bytes((short)1, (short)0, id, (short)1, (byte)0, (byte)0);
        var bytes = MakeRecord("LVSC", 0x908,
            ("LVLD", Bytes(0f)),
            ("LVLM", Bytes((byte)0)),
            ("LVLF", Bytes((ushort)0)),
            ("LLCT", Bytes((byte)2)),
            ("LVLO", Lvlo(LeveledSpaceCellId)),
            ("LVLO", Lvlo(CellId)));
        foreach (var rec in ReadBoth(bytes, f => LeveledSpaceCell.CreateFromBinary(f),
                     (s, p) => LeveledSpaceCellBinaryOverlay.LeveledSpaceCellFactory(s, p)))
        {
            LinksResolveTo(rec, LeveledSpaceCellId, typeof(ILeveledSpaceCellGetter));
            LinksResolveTo(rec, CellId, typeof(ICellGetter));
        }
    }

    [Fact]
    public void IsSpellTargetAcceptsObjectEffect()
    {
        // CTDA: flags, 3 unused, comparison value, function 223 (IsSpellTarget), unused,
        // parameter 1, parameter 2, run on, reference, parameter 3
        var ctda = Bytes((byte)0, (byte)0, (byte)0, (byte)0, 1f, (ushort)223, (ushort)0,
            ObjectEffectId, 0u, 0u, 0u, -1);
        var bytes = MakeRecord("LVSC", 0x909, ("CTDA", ctda));
        foreach (var rec in ReadBoth(bytes, f => LeveledSpaceCell.CreateFromBinary(f),
                     (s, p) => LeveledSpaceCellBinaryOverlay.LeveledSpaceCellFactory(s, p)))
        {
            LinksResolveTo(rec, ObjectEffectId, typeof(IObjectEffectGetter));
        }
    }

    [Fact]
    public void CrowdComponentPropertiesAreNpcs()
    {
        var bytes = MakeRecord("GBFM", 0x90A,
            ("BFCB", Bytes("BGSCrowdComponent_Component")),
            ("PRPS", Bytes(NpcId, 100f, 0u)),
            ("BFCE", Array.Empty<byte>()));
        foreach (var rec in ReadBoth(bytes, f => GenericBaseForm.CreateFromBinary(f),
                     (s, p) => GenericBaseFormBinaryOverlay.GenericBaseFormFactory(s, p)))
        {
            LinksResolveTo(rec, NpcId, typeof(INpcGetter));
        }
    }
}
