using System.Text;
using Mutagen.Bethesda.Fallout3;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Fallout3;

// Hand-written overlay getters for fields of a data subrecord must return the
// same defaults as the full import when that subrecord is absent.
public class OverlayCustomGetterMissingSubrecordTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Fallout3, TestModKey, masters);
    }

    private static byte[] Subrecord(string type, byte[] content)
    {
        var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(BitConverter.GetBytes(checked((ushort)content.Length)));
        stream.Write(content);
        return stream.ToArray();
    }

    private static byte[] Record(string type, params byte[][] subrecords)
    {
        var body = subrecords.SelectMany(x => x).ToArray();
        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes(type));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)15));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static byte[] EditorID(string edid) => Subrecord("EDID", Encoding.ASCII.GetBytes(edid + "\0"));

    private static IEnumerable<IWeaponGetter> ReadWeapons(byte[] bytes)
    {
        yield return Weapon.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return WeaponBinaryOverlay.WeaponFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    [Fact]
    public void Weapon_WithoutDnam_FlagsAreDefault()
    {
        var bytes = Record("WEAP", EditorID("WeapEdidOnly"));

        foreach (var weapon in ReadWeapons(bytes))
        {
            weapon.Flags.ShouldBe(default(Weapon.WeaponFlag));
            weapon.Reach.ShouldBe(0f);
            var copy = Should.NotThrow(() => weapon.DeepCopy());
            copy.Flags.ShouldBe(default(Weapon.WeaponFlag));
        }
    }

    [Fact]
    public void Weapon_WithDnam_CombinesFlags()
    {
        var dnam = new byte[0x78];
        BitConverter.GetBytes(2.5f).CopyTo(dnam, 0x8);
        dnam[0xC] = 0x5;
        BitConverter.GetBytes(0x3u).CopyTo(dnam, 0x38);
        var bytes = Record("WEAP", EditorID("WeapDnam"), Subrecord("DNAM", dnam));

        foreach (var weapon in ReadWeapons(bytes))
        {
            ((uint)weapon.Flags).ShouldBe(0x305u);
            weapon.Reach.ShouldBe(2.5f);
        }
    }
}
