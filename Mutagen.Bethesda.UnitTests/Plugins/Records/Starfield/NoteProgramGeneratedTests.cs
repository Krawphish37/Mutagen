using System.Text;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C43: the generator crashed making the overlay of a nullable ByteArray with no record type and no length
// (ByteArrayBinaryTranslationGeneration.GenerateWrapperFields), so C39 kept NoteProgram.Unknown out of generation
// (binary="NoGeneration") and its overlay returned null. It now generates: the rest of the data, null when nothing
// remains, in both readers. On a NOTE, Note.cs still reads and writes it as the SNAM subrecord (NoteSnamTests); these
// tests read NoteProgram's own (typeless) binary, File then Unknown, with the generated code of both readers.
public class NoteProgramGeneratedTests
{
    private static byte[] Bytes(string file, byte[] rest) => Encoding.ASCII.GetBytes(file + "\0").Concat(rest).ToArray();

    private static IEnumerable<INoteProgramGetter> Read(byte[] bytes) => ReadBoth<INoteProgramGetter>(bytes,
        f => NoteProgram.CreateFromBinary(f), (s, p) => NoteProgramBinaryOverlay.NoteProgramFactory(s, p));

    private static byte[] Written(INoteProgramGetter prog) => Write(w => ((IBinaryItem)prog).WriteToBinary(w));

    [Fact]
    public void FileThenUnknown_BothReaders_WrittenBack()
    {
        var bytes = Bytes("Program.swf", Hex("DEADBEEF"));
        foreach (var prog in Read(bytes))
        {
            prog.File.ShouldBe("Program.swf");
            prog.Unknown!.Value.ToArray().ShouldBe(Hex("DEADBEEF"));
            Written(prog).ShouldBe(bytes);
            Written(prog.DeepCopy()).ShouldBe(bytes);
        }
    }

    // Any length, as Note.cs keeps it
    [Fact]
    public void UnknownOfAnotherLength_KeptWhole()
    {
        var bytes = Bytes("P", Hex("0102030405"));
        foreach (var prog in Read(bytes))
        {
            prog.Unknown!.Value.ToArray().ShouldBe(Hex("0102030405"));
            Written(prog).ShouldBe(bytes);
        }
    }

    [Fact]
    public void NothingAfterFile_UnknownIsNull()
    {
        var bytes = Bytes("Program.swf", []);
        foreach (var prog in Read(bytes))
        {
            prog.File.ShouldBe("Program.swf");
            prog.Unknown.ShouldBeNull();
            Written(prog).ShouldBe(bytes);
        }
    }

    // The overlay's factory moves the stream past the whole object (it stopped after File before)
    [Fact]
    public void OverlayFactory_EndsAfterUnknown()
    {
        var bytes = Bytes("Program.swf", Hex("DEADBEEF"));
        var meta = Meta();
        var stream = new OverlayStream(bytes, meta);
        NoteProgramBinaryOverlay.NoteProgramFactory(stream, new BinaryOverlayFactoryPackage(meta));
        stream.Position.ShouldBe(bytes.Length);
    }

    [Fact]
    public void NewProgram_WritesFileThenUnknown()
    {
        var prog = new NoteProgram { File = "a", Unknown = Hex("01020304") };
        Written(prog).ShouldBe(Bytes("a", Hex("01020304")));
    }
}
