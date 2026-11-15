using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Starfield.Internals;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C39: NOTE's SNAM is a union decided by DNAM (xEdit wbNoteTypeDecider): Sound (0) -> SNDR, Voice (1) -> SCEN,
// Terminal (3) -> TERM, Program (2) -> 4 unused bytes. Mutagen had no Sound type (a Sound note threw) and dropped a
// Program note's SNAM. No plugin in the Data folder has a NOTE record, so these records are hand-built from xEdit's
// layout (wbDefinitionsSF1.pas NOTE): EDID, OBND, ODTY, DNAM, DATA {Value u32, Weight float}, SNAM, PNAM.
public class NoteSnamTests
{
    private const uint LinkId = 0x000801;
    private static readonly FormKey Link = new(TestModKey, LinkId);

    private static byte[] Note(byte type, byte[]? snam = null, string? pnam = null)
    {
        var subs = new List<byte[]>
        {
            Subrecord("EDID", Encoding.ASCII.GetBytes("TestNote\0")),
            Subrecord("OBND", new byte[24]),
            Subrecord("ODTY", BitConverter.GetBytes(0f)),
            Subrecord("DNAM", new[] { type }),
            Subrecord("DATA", BitConverter.GetBytes(25u).Concat(BitConverter.GetBytes(0.5f)).ToArray()),
        };
        if (snam != null) subs.Add(Subrecord("SNAM", snam));
        if (pnam != null) subs.Add(Subrecord("PNAM", Encoding.ASCII.GetBytes(pnam + "\0")));
        return Record("NOTE", subs.ToArray());
    }

    private static IEnumerable<INoteGetter> ReadAndCheckRoundTrip(byte[] record)
    {
        foreach (var note in ReadBoth<INoteGetter>(record, f => Mutagen.Bethesda.Starfield.Note.CreateFromBinary(f), (s, p) => NoteBinaryOverlay.NoteFactory(s, p)))
        {
            note.EditorID.ShouldBe("TestNote");
            note.Value.ShouldBe(25u);
            note.Weight.ShouldBe(0.5f);
            var written = GameConstants.Starfield.MajorRecord(Write(w => note.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(GameConstants.Starfield.MajorRecord(record).Content.ToArray());
            yield return note;
        }
    }

    [Fact]
    public void Sound_WithSnam()
    {
        var count = 0;
        foreach (var note in ReadAndCheckRoundTrip(Note(0, BitConverter.GetBytes(LinkId))))
        {
            var sound = note.Data.ShouldBeAssignableTo<INoteSoundGetter>()!;
            sound.Sound.FormKey.ShouldBe(Link);
            count++;
        }
        count.ShouldBe(2);
    }

    [Fact]
    public void Sound_WithoutSnam()
    {
        var count = 0;
        foreach (var note in ReadAndCheckRoundTrip(Note(0)))
        {
            note.Data.ShouldBeAssignableTo<INoteSoundGetter>()!.Sound.IsNull.ShouldBeTrue();
            count++;
        }
        count.ShouldBe(2);
    }

    [Fact]
    public void Voice_WithSnam()
    {
        var count = 0;
        foreach (var note in ReadAndCheckRoundTrip(Note(1, BitConverter.GetBytes(LinkId))))
        {
            note.Data.ShouldBeAssignableTo<INoteVoiceGetter>()!.Scene.FormKey.ShouldBe(Link);
            count++;
        }
        count.ShouldBe(2);
    }

    [Fact]
    public void Voice_WithoutSnam()
    {
        var count = 0;
        foreach (var note in ReadAndCheckRoundTrip(Note(1)))
        {
            note.Data.ShouldBeAssignableTo<INoteVoiceGetter>()!.Scene.IsNull.ShouldBeTrue();
            count++;
        }
        count.ShouldBe(2);
    }

    [Fact]
    public void Program_WithSnamAndPnam()
    {
        var count = 0;
        foreach (var note in ReadAndCheckRoundTrip(Note(2, Hex("DEADBEEF"), "Programs\\Test.swf")))
        {
            var prog = note.Data.ShouldBeAssignableTo<INoteProgramGetter>()!;
            prog.Unknown!.Value.ToArray().ShouldBe(Hex("DEADBEEF"));
            prog.File.ShouldBe("Programs\\Test.swf");
            count++;
        }
        count.ShouldBe(2);
    }

    [Fact]
    public void Program_WithSnamOnly()
    {
        var count = 0;
        foreach (var note in ReadAndCheckRoundTrip(Note(2, Hex("01020304"))))
        {
            var prog = note.Data.ShouldBeAssignableTo<INoteProgramGetter>()!;
            prog.Unknown!.Value.ToArray().ShouldBe(Hex("01020304"));
            prog.File.ShouldBeNull();
            count++;
        }
        count.ShouldBe(2);
    }

    [Fact]
    public void Program_WithPnamOnly()
    {
        var count = 0;
        foreach (var note in ReadAndCheckRoundTrip(Note(2, pnam: "Programs\\Test.swf")))
        {
            var prog = note.Data.ShouldBeAssignableTo<INoteProgramGetter>()!;
            prog.Unknown.ShouldBeNull();
            prog.File.ShouldBe("Programs\\Test.swf");
            count++;
        }
        count.ShouldBe(2);
    }

    [Fact]
    public void Program_WithNeither()
    {
        var count = 0;
        foreach (var note in ReadAndCheckRoundTrip(Note(2)))
        {
            var prog = note.Data.ShouldBeAssignableTo<INoteProgramGetter>()!;
            prog.Unknown.ShouldBeNull();
            prog.File.ShouldBeNull();
            count++;
        }
        count.ShouldBe(2);
    }

    [Fact]
    public void Terminal_WithSnam()
    {
        var count = 0;
        foreach (var note in ReadAndCheckRoundTrip(Note(3, BitConverter.GetBytes(LinkId))))
        {
            note.Data.ShouldBeAssignableTo<INoteTerminalGetter>()!.Terminal.FormKey.ShouldBe(Link);
            count++;
        }
        count.ShouldBe(2);
    }

    [Fact]
    public void Terminal_WithoutSnam()
    {
        var count = 0;
        foreach (var note in ReadAndCheckRoundTrip(Note(3)))
        {
            note.Data.ShouldBeAssignableTo<INoteTerminalGetter>()!.Terminal.IsNull.ShouldBeTrue();
            count++;
        }
        count.ShouldBe(2);
    }

    [Fact]
    public void Sound_NewRecord_WritesSnamOnlyWhenSet()
    {
        var note = new Mutagen.Bethesda.Starfield.Note(new FormKey(TestModKey, 0x900), StarfieldRelease.Starfield)
        {
            Data = new NoteSound(),
        };
        INoteGetter getter = note;
        var written = GameConstants.Starfield.MajorRecord(Write(w => getter.WriteToBinary(w)));
        written.TryFindSubrecord(RecordTypes.SNAM, out _).ShouldBeFalse();
        written.FindSubrecord(RecordTypes.DNAM).Content[0].ShouldBe((byte)0);

        ((NoteSound)note.Data).Sound.SetTo(Link);
        written = GameConstants.Starfield.MajorRecord(Write(w => getter.WriteToBinary(w)));
        written.FindSubrecord(RecordTypes.SNAM).AsUInt32().ShouldBe(LinkId);
    }
}
