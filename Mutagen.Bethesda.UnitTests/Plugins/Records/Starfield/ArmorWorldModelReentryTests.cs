using System.Buffers.Binary;
using System.Text;
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

public class ArmorWorldModelReentryTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private static readonly MasterReferenceCollection Masters = new(TestModKey);

    private const string MalePath = "Male\\Model.nif";
    private const string FemalePath = "Female\\Model.nif";

    private static ParsingMeta Meta() => new(
        GameConstants.Starfield,
        TestModKey,
        SeparatedMasterPackage.NotSeparate(Masters));

    private static byte[] Subrecord(string type, byte[] content)
    {
        var header = new byte[6];
        Encoding.ASCII.GetBytes(type).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), checked((ushort)content.Length));
        return header.Concat(content).ToArray();
    }

    private static byte[] NullTerminated(string s) => Encoding.ASCII.GetBytes(s + "\0");

    private static byte[] UInt32(uint i) => BitConverter.GetBytes(i);

    private static byte[] MakeArmorBytes(params byte[][] subrecords)
    {
        var content = Subrecord("EDID", NullTerminated("TestArmor"))
            .Concat(subrecords.SelectMany(x => x))
            .ToArray();

        var header = new byte[GameConstants.Starfield.MajorConstants.HeaderLength];
        Encoding.ASCII.GetBytes("ARMO").CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)content.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 0x800);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 555);
        return header.Concat(content).ToArray();
    }

    private static IArmorGetter ReadDirect(byte[] bytes)
    {
        return Armor.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
    }

    private static IArmorGetter ReadOverlay(byte[] bytes)
    {
        var meta = Meta();
        return ArmorBinaryOverlay.ArmorFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    private static IEnumerable<IArmorGetter> ReadBoth(byte[] bytes)
    {
        yield return ReadDirect(bytes);
        yield return ReadOverlay(bytes);
    }

    private static byte[] Write(IArmorGetter armor)
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
            ArmorBinaryWriteTranslation.Instance.Write(writer, armor, default);
        }
        return stream.ToArray();
    }

    private static void AssertModels(IArmorGetter armor, uint maleLightLayer, uint femaleLightLayer)
    {
        armor.WorldModel.ShouldNotBeNull();
        var male = armor.WorldModel.Male;
        var female = armor.WorldModel.Female;
        male.ShouldNotBeNull();
        female.ShouldNotBeNull();
        male.File.ShouldNotBeNull();
        male.File.GivenPath.ShouldBe(MalePath);
        ((uint?)male.LightLayer).ShouldBe(maleLightLayer);
        female.File.ShouldNotBeNull();
        female.File.GivenPath.ShouldBe(FemalePath);
        ((uint?)female.LightLayer).ShouldBe(femaleLightLayer);
    }

    [Fact]
    public void LightLayersAfterLaterSubrecords_AddToModelsAlreadyRead()
    {
        // MOD2 MOD4 ... STOP FLLD FLLD, as written by some tools
        var bytes = MakeArmorBytes(
            Subrecord("MOD2", NullTerminated(MalePath)),
            Subrecord("MOD4", NullTerminated(FemalePath)),
            Subrecord("BO64", new byte[8]),
            Subrecord("FLLD", UInt32(1)),
            Subrecord("FLLD", UInt32(2)));

        foreach (var armor in ReadBoth(bytes))
        {
            AssertModels(armor, maleLightLayer: 1, femaleLightLayer: 2);
            armor.FirstPersonFlags.ShouldBe((FirstPersonFlag)0);
        }
    }

    [Fact]
    public void FemaleModelAfterLaterSubrecords_KeepsMaleModel()
    {
        var bytes = MakeArmorBytes(
            Subrecord("MOD2", NullTerminated(MalePath)),
            Subrecord("FLLD", UInt32(1)),
            Subrecord("BO64", new byte[8]),
            Subrecord("MOD4", NullTerminated(FemalePath)),
            Subrecord("FLLD", UInt32(2)));

        foreach (var armor in ReadBoth(bytes))
        {
            AssertModels(armor, maleLightLayer: 1, femaleLightLayer: 2);
        }
    }

    [Fact]
    public void LightLayersAfterLaterSubrecords_WrittenWithTheirModels()
    {
        var bytes = MakeArmorBytes(
            Subrecord("MOD2", NullTerminated(MalePath)),
            Subrecord("MOD4", NullTerminated(FemalePath)),
            Subrecord("BO64", new byte[8]),
            Subrecord("DATA", new byte[12]),
            Subrecord("FNAM", new byte[8]),
            Subrecord("FLLD", UInt32(1)),
            Subrecord("FLLD", UInt32(2)));

        foreach (var armor in ReadBoth(bytes))
        {
            var written = Write(armor);
            var types = RecordSpanExtensions.EnumerateSubrecords(
                    new ReadOnlyMemorySlice<byte>(written).Slice(GameConstants.Starfield.MajorConstants.HeaderLength),
                    GameConstants.Starfield)
                .Select(x => x.RecordType.Type)
                .Where(x => x is "MOD2" or "MOD4" or "FLLD")
                .ToArray();
            types.ShouldBe(new[] { "MOD2", "FLLD", "MOD4", "FLLD" });
            AssertModels(ReadDirect(written), maleLightLayer: 1, femaleLightLayer: 2);
        }
    }

    [Fact]
    public void TypicalLayout_Unchanged()
    {
        var bytes = MakeArmorBytes(
            Subrecord("MOD2", NullTerminated(MalePath)),
            Subrecord("FLLD", UInt32(1)),
            Subrecord("MOD4", NullTerminated(FemalePath)),
            Subrecord("FLLD", UInt32(2)),
            Subrecord("BO64", new byte[8]));

        foreach (var armor in ReadBoth(bytes))
        {
            AssertModels(armor, maleLightLayer: 1, femaleLightLayer: 2);
        }
    }
}
