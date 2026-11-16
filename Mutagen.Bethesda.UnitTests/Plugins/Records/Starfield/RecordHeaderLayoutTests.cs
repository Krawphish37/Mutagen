using System.Buffers.Binary;
using System.Xml.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.HandReadLayoutFile;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C57: where the record header's fields sit, in Mutagen's shipped files: the record xml (layoutSubrecord="header" and
// layoutOffset, from the record's first byte, on Core's MajorRecord.xml and Starfield's StarfieldMajorRecord.xml and
// StarfieldModHeader.xml) and Layouts/HandReadLayouts.xml's Header entries (the whole 24 bytes, with the views of the
// flags: IsDeleted, IsCompressed, StarfieldMajorRecordFlags, MajorFlags). Held against the bytes Mutagen reads and writes
// and against each other. The real-data check of the Header entries runs in HandReadLayoutFileTests.
public class RecordHeaderLayoutTests
{
    private static readonly XNamespace Loqui = "http://tempuri.org/LoquiSource.xsd";

    // Object.Field -> its offset in the record header
    private static readonly Dictionary<string, int> Expected = new()
    {
        ["MajorRecord.MajorRecordFlagsRaw"] = 8,
        ["MajorRecord.FormKey"] = 12,
        ["MajorRecord.VersionControl"] = 16,
        ["StarfieldMajorRecord.FormVersion"] = 20,
        ["StarfieldMajorRecord.Version2"] = 22,
        ["StarfieldMajorRecord.StarfieldMajorRecordFlags"] = 8,
        ["StarfieldModHeader.Flags"] = 8,
        ["StarfieldModHeader.FormID"] = 12,
        ["StarfieldModHeader.Version"] = 16,
        ["StarfieldModHeader.FormVersion"] = 20,
        ["StarfieldModHeader.Version2"] = 22,
    };

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine([dir.FullName, .. parts]);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }

    private static readonly string[] HeaderXmls =
    [
        RepoFile("Mutagen.Bethesda.Core", "Plugins", "Records", "MajorRecord.xml"),
        RepoFile("Mutagen.Bethesda.Starfield", "Records", "StarfieldMajorRecord.xml"),
        RepoFile("Mutagen.Bethesda.Starfield", "Records", "StarfieldModHeader.xml"),
    ];

    // Object.Field -> offset, for every field of those files placed in the record header
    private static Dictionary<string, int> XmlPlaced()
    {
        var ret = new Dictionary<string, int>();
        foreach (var file in HeaderXmls)
        {
            foreach (var obj in XDocument.Load(file).Root!.Elements(Loqui + "Object"))
            {
                foreach (var field in obj.Element(Loqui + "Fields")!.Elements())
                {
                    if ((string?)field.Attribute("layoutSubrecord") != "header") continue;
                    ret.Add($"{obj.Attribute("name")!.Value}.{field.Attribute("name")!.Value}", int.Parse((string)field.Attribute("layoutOffset")!));
                }
            }
        }
        return ret;
    }

    [Fact]
    public void RecordXml_PlacesEveryHeaderField()
    {
        XmlPlaced().OrderBy(x => x.Key).ShouldBe(Expected.OrderBy(x => x.Key));
    }

    // The layout file's Header entries: Field -> offset (a View or Bit at the offset of the value it views)
    private static Dictionary<string, int> LayoutPlaced(XElement header)
    {
        var ret = new Dictionary<string, int>();
        var named = new Dictionary<string, int>();
        var at = 0;
        foreach (var e in header.Elements())
        {
            int size = e.Name.LocalName switch
            {
                "Bytes" => int.Parse((string)e.Attribute("length")!),
                "U32" or "I32" or "FormID" => 4,
                "U16" or "I16" => 2,
                "View" or "Bit" => 0,
                var other => throw new InvalidDataException($"{other} in a Header"),
            };
            var place = size == 0 ? named[(string)e.Attribute("of")!] : at;
            if (e.Attribute("name") is { } name) named[name.Value] = at;
            if (e.Attribute("field") is { } field) ret[field.Value] = place;
            at += size;
        }
        at.ShouldBe(24);
        return ret;
    }

    private static Dictionary<string, XElement> Headers() =>
        XDocument.Load(HandReadLayoutFileTests.LayoutPath()).Root!.Elements("Header")
            .ToDictionary(h => (string?)h.Attribute("sig") ?? "");

    [Fact]
    public void LayoutFileHeaders_AgreeWithTheRecordXml()
    {
        var headers = Headers();
        headers.Keys.ShouldBe(["", "TES4"], ignoreOrder: true);
        ((string)headers[""].Attribute("object")!).ShouldBe("StarfieldMajorRecord");
        ((string)headers["TES4"].Attribute("object")!).ShouldBe("StarfieldModHeader");

        var record = LayoutPlaced(headers[""]);
        var fromXml = Expected.Where(x => !x.Key.StartsWith("StarfieldModHeader.")).ToDictionary(x => x.Key.Split('.')[1], x => x.Value);
        // the record xml's fields, and the views of the flags that no xml field is
        var views = new KeyValuePair<string, int>[] { new("IsDeleted", 8), new("IsCompressed", 8), new("MajorFlags", 8) };
        record.ShouldBe(fromXml.Concat(views).ToDictionary(), ignoreOrder: true);
        var mod = LayoutPlaced(headers["TES4"]);
        mod.ShouldBe(Expected.Where(x => x.Key.StartsWith("StarfieldModHeader.")).ToDictionary(x => x.Key.Split('.')[1], x => x.Value), ignoreOrder: true);

        // the bits the flag views are: Core's MajorRecord's deleted (0x20) and compressed (0x40000) flags
        var bits = headers[""].Elements("Bit").ToDictionary(b => (string)b.Attribute("field")!, b => 1 << int.Parse((string)b.Attribute("index")!));
        bits.ShouldBe(new Dictionary<string, int> { ["IsDeleted"] = 0x20, ["IsCompressed"] = 0x4_0000 }, ignoreOrder: true);
    }

    private static int Offset(string key) => XmlPlaced()[key];

    // A keyword (an own record of the test mod, which has no masters) with every header field set apart
    [Fact]
    public void RecordHeader_BytesAreWhereTheXmlSays_BothReaders()
    {
        var bytes = Record("KYWD", Subrecord("EDID", "41424300"));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(Offset("MajorRecord.MajorRecordFlagsRaw")), 0x0010_0420);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(Offset("MajorRecord.FormKey")), 0x0000_0812);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(Offset("MajorRecord.VersionControl")), 0xA1B2_C3D4);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(Offset("StarfieldMajorRecord.FormVersion")), 555);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(Offset("StarfieldMajorRecord.Version2")), 7);
        Offset("StarfieldMajorRecord.StarfieldMajorRecordFlags").ShouldBe(Offset("MajorRecord.MajorRecordFlagsRaw"));
        foreach (var kywd in ReadBoth<IKeywordGetter>(bytes, f => Keyword.CreateFromBinary(f), (s, p) => KeywordBinaryOverlay.KeywordFactory(s, p)))
        {
            kywd.MajorRecordFlagsRaw.ShouldBe(0x0010_0420);
            kywd.IsDeleted.ShouldBeTrue();
            kywd.IsCompressed.ShouldBeFalse();
            ((int)kywd.StarfieldMajorRecordFlags).ShouldBe(0x0010_0420);
            ((int)kywd.MajorFlags).ShouldBe(0x0010_0420);
            kywd.FormKey.ShouldBe(new FormKey(TestModKey, 0x812));
            kywd.VersionControl.ShouldBe(0xA1B2_C3D4);
            kywd.FormVersion.ShouldBe((ushort)555);
            kywd.Version2.ShouldBe((ushort)7);
            Write(w => kywd.WriteToBinary(w)).AsSpan(8, 16).ToArray().ShouldBe(bytes.AsSpan(8, 16).ToArray());
        }
    }

    // The plugin header: its own five fields, then HEDR
    [Fact]
    public void PluginHeader_BytesAreWhereTheXmlSays_BothReaders()
    {
        var bytes = Record("TES4", Subrecord("HEDR", "8FC2753F 05000000 00080000"));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(Offset("StarfieldModHeader.Flags")), 0x0000_0101);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(Offset("StarfieldModHeader.FormID")), 0x0000_0ABC);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(Offset("StarfieldModHeader.Version")), 0x0102_0304);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(Offset("StarfieldModHeader.FormVersion")), 575);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(Offset("StarfieldModHeader.Version2")), 9);
        var full = StarfieldModHeader.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        var overlay = StarfieldModHeaderBinaryOverlay.StarfieldModHeaderFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
        foreach (var header in new IStarfieldModHeaderGetter[] { full, overlay })
        {
            header.Flags.ShouldBe(StarfieldModHeader.HeaderFlag.Master | StarfieldModHeader.HeaderFlag.Light);
            header.FormID.ShouldBe(0xABCu);
            header.Version.ShouldBe(0x0102_0304);
            header.FormVersion.ShouldBe((ushort)575);
            header.Version2.ShouldBe((ushort)9);
            header.Stats.NumRecords.ShouldBe(5u);
            Write(w => header.WriteToBinary(w)).AsSpan(0, bytes.Length).ToArray().ShouldBe(bytes);
        }
    }

    // The checker reads the Header entries as the real-data run does, here over a written record and plugin header
    [Fact]
    public void LayoutChecker_ReadsTheHeaders()
    {
        var headers = Headers();
        var bytes = Record("KYWD", Subrecord("EDID", "41424300"));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 0x20);
        var kywd = Keyword.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var checker = new LayoutChecker(Meta().MasterReferences, localized: false);
        checker.CheckHeader(headers[""], bytes[..24], kywd);
        checker.Errors.ShouldBeEmpty();
        checker.Checked.Keys.ShouldContain("Header/BitIsDeleted");
        checker.Checked.Keys.ShouldContain("Header/ViewMajorFlags");

        // a value Mutagen doesn't hold is caught: the record read with other header bytes
        var other = (byte[])bytes.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(other.AsSpan(20), 1);
        checker.CheckHeader(headers[""], other[..24], kywd);
        checker.Errors.ShouldHaveSingleItem().ShouldContain("FormVersion");

        var tes4 = Record("TES4", Subrecord("HEDR", "8FC2753F 05000000 00080000"));
        var header = StarfieldModHeader.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(tes4, Meta())));
        var modChecker = new LayoutChecker(Meta().MasterReferences, localized: false);
        modChecker.CheckHeader(headers["TES4"], tes4[..24], header);
        modChecker.Errors.ShouldBeEmpty();
    }
}
