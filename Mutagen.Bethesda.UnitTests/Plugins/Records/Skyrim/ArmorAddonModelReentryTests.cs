using System.Buffers.Binary;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Skyrim;

public class ArmorAddonModelReentryTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta() => new(
        GameConstants.SkyrimSE,
        TestModKey,
        SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey)));

    private static byte[] Subrecord(string type, byte[] content)
    {
        var header = new byte[6];
        Encoding.ASCII.GetBytes(type).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), checked((ushort)content.Length));
        return header.Concat(content).ToArray();
    }

    private static byte[] NullTerminated(string s) => Encoding.ASCII.GetBytes(s + "\0");

    private static byte[] MakeArmorAddonBytes(params byte[][] subrecords)
    {
        var content = Subrecord("EDID", NullTerminated("TestArmorAddon"))
            .Concat(subrecords.SelectMany(x => x))
            .ToArray();

        var header = new byte[GameConstants.SkyrimSE.MajorConstants.HeaderLength];
        Encoding.ASCII.GetBytes("ARMA").CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)content.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 0x800);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 44);
        return header.Concat(content).ToArray();
    }

    private static IEnumerable<IArmorAddonGetter> ReadBoth(byte[] bytes)
    {
        yield return ArmorAddon.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return ArmorAddonBinaryOverlay.ArmorAddonFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    [Fact]
    public void FemaleWorldModelAfterFirstPersonModel_KeepsMaleWorldModel()
    {
        var bytes = MakeArmorAddonBytes(
            Subrecord("MOD2", NullTerminated("Male.nif")),
            Subrecord("MOD4", NullTerminated("MaleFirstPerson.nif")),
            Subrecord("MOD3", NullTerminated("Female.nif")));

        foreach (var armorAddon in ReadBoth(bytes))
        {
            armorAddon.WorldModel.ShouldNotBeNull();
            armorAddon.WorldModel.Male.ShouldNotBeNull();
            armorAddon.WorldModel.Male.File.GivenPath.ShouldBe("Male.nif");
            armorAddon.WorldModel.Female.ShouldNotBeNull();
            armorAddon.WorldModel.Female.File.GivenPath.ShouldBe("Female.nif");
            armorAddon.FirstPersonModel.ShouldNotBeNull();
            armorAddon.FirstPersonModel.Male.ShouldNotBeNull();
            armorAddon.FirstPersonModel.Male.File.GivenPath.ShouldBe("MaleFirstPerson.nif");
        }
    }
}
