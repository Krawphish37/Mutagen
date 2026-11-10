using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C131: the Planet Content Manager content-properties component's ZNAM is a float. Every ZNAM of that component in
// the 370 plugins of the Data folder (fieldscan census) is 0 or 1.0f; the only non-zero ones are 16 WRLD in
// suitpowercores.esm (0x3F800000). xEdit: wbUnknown(ZNAM, 4), "presume this is the Days Until Reset manual value".
// C132: ARMA DNAM's 2 bytes after the priorities are one byte per sex (Fallout 4/Skyrim keep WeightSliderEnabled there);
// 01 01 on 9 ARMA in svc3holsters.esm/svc3holstersx1.esm, 0 everywhere else.
// Both tests use real subrecords read (read only) from those plugins, read them with both readers and write them back
// byte for byte. Only subrecords without links to masters are taken, so the test's single-plugin master list fits.
public class SmallTypesR7Tests
{
    // zzspcpoi01 (01002626:suitpowercores.esm): EDID and its Planet Content Manager component block
    private static readonly byte[][] PcmComponent =
    {
        Subrecord("EDID", "7A7A737063706F69303100"),
        Subrecord("BFCB", "424753506C616E6574436F6E74656E744D616E61676572436F6E74656E7450726F706572746965735F436F6D706F6E656E7400"),
        Subrecord("ZNAM", "0000803F"),
        Subrecord("YNAM", "00"),
        Subrecord("XNAM", "00000000"),
        Subrecord("WNAM", "00000000"),
        Subrecord("VNAM", "01"),
        Subrecord("UNAM", "00000000"),
        Subrecord("NAM1", "00000000"),
        Subrecord("NAM2", "00000000"),
        Subrecord("NAM3", "00000000"),
        Subrecord("NAM4", "00FF0000"),
        Subrecord("NAM5", "00000000"),
        Subrecord("NAM6", "00000000"),
        Subrecord("NAM7", "00000000"),
        Subrecord("NAM8", "00"),
        Subrecord("NAM9", "01000000"),
        Subrecord("CITC", "00000000"),
        Subrecord("BFCE", ""),
    };

    private static IEnumerable<IWorldspaceGetter> ReadWorld(byte[][] subs)
        => ReadBoth<IWorldspaceGetter>(Record("WRLD", subs), f => Worldspace.CreateFromBinary(f), (s, p) => WorldspaceBinaryOverlay.WorldspaceFactory(s, p));

    [Fact]
    public void PlanetContentManagerZnam_IsAFloat()
    {
        var block = PcmComponent.Skip(1).SelectMany(x => x).ToArray();
        foreach (var world in ReadWorld(PcmComponent))
        {
            var pcm = world.Components.OfType<IPlanetContentManagerContentPropertiesComponentGetter>().ShouldHaveSingleItem();
            pcm.ZNAM.ShouldBe(1.0f);
            pcm.SavePlacement.ShouldBe((byte)1);
            var written = GameConstants.Starfield.MajorRecord(Write(w => world.WriteToBinary(w)));
            written.Content.ToArray().AsSpan().IndexOf(block).ShouldBeGreaterThanOrEqualTo(0); // the whole component block, byte for byte
        }
    }

    [Fact]
    public void PlanetContentManagerZnam_ZeroRoundTrips()
    {
        var subs = PcmComponent.Select(s => s[..4].SequenceEqual("ZNAM"u8.ToArray()) ? Subrecord("ZNAM", "00000000") : s).ToArray();
        var block = subs.Skip(1).SelectMany(x => x).ToArray();
        foreach (var world in ReadWorld(subs))
        {
            world.Components.OfType<IPlanetContentManagerContentPropertiesComponentGetter>().ShouldHaveSingleItem().ZNAM.ShouldBe(0f);
            var written = GameConstants.Starfield.MajorRecord(Write(w => world.WriteToBinary(w)));
            written.Content.ToArray().AsSpan().IndexOf(block).ShouldBeGreaterThanOrEqualTo(0);
        }
    }

    // scxSkinUpperbodyAA (01000887:svc3holsters.esm): EDID and DNAM (Weapon Adjust 0, priorities 0/0, then 01 01,
    // Detection Sound Value 0, Health Bar Offset 0)
    private const string Dnam = "00000000000001010000000000";

    [Fact]
    public void ArmorAddonDnam_BytePerSex()
    {
        var bytes = Record("ARMA", Subrecord("EDID", "736378536B696E5570706572626F6479414100"), Subrecord("DNAM", Dnam));
        foreach (var arma in ReadBoth<IArmorAddonGetter>(bytes, f => ArmorAddon.CreateFromBinary(f), (s, p) => ArmorAddonBinaryOverlay.ArmorAddonFactory(s, p)))
        {
            arma.UnknownMale.ShouldBe((byte)1);
            arma.UnknownFemale.ShouldBe((byte)1);
            arma.MalePriority.ShouldBe((byte)0);
            arma.FemalePriority.ShouldBe((byte)0);
            var written = GameConstants.Starfield.MajorRecord(Write(w => arma.WriteToBinary(w)));
            written.FindSubrecord(new RecordType("DNAM")).Content.ToArray().ShouldBe(Hex(Dnam));
        }
    }
}
