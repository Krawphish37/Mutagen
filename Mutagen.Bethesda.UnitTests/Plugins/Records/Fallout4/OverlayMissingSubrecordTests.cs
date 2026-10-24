using System.Text;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Fallout4;

// Hand-written overlay getters that checked the wrong subrecord, or none,
// and so disagreed with the full import when a subrecord was missing.
public class OverlayMissingSubrecordTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Fallout4, TestModKey, masters);
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
        record.Write(BitConverter.GetBytes((ushort)131));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static byte[] EditorID(string edid) => Subrecord("EDID", Encoding.ASCII.GetBytes(edid + "\0"));

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(x => x).ToArray();

    private static byte[] U32(uint value) => BitConverter.GetBytes(value);

    // Flags = CantBeTaken | Perk, Teaches = local 0x900, TextOffsetX = 7, TextOffsetY = 8
    private static readonly byte[] BookDnam = Concat(new byte[] { 0x12 }, U32(0x900), U32(7), U32(8));
    private static readonly byte[] BookData = Concat(U32(25), BitConverter.GetBytes(0.5f));

    private static IEnumerable<IBookGetter> ReadBooks(byte[] bytes)
    {
        yield return Book.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return BookBinaryOverlay.BookFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    private static IEnumerable<IPackageGetter> ReadPackages(byte[] bytes)
    {
        yield return Package.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return PackageBinaryOverlay.PackageFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    private static IEnumerable<IWaterGetter> ReadWaters(byte[] bytes)
    {
        yield return Water.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return WaterBinaryOverlay.WaterFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    private static IEnumerable<IHolotapeGetter> ReadHolotapes(byte[] bytes)
    {
        yield return Holotape.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return HolotapeBinaryOverlay.HolotapeFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    private static FormKey? HolotapeLink(IAHolotapeDataGetter? data)
    {
        return data switch
        {
            IHolotapeSoundGetter sound => sound.Sound.FormKeyNullable,
            IHolotapeVoiceGetter voice => voice.Scene.FormKeyNullable,
            IHolotapeTerminalGetter term => term.Terminal.FormKeyNullable,
            _ => throw new ArgumentException(data?.GetType().Name),
        };
    }

    [Fact]
    public void Book_DataWithoutDnam_FlagsAndTeachesAreDefault()
    {
        var bytes = Record("BOOK", EditorID("BookDataOnly"), Subrecord("DATA", BookData));

        foreach (var book in ReadBooks(bytes))
        {
            book.Value.ShouldBe(25u);
            book.Flags.ShouldBe(default(Book.Flag));
            book.Teaches.ShouldBeNull();
            Should.NotThrow(() => book.DeepCopy());
        }
    }

    [Fact]
    public void Book_DnamWithoutData_ReadsFlagsAndTeaches()
    {
        var bytes = Record("BOOK", EditorID("BookDnamOnly"), Subrecord("DNAM", BookDnam));

        foreach (var book in ReadBooks(bytes))
        {
            ((int)book.Flags).ShouldBe(0x12);
            var perk = book.Teaches.ShouldBeAssignableTo<IBookPerkGetter>();
            perk!.Perk.FormKey.ShouldBe(new FormKey(TestModKey, 0x900));
            book.TextOffsetX.ShouldBe(7u);
        }
    }

    [Fact]
    public void Package_WithoutPkcu_PackageTemplateIsNullLink()
    {
        var bytes = Record("PACK", EditorID("PackEdidOnly"));

        foreach (var package in ReadPackages(bytes))
        {
            package.PackageTemplate.ShouldNotBeNull();
            package.PackageTemplate.IsNull.ShouldBeTrue();
            Should.NotThrow(() => package.EnumerateFormLinks().ToArray());
            Should.NotThrow(() => package.DeepCopy());
        }
    }

    [Fact]
    public void Water_WithoutDnam_NoiseLayersAreDefault()
    {
        var bytes = Record("WATR",
            EditorID("WaterNoDnam"),
            Subrecord("NAM2", Encoding.ASCII.GetBytes("noise.dds\0")));

        foreach (var water in ReadWaters(bytes))
        {
            water.NoiseLayerOne.WindSpeed.ShouldBe(0f);
            water.NoiseLayerOne.Texture.ShouldBe("noise.dds");
            water.NoiseLayerTwo.WindDirection.ShouldBe(0f);
            water.NoiseLayerThree.NoiseFalloff.ShouldBe(0f);
            Should.NotThrow(() => water.DeepCopy());
        }
    }

    [Fact]
    public void Water_WithDnam_ReadsInterleavedNoiseLayers()
    {
        // Noise layer floats start at 0x80: WindDirection x3, WindSpeed x3, AmplitudeScale x3, UvScale x3, NoiseFalloff x3
        var dnam = new byte[0xBC];
        for (int i = 0; i < 15; i++)
        {
            BitConverter.GetBytes((float)(i + 1)).CopyTo(dnam, 0x80 + i * 4);
        }
        var bytes = Record("WATR", EditorID("WaterDnam"), Subrecord("DNAM", dnam));

        foreach (var water in ReadWaters(bytes))
        {
            water.NoiseLayerOne.WindDirection.ShouldBe(1f);
            water.NoiseLayerTwo.WindDirection.ShouldBe(2f);
            water.NoiseLayerThree.WindDirection.ShouldBe(3f);
            water.NoiseLayerOne.WindSpeed.ShouldBe(4f);
            water.NoiseLayerTwo.AmplitudeScale.ShouldBe(8f);
            water.NoiseLayerThree.UvScale.ShouldBe(12f);
            water.NoiseLayerThree.NoiseFalloff.ShouldBe(15f);
        }
    }

    [Theory]
    [InlineData(Holotape.Types.Sound)]
    [InlineData(Holotape.Types.Voice)]
    [InlineData(Holotape.Types.Terminal)]
    public void Holotape_LinkTypeWithPnam_IgnoresPnam(Holotape.Types type)
    {
        var bytes = Record("NOTE",
            EditorID("HolotapePnam"),
            Subrecord("DNAM", new byte[] { (byte)type }),
            Subrecord("PNAM", Encoding.ASCII.GetBytes("abc\0")));

        foreach (var holotape in ReadHolotapes(bytes))
        {
            (HolotapeLink(holotape.Data)?.IsNull ?? true).ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData(Holotape.Types.Sound, false)]
    [InlineData(Holotape.Types.Sound, true)]
    [InlineData(Holotape.Types.Voice, false)]
    [InlineData(Holotape.Types.Voice, true)]
    [InlineData(Holotape.Types.Terminal, false)]
    [InlineData(Holotape.Types.Terminal, true)]
    public void Holotape_LinkTypeWithSnamAndPnam_ReadsSnam(Holotape.Types type, bool pnamFirst)
    {
        var snam = Subrecord("SNAM", U32(0x900));
        var pnam = Subrecord("PNAM", Encoding.ASCII.GetBytes("abc\0"));
        var bytes = Record("NOTE",
            EditorID("HolotapeBoth"),
            Subrecord("DNAM", new byte[] { (byte)type }),
            pnamFirst ? pnam : snam,
            pnamFirst ? snam : pnam);

        foreach (var holotape in ReadHolotapes(bytes))
        {
            HolotapeLink(holotape.Data).ShouldBe(new FormKey(TestModKey, 0x900));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Holotape_ProgramWithSnam_ReadsOnlyPnam(bool withPnam)
    {
        var subrecords = new List<byte[]>
        {
            EditorID("HolotapeProgramSnam"),
            Subrecord("DNAM", new byte[] { (byte)Holotape.Types.Program }),
        };
        if (withPnam)
        {
            subrecords.Add(Subrecord("PNAM", Encoding.ASCII.GetBytes("prog.swf\0")));
        }
        subrecords.Add(Subrecord("SNAM", U32(0x900)));
        var bytes = Record("NOTE", subrecords.ToArray());

        foreach (var holotape in ReadHolotapes(bytes))
        {
            var program = holotape.Data.ShouldBeAssignableTo<IHolotapeProgramGetter>();
            program!.File.ShouldBe(withPnam ? "prog.swf" : null);
        }
    }
}
