using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Strings;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// The public round-trip API: what Mutagen keeps about a plugin and its records beyond their fields, so that a mod
// rebuilt from another form (JSON, a database) writes back like its file. Each test reads real records, rebuilds them
// as fresh objects the way a JSON reader does (new mod, copied records, new translated strings, no kept notes), shows
// the rebuild writes other bytes, then gives back the kept value through the API and gets the original bytes.
public class RoundTripApiTests
{
    private static readonly ModKey StarfieldEsm = ModKey.FromFileName("Starfield.esm");
    private static readonly ModKey Kgcdoom = ModKey.FromFileName("kgcdoom.esm");

    // kgcdoom.esm: its real header (Medium, localized; its own records in FD form)
    private const string KgcdoomTes4 = "54455334CD00000081040000000000004A34200046020000484544520C008FC2753FE13B0000001000FF434E414D0700507974686F6E00534E414D0700536372697074004D4153540E00737461726669656C642E65736D00544E414D64008800000080B900FD8EB900FD85B900FD83B900FD89B900FD7AB900FD90B900FD8FB900FD8DB900FD8CB900FD8AB900FD88B900FD86B900FD82B900FD81B900FD7FB900FD7CB900FD7BB900FD91B900FD7DB900FD84B900FD87B900FD8BB900FD7EB900FD424E414D09004465765F4D61696E00494E43430400070000004348474C0400494A4200";

    // kgcDoom_Message_NameDerelictShip (00B9B6:kgcdoom.esm), FormID FD00B9B6, FULL string ID 0x395 ("Athena")
    private const string KgcdoomMesg = "4D4553475900000000000000B6B900FD4A342000460203004544494421006B6763446F6F6D5F4D6573736167655F4E616D65446572656C69637453686970004445534304000000000046554C4C040095030000494E414D040000000000444E414D040001000000424E414D0400FFFFFFFF";

    private static byte[] Group(string label, byte[] records)
    {
        var s = new MemoryStream();
        s.Write("GRUP"u8);
        s.Write(BitConverter.GetBytes(24 + records.Length));
        s.Write(Encoding.ASCII.GetBytes(label));
        s.Write(BitConverter.GetBytes(0));
        s.Write(new byte[8]);
        s.Write(records);
        return s.ToArray();
    }

    // A .strings file with one entry
    private static byte[] StringsFile(uint id, string text)
    {
        var data = Encoding.UTF8.GetBytes(text + "\0");
        var s = new MemoryStream();
        s.Write(BitConverter.GetBytes(1));
        s.Write(BitConverter.GetBytes(data.Length));
        s.Write(BitConverter.GetBytes(id));
        s.Write(BitConverter.GetBytes(0));
        s.Write(data);
        return s.ToArray();
    }

    private static byte[] RecordBytes(byte[] file, string type)
    {
        int p = 24 + BitConverter.ToInt32(file, 4);
        while (p + 24 <= file.Length)
        {
            var sig = Encoding.ASCII.GetString(file, p, 4);
            int size = BitConverter.ToInt32(file, p + 4);
            if (sig == "GRUP") { p += 24; continue; }
            if (sig == type) return file.AsSpan(p, 24 + size).ToArray();
            p += 24 + size;
        }
        throw new InvalidOperationException(type + " not written");
    }

