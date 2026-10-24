using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Hand-written overlay getters that checked the wrong subrecord, or none,
// and so disagreed with the full import when a subrecord was missing or of the other kind.
public class OverlayMissingSubrecordTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Starfield, TestModKey, masters);
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
        record.Write(BitConverter.GetBytes((ushort)555));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static byte[] EditorID(string edid) => Subrecord("EDID", Encoding.ASCII.GetBytes(edid + "\0"));

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(x => x).ToArray();

    private static byte[] U32(uint value) => BitConverter.GetBytes(value);

    // Flags = CantBeTaken | Perk, Teaches = local 0x900, TextOffsetX = 7, TextOffsetY = 8, DataSlateType = Text
    private static readonly byte[] BookDnam = Concat(new byte[] { 0x12 }, U32(0x900), U32(7), U32(8), U32(1));
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

    private static IEnumerable<INoteGetter> ReadNotes(byte[] bytes)
    {
        yield return Note.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return NoteBinaryOverlay.NoteFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
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
    public void Package_WithPkcu_ReadsPackageTemplate()
    {
        var bytes = Record("PACK",
            EditorID("PackPkcu"),
            Subrecord("PKCU", Concat(U32(0), U32(0x900), U32(3))));

        foreach (var package in ReadPackages(bytes))
        {
            package.PackageTemplate.FormKey.ShouldBe(new FormKey(TestModKey, 0x900));
        }
    }

    [Fact]
    public void Note_VoiceWithPnam_IgnoresPnam()
    {
        var bytes = Record("NOTE",
            EditorID("NoteVoicePnam"),
            Subrecord("DNAM", new byte[] { (byte)Note.Types.Voice }),
            Subrecord("PNAM", Encoding.ASCII.GetBytes("abc\0")));

        foreach (var note in ReadNotes(bytes))
        {
            var voice = note.Data.ShouldBeAssignableTo<INoteVoiceGetter>();
            voice!.Scene.IsNull.ShouldBeTrue();
        }
    }

    [Fact]
    public void Note_ProgramWithSnam_IgnoresSnam()
    {
        var bytes = Record("NOTE",
            EditorID("NoteProgramSnam"),
            Subrecord("DNAM", new byte[] { (byte)Note.Types.Program }),
            Subrecord("SNAM", U32(0x900)));

        foreach (var note in ReadNotes(bytes))
        {
            var program = note.Data.ShouldBeAssignableTo<INoteProgramGetter>();
            program!.File.ShouldBeNull();
        }
    }

    [Fact]
    public void Note_VoiceWithSnamAndPnam_ReadsSnam()
    {
        var bytes = Record("NOTE",
            EditorID("NoteVoiceBoth"),
            Subrecord("DNAM", new byte[] { (byte)Note.Types.Voice }),
            Subrecord("SNAM", U32(0x900)),
            Subrecord("PNAM", Encoding.ASCII.GetBytes("abc\0")));

        foreach (var note in ReadNotes(bytes))
        {
            var voice = note.Data.ShouldBeAssignableTo<INoteVoiceGetter>();
            voice!.Scene.FormKey.ShouldBe(new FormKey(TestModKey, 0x900));
        }
    }
}
