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
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

/// <summary>
/// HAZD OPDS and PDCL DEFL / XALG, as found in official Creations (sfta03.esm, sfbgs00b.esm)
/// </summary>
public class ObjectPaletteSubrecordTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private static readonly MasterReferenceCollection Masters = new(TestModKey);

    // OPDS content of sfta03.esm HAZD FD0113BE SFTA03_TeslaCoil_Hazard
    private static readonly byte[] Opds = Convert.FromHexString(
        "00000000000000000000000000000000050000000000803F0000800000000000" +
        "00000000000000000000000000000000000000000000803F000000000000803F" +
        "0000803F0000000000000000FFFF7F7F");

    private const ulong Xalg = 0x0000_0000_0000_0040;
    private const uint LayerId = 0x801;
    private const uint MaterialId = 0x802;

    private static byte[] Subrecord(string type, byte[] content)
    {
        var header = new byte[6];
        Encoding.ASCII.GetBytes(type).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), checked((ushort)content.Length));
        return header.Concat(content).ToArray();
    }

    private static byte[] Record(string type, params byte[][] subrecords)
    {
        var content = subrecords.SelectMany(x => x).ToArray();
        var header = new byte[GameConstants.Starfield.MajorConstants.HeaderLength];
        Encoding.ASCII.GetBytes(type).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)content.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 0x800);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 555);
        return header.Concat(content).ToArray();
    }

    private static byte[] Edid(string s) => Encoding.ASCII.GetBytes(s + "\0");
    private static byte[] Obnd() => new byte[24];
    private static byte[] Odty() => BitConverter.GetBytes(1f);

    // EDID OBND ODTY OPDS XALG DNAM, the order the Creation Kit writes
    private static byte[] HazardBytes() => Record(
        "HAZD",
        Subrecord("EDID", Edid("TestHazard")),
        Subrecord("OBND", Obnd()),
        Subrecord("ODTY", Odty()),
        Subrecord("OPDS", Opds),
        Subrecord("XALG", BitConverter.GetBytes(Xalg)),
        Subrecord("DNAM", new byte[92]));

    // EDID OBND ODTY DEFL DODT DATA, as sfbgs00b.esm PDCL FD002053
    private static byte[] DecalWithLayerBytes() => Record(
        "PDCL",
        Subrecord("EDID", Edid("TestDecalLayer")),
        Subrecord("OBND", Obnd()),
        Subrecord("ODTY", Odty()),
        Subrecord("DEFL", BitConverter.GetBytes(LayerId)),
        Subrecord("DODT", BitConverter.GetBytes(MaterialId)),
        Subrecord("DATA", new byte[24]));

    // EDID OBND ODTY OPDS XALG DODT DATA, as sfbgs00b.esm PDCL FD002058
    private static byte[] DecalWithXalgBytes() => Record(
        "PDCL",
        Subrecord("EDID", Edid("TestDecalXalg")),
        Subrecord("OBND", Obnd()),
        Subrecord("ODTY", Odty()),
        Subrecord("OPDS", Opds),
        Subrecord("XALG", BitConverter.GetBytes(Xalg)),
        Subrecord("DODT", BitConverter.GetBytes(MaterialId)),
        Subrecord("DATA", new byte[24]));

    private static ParsingMeta Meta(bool throwOnUnknown) => new(
        GameConstants.Starfield,
        TestModKey,
        SeparatedMasterPackage.NotSeparate(Masters))
    {
        ThrowOnUnknown = throwOnUnknown
    };

    private static IEnumerable<IHazardGetter> ReadHazard(byte[] bytes, bool throwOnUnknown)
    {
        yield return Hazard.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta(throwOnUnknown))));
        yield return HazardBinaryOverlay.HazardFactory(bytes, new BinaryOverlayFactoryPackage(Meta(throwOnUnknown)));
    }

    private static IEnumerable<IProjectedDecalGetter> ReadDecal(byte[] bytes, bool throwOnUnknown)
    {
        yield return ProjectedDecal.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta(throwOnUnknown))));
        yield return ProjectedDecalBinaryOverlay.ProjectedDecalFactory(bytes, new BinaryOverlayFactoryPackage(Meta(throwOnUnknown)));
    }

    private static byte[] Write(IMajorRecordGetter record)
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
            ((IBinaryItem)record).WriteToBinary(writer);
        }
        return stream.ToArray();
    }

    [Fact]
    public void Hazard_ObjectPaletteDefaults_Parsed()
    {
        foreach (var hazard in ReadHazard(HazardBytes(), throwOnUnknown: true))
        {
            hazard.ObjectPaletteDefaults.ShouldNotBeNull();
            ((ulong?)hazard.XalgFlags).ShouldBe(Xalg);
        }
    }

    [Fact]
    public void Hazard_ObjectPaletteDefaults_RoundTrips()
    {
        var bytes = HazardBytes();
        foreach (var hazard in ReadHazard(bytes, throwOnUnknown: false))
        {
            Write(hazard).ShouldBe(bytes);
        }
    }

    [Fact]
    public void ProjectedDecal_DefaultLayer_Parsed()
    {
        foreach (var decal in ReadDecal(DecalWithLayerBytes(), throwOnUnknown: true))
        {
            decal.DefaultLayer.FormKey.ShouldBe(new FormKey(TestModKey, LayerId));
            decal.XalgFlags.ShouldBeNull();
            decal.Material.FormKey.ShouldBe(new FormKey(TestModKey, MaterialId));
        }
    }

    [Fact]
    public void ProjectedDecal_DefaultLayer_RoundTrips()
    {
        var bytes = DecalWithLayerBytes();
        foreach (var decal in ReadDecal(bytes, throwOnUnknown: false))
        {
            Write(decal).ShouldBe(bytes);
        }
    }

    [Fact]
    public void ProjectedDecal_Xalg_Parsed()
    {
        foreach (var decal in ReadDecal(DecalWithXalgBytes(), throwOnUnknown: true))
        {
            decal.ObjectPaletteDefaults.ShouldNotBeNull();
            ((ulong?)decal.XalgFlags).ShouldBe(Xalg);
            decal.DefaultLayer.IsNull.ShouldBeTrue();
        }
    }

    [Fact]
    public void ProjectedDecal_Xalg_RoundTrips()
    {
        var bytes = DecalWithXalgBytes();
        foreach (var decal in ReadDecal(bytes, throwOnUnknown: false))
        {
            Write(decal).ShouldBe(bytes);
        }
    }
}