    // A real folder (the generated full import looks up .strings files on the real file system, not a mock one)
    private sealed class Kgc : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "MutagenRoundTripApiTests", Guid.NewGuid().ToString("N"));
        public string PluginPath => System.IO.Path.Combine(Root, "Data", Kgcdoom.FileName);
        public string OutDir => System.IO.Path.Combine(Root, "Out");
        public LoadOrder<IModMasterStyledGetter> Lo { get; } = new(new IModMasterStyledGetter[]
        {
            new KeyedMasterStyle(StarfieldEsm, MasterStyle.Full),
            new KeyedMasterStyle(Kgcdoom, MasterStyle.Medium),
        });

        public Kgc(string mesg = KgcdoomMesg)
        {
            Directory.CreateDirectory(System.IO.Path.Combine(Root, "Data", "Strings"));
            File.WriteAllBytes(PluginPath, Hex(KgcdoomTes4).Concat(Group("MESG", Hex(mesg))).ToArray());
            File.WriteAllBytes(System.IO.Path.Combine(Root, "Data", "Strings", "kgcdoom_en.strings"), StringsFile(0x395, "Athena"));
        }

        public StarfieldMod Read() => StarfieldMod.CreateFromBinary(PluginPath, StarfieldRelease.Starfield,
            new BinaryReadParameters { MasterFlagsLookup = Lo });

        public IStarfieldModDisposableGetter ReadOverlay() => StarfieldMod.CreateFromBinaryOverlay(PluginPath, StarfieldRelease.Starfield,
            new BinaryReadParameters { MasterFlagsLookup = Lo });

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }
    }

    private static byte[] WriteMesg(Kgc k, IStarfieldModGetter mod)
    {
        var outPath = Path.Combine(k.OutDir, Kgcdoom.FileName);
        Directory.CreateDirectory(k.OutDir);
        mod.WriteToBinary(outPath, new BinaryWriteParameters
        {
            MasterFlagsLookup = k.Lo,
            MastersListContent = MastersListContentOption.NoCheck,
            MastersListOrdering = new MastersListOrderingByLoadOrder(new[] { StarfieldEsm }),
        });
        return RecordBytes(File.ReadAllBytes(outPath), "MESG");
    }

    // A rebuild the way a JSON reader makes one: a new mod with the same header, the record copied, its texts new
    // translated strings holding only their text
    private static StarfieldMod Rebuild(IStarfieldModGetter read)
    {
        var mod = new StarfieldMod(read.ModKey, StarfieldRelease.Starfield);
        mod.ModHeader.DeepCopyIn(read.ModHeader);
        var mesg = read.Messages.Single().DeepCopy();
        mesg.Name = new TranslatedString(Language.English, mesg.Name!.String);
        mesg.Description = new TranslatedString(Language.English, mesg.Description.String);
        mod.Messages.Add(mesg);
        return mod;
    }

    // What a database keeps beside the JSON: each text's string ID (here FULL 0x395; the empty DESC is ID 0, which
    // reads as no text and no ID, and writes back as 0 anyway)
    private static void GiveBackStringIds(IStarfieldModGetter read, StarfieldMod rebuilt)
    {
        var from = read.Messages.Single();
        var to = rebuilt.Messages.Single();
        foreach (var (src, dst) in new[] { ((TranslatedString)from.Name!, to.Name!), ((TranslatedString)from.Description, to.Description) })
        {
            if (src.StringsKey is { } key)
            {
                dst.SetStringsKey(key, src.StringsKeySource, src.StringsKeyPlugin!.Value);
            }
        }
    }

    [Fact]
    public void OwnFormIdStyle_RebuiltMediumPluginKeepsItsFdFormIds()
    {
        using var k = new Kgc();
        var read = k.Read();
        read.GetOwnFormIdStyle().ShouldBe(ModOwnFormIdStyle.OwnStyle);
        WriteMesg(k, read).ShouldBe(Hex(KgcdoomMesg));

        var rebuilt = Rebuild(read);
        rebuilt.GetOwnFormIdStyle().ShouldBe(ModOwnFormIdStyle.Default);
        GiveBackStringIds(read, rebuilt);
        // Without the style: the plugin's own record is written in the form of its size, FD (decision A3, C163)
        BitConverter.ToUInt32(WriteMesg(k, rebuilt), 12).ShouldBe(BitConverter.ToUInt32(Hex(KgcdoomMesg), 12));

        rebuilt.SetOwnFormIdStyle(ModOwnFormIdStyle.OwnStyle);
        rebuilt.GetOwnFormIdStyle().ShouldBe(ModOwnFormIdStyle.OwnStyle);
        WriteMesg(k, rebuilt).ShouldBe(Hex(KgcdoomMesg));

        rebuilt.SetOwnFormIdStyle(ModOwnFormIdStyle.FullIndex);
        BitConverter.ToUInt32(WriteMesg(k, rebuilt), 12).ShouldBe(0x0100B9B6u);
        rebuilt.SetOwnFormIdStyle(ModOwnFormIdStyle.Default);
        rebuilt.GetOwnFormIdStyle().ShouldBe(ModOwnFormIdStyle.Default);
    }

    [Fact]
    public void StringsKey_RebuiltTextKeepsItsStringId()
    {
        using var k = new Kgc();
        var read = k.Read();
        var name = (TranslatedString)read.Messages.Single().Name!;
        name.String.ShouldBe("Athena");
        name.StringsKey.ShouldBe(0x395u);
        name.StringsKeySource.ShouldBe(StringsSource.Normal);
        name.StringsKeyPlugin.ShouldBe(Kgcdoom);

        var rebuilt = Rebuild(read);
        rebuilt.SetOwnFormIdStyle(read.GetOwnFormIdStyle());
        var rebuiltName = rebuilt.Messages.Single().Name!;
        rebuiltName.StringsKey.ShouldBeNull();
        // Without the ID the text gets a new one
        WriteMesg(k, rebuilt).ShouldNotBe(Hex(KgcdoomMesg));

        GiveBackStringIds(read, rebuilt);
        rebuiltName.StringsKey.ShouldBe(0x395u);
        rebuiltName.String.ShouldBe("Athena");
        WriteMesg(k, rebuilt).ShouldBe(Hex(KgcdoomMesg));
        File.ReadAllBytes(Path.Combine(k.OutDir, "Strings", "kgcdoom_en.strings"))
            .ShouldBe(StringsFile(0x395, "Athena"));

        // Setting the key alone keeps the table and plugin
        rebuiltName.StringsKey = 0x396;
        rebuiltName.StringsKeyPlugin.ShouldBe(Kgcdoom);
        rebuiltName.String.ShouldBe("Athena");
        rebuiltName.SetStringsKey(null, StringsSource.Normal, Kgcdoom);
        rebuiltName.StringsKeyPlugin.ShouldBeNull();
    }

    // A REFR of darkstar.esm (008DCA:darkstar.esm) whose subrecords are not in Mutagen's order: XLIG before XLVD
    private const string DarkstarRefr = "524546529200000000010000CA8D000200000000460200004E414D4504007E820E0058414C441000000000BF000000BF0000000000000000584C524414000000000000000000000000000000000001000000584C49472000000070420000803F000040409A99193E00000000000000000000000000000000584C564404000000803F584C534D04000000000044415441180003E0C842B15CBCC0721E5C3FDA0F4940DB0FC9BF00000000";

    private static byte[] Content(Action<Mutagen.Bethesda.Plugins.Binary.Streams.MutagenWriter> write)
        => GameConstants.Starfield.MajorRecord(Write(write)).Content.ToArray();

    [Fact]
    public void SubrecordOrder_RebuiltRecordKeepsItsFileOrder()
    {
        var original = GameConstants.Starfield.MajorRecord(Hex(DarkstarRefr)).Content.ToArray();
        foreach (var refr in ReadBoth<IPlacedObjectGetter>(Hex(DarkstarRefr),
                     f => PlacedObject.CreateFromBinary(f), (s, p) => PlacedObjectBinaryOverlay.PlacedObjectFactory(s, p)))
        {
            var order = refr.GetSubrecordOrder().ShouldNotBeNull();
            var rebuilt = refr.DeepCopy();
            rebuilt.SetSubrecordOrder(null);
            rebuilt.GetSubrecordOrder().ShouldBeNull();
            Content(w => rebuilt.WriteToBinary(w)).ShouldNotBe(original);

            rebuilt.SetSubrecordOrder(order);
            rebuilt.GetSubrecordOrder().ShouldBe(order);
            Content(w => rebuilt.WriteToBinary(w)).ShouldBe(original);

            // A changed record is written in Mutagen's order
            rebuilt.Scale = 2;
            Content(w => rebuilt.WriteToBinary(w)).ShouldNotBe(original);
        }
        Should.Throw<FormatException>(() => new PlacedObject(FormKey.Factory("000800:Test.esp"), StarfieldRelease.Starfield)
            .SetSubrecordOrder("not an order"));
    }

    [Fact]
    public void ProcedureTreeDataOrder_RebuiltPackageKeepsItsDataOrder()
    {
        var hex = PackageCopyKeepsOrderTests.Pilot;
        foreach (var pack in ReadBoth<IPackageGetter>(Record("PACK", Hex(hex)),
                     f => Package.CreateFromBinary(f), (s, p) => PackageBinaryOverlay.PackageFactory(s, p)))
        {
            var order = pack.GetProcedureTreeDataOrder().ShouldNotBeNull();
            var rebuilt = pack.DeepCopy();
            rebuilt.ProcedureTreeDataOrder = null;
            rebuilt.SetSubrecordOrder(null);
            Content(w => rebuilt.WriteToBinary(w)).ShouldNotBe(Hex(hex));
            rebuilt.Equals(pack).ShouldBeTrue();

            rebuilt.ProcedureTreeDataOrder = order;
            rebuilt.SetSubrecordOrder(pack.GetSubrecordOrder());
            Content(w => rebuilt.WriteToBinary(w)).ShouldBe(Hex(hex));
            rebuilt.Equals(pack).ShouldBeTrue();
            rebuilt.GetHashCode().ShouldBe(pack.DeepCopy().GetHashCode());
        }
    }
    // The same message with its header FormID stored with kgcdoom's full index (01: after its one Full master), as the
    // Creation Kit writes a Medium .esm's own records; Mutagen writes FD00B9B6 (decision A3)
    private static readonly string KgcdoomMesgFullIndex = KgcdoomMesg.Replace("B6B900FD", "B6B90001");

    [Fact]
    public void HeaderFormIdAsStored_RebuiltRecordKeepsItsHeaderForm()
    {
        using var k = new Kgc(KgcdoomMesgFullIndex);
        var read = k.Read();
        read.Messages.Single().GetHeaderFormIdAsStored().ShouldBe(0x0100B9B6u);
        using (var overlay = k.ReadOverlay())
        {
            overlay.Messages.Single().GetHeaderFormIdAsStored().ShouldBe(0x0100B9B6u);
        }
        WriteMesg(k, read).ShouldBe(Hex(KgcdoomMesgFullIndex));

        // Rebuilt as from JSON (nothing kept): Mutagen's form
        var rebuilt = Rebuild(read);
        rebuilt.Messages.Single().SetHeaderFormIdAsStored(null);
        rebuilt.Messages.Single().GetHeaderFormIdAsStored().ShouldBeNull();
        GiveBackStringIds(read, rebuilt);
        BitConverter.ToUInt32(WriteMesg(k, rebuilt), 12).ShouldBe(0xFD00B9B6u);

        // Given back: as stored
        rebuilt.Messages.Single().SetHeaderFormIdAsStored(0x0100B9B6u);
        WriteMesg(k, rebuilt).ShouldBe(Hex(KgcdoomMesgFullIndex));
    }

    [Fact]
    public void HeaderFormIdAsStored_NullWhenMutagenWritesTheSame()
    {
        using var k = new Kgc();
        k.Read().Messages.Single().GetHeaderFormIdAsStored().ShouldBeNull();
        using var overlay = k.ReadOverlay();
        overlay.Messages.Single().GetHeaderFormIdAsStored().ShouldBeNull();

        // A value that no longer reads as the record's FormKey isn't written
        var rebuilt = Rebuild(k.Read());
        GiveBackStringIds(k.Read(), rebuilt);
        rebuilt.Messages.Single().SetHeaderFormIdAsStored(0x0100B9B7u);
        WriteMesg(k, rebuilt).ShouldBe(Hex(KgcdoomMesg));
    }
}
