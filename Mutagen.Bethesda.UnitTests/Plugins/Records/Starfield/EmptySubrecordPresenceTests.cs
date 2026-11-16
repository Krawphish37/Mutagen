using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.HandReadLayoutFile;
using Noggog;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

/// <summary>
/// C65 (SFSearch S125): a subrecord present with no bytes must be told from an absent one through the public API.
/// A census of Starfield.esm, the 32 official plugins and kgcdoom.esm found every zero-length subrecord to be either a
/// marker (an end marker, a bool written as a marker, a Marker field), present wherever its structure is, or one of 16
/// data subrecords that are sometimes empty; Mutagen tells those apart in both readers (an empty list, an empty string
/// or array, against null). No GroupedPackIn LNAM is empty in any of them: the records S125 names hold patrol data
/// (XPRD, an empty XPPA marker, INAM) and no grouped pack-in.
/// </summary>
public class EmptySubrecordPresenceTests
{
    private static readonly byte[] Empty = Array.Empty<byte>();

    // A location's master unique NPCs (LCUN): an empty list when the subrecord is there with no entries
    [Fact]
    public void LocationList_PresentEmpty_IsAnEmptyList()
    {
        var present = Record("LCTN", Subrecord("EDID", Encoding.ASCII.GetBytes("Loc\0")), Subrecord("LCUN", Empty));
        var absent = Record("LCTN", Subrecord("EDID", Encoding.ASCII.GetBytes("Loc\0")));
        foreach (var location in ReadLocations(present))
        {
            location.MasterUniqueNpcs.ShouldNotBeNull();
            location.MasterUniqueNpcs!.ShouldBeEmpty();
            Contains(Write(w => location.WriteToBinary(w)), "LCUN", 0).ShouldBeTrue();
        }
        foreach (var location in ReadLocations(absent))
        {
            location.MasterUniqueNpcs.ShouldBeNull();
            Contains(Write(w => location.WriteToBinary(w)), "LCUN", 0).ShouldBeFalse();
        }

        static IEnumerable<ILocationGetter> ReadLocations(byte[] bytes) =>
            ReadBoth<ILocationGetter>(bytes, f => Location.CreateFromBinary(f), (s, p) => LocationBinaryOverlay.LocationFactory(s, p));
    }

    // A planet's master worldspaces (its first CNAM): 1,743 of Starfield.esm's 1,798 planets hold it empty
    [Fact]
    public void PlanetList_PresentEmpty_IsAnEmptyList()
    {
        var present = Record("PNDT", Subrecord("EDID", Encoding.ASCII.GetBytes("Planet\0")), Subrecord("CNAM", Empty));
        var absent = Record("PNDT", Subrecord("EDID", Encoding.ASCII.GetBytes("Planet\0")));
        foreach (var planet in ReadPlanets(present))
        {
            planet.MasterWorldspaces.ShouldNotBeNull();
            planet.MasterWorldspaces!.ShouldBeEmpty();
        }
        foreach (var planet in ReadPlanets(absent))
        {
            planet.MasterWorldspaces.ShouldBeNull();
        }

        static IEnumerable<IPlanetGetter> ReadPlanets(byte[] bytes) =>
            ReadBoth<IPlanetGetter>(bytes, f => Planet.CreateFromBinary(f), (s, p) => PlanetBinaryOverlay.PlanetFactory(s, p));
    }

    private static bool Contains(byte[] record, string type, int length)
    {
        return GameConstants.Starfield.MajorRecord(record).TryFindSubrecord(new RecordType(type), out var found)
               && found.ContentLength == length;
    }

    /// <summary>
    /// The census's 16 data subrecords that are sometimes empty, on a real record each: the record read with its empty
    /// subrecord differs from the same record without it, in the full import and in the overlay
    /// </summary>
    public static readonly (string Record, string Subrecord, string Plugin, uint FormId)[] SometimesEmpty =
    [
        ("DIAL", "TIFL", "Starfield.esm", 0x000056EE),
        ("GBFM", "PRPS", "Starfield.esm", 0x001A9562),
        ("LCTN", "LCUN", "Starfield.esm", 0x0001535B),
        ("LCTN", "LCUR", "Starfield.esm", 0x000019FB),
        ("OTFT", "INAM", "Starfield.esm", 0x003B38F2),
        ("PNDT", "CNAM", "Starfield.esm", 0x000116C1),
        ("PNDT", "EOVR", "SFBGS00D.esm", 0x0005E364),
        ("SCEN", "BTXT", "Starfield.esm", 0x003625FD),
        ("SCEN", "DNAM", "Starfield.esm", 0x00005636),
        ("SCEN", "FNAM", "Starfield.esm", 0x00005B7E),
        ("SCEN", "HTID", "Starfield.esm", 0x00005636),
        ("SCEN", "PNAM", "Starfield.esm", 0x00005B8A),
        ("SCEN", "VNAM", "Starfield.esm", 0x0000D360),
        ("WEAP", "BNAM", "Starfield.esm", 0x000297F6),
        ("WRLD", "WHGT", "Starfield.esm", 0x00002D62),
        ("WRLD", "XCLW", "Starfield.esm", 0x00002D62),
    ];

    [Fact]
    [Trait("Category", "RealData")]
    public void RealData_SometimesEmptySubrecords_TellPresentFromAbsent()
    {
        var data = Environment.GetEnvironmentVariable("MUTAGEN_STARFIELD_DATA");
        if (string.IsNullOrEmpty(data)) return; // needs the game's Data folder
        var styles = new Cache<IModMasterStyledGetter, ModKey>(x => x.ModKey);
        foreach (var name in HandReadLayoutFileTests.OfficialPlugins)
        {
            styles.Set(new KeyedMasterStyle(ModKey.FromFileName(name), ModHeaderFrame.FromPath(Path.Combine(data, name), GameRelease.Starfield).MasterStyle));
        }
        var param = new BinaryReadParameters { MasterFlagsLookup = styles };
        foreach (var group in SometimesEmpty.GroupBy(x => x.Plugin))
        {
            var path = new ModPath(ModKey.FromFileName(group.Key), Path.Combine(data, group.Key));
            var bytes = File.ReadAllBytes(path.Path);
            using var reader = MajorRecordReader.FromPath(path, GameRelease.Starfield, param);
            var package = SeparatedMasterPackage.Factory(GameRelease.Starfield, path, styles);
            foreach (var (record, subrecord, _, formId) in group)
            {
                var (with, without) = Variants(bytes, record, formId, subrecord);
                var what = $"{group.Key} {record} {formId:X8} {subrecord}";
                reader.Read(with).Equals(reader.Read(without)).ShouldBeFalse(what);
                var overlayWith = Overlay(with, package, path.ModKey);
                var overlayWithout = Overlay(without, package, path.ModKey);
                overlayWith.Equals(overlayWithout).ShouldBeFalse(what + " (overlay)");
            }
        }
    }

    /// <summary>
    /// The records S125 names (sfta03.esm 0020F9, 0020FB; kgcdoom.esm 008B9A, 008B9C) have no grouped pack-in: Mutagen
    /// reads GroupedPackIn as null and their patrol data (its empty XPPA marker) as Patrol
    /// </summary>
    [Fact]
    [Trait("Category", "RealData")]
    public void RealData_S125Records_HaveNoGroupedPackIn()
    {
        var data = Environment.GetEnvironmentVariable("MUTAGEN_STARFIELD_DATA");
        if (string.IsNullOrEmpty(data)) return; // needs the game's Data folder
        var styles = new Cache<IModMasterStyledGetter, ModKey>(x => x.ModKey);
        foreach (var name in HandReadLayoutFileTests.OfficialPlugins.Append("kgcdoom.esm"))
        {
            styles.Set(new KeyedMasterStyle(ModKey.FromFileName(name), ModHeaderFrame.FromPath(Path.Combine(data, name), GameRelease.Starfield).MasterStyle));
        }
        var param = new BinaryReadParameters { MasterFlagsLookup = styles };
        foreach (var (plugin, ids) in new[] { ("sfta03.esm", new uint[] { 0x20F9, 0x20FB }), ("kgcdoom.esm", new uint[] { 0x8B9A, 0x8B9C }) })
        {
            var path = new ModPath(ModKey.FromFileName(plugin), Path.Combine(data, plugin));
            var bytes = File.ReadAllBytes(path.Path);
            using var reader = MajorRecordReader.FromPath(path, GameRelease.Starfield, param);
            var found = 0;
            foreach (var position in RecordPositions(bytes, "REFR"))
            {
                var placed = (IPlacedObjectGetter)reader.ReadAt(position);
                if (placed.FormKey.ModKey != path.ModKey || !ids.Contains(placed.FormKey.ID)) continue;
                found++;
                placed.GroupedPackIn.ShouldBeNull();
                placed.Patrol.ShouldNotBeNull();
            }
            found.ShouldBe(ids.Length, plugin);
        }
    }

    // A record read by the overlay from its bytes alone, through its class's overlay factory
    private static IMajorRecordGetter Overlay(byte[] record, IReadOnlySeparatedMasterPackage package, ModKey modKey)
    {
        var meta = new ParsingMeta(GameConstants.Starfield, modKey, package);
        var cls = MajorRecordReader.GetRecordClass(GameCategory.Starfield, new MajorRecordHeader(GameConstants.Starfield, record).RecordType)!;
        var overlay = cls.Assembly.GetType($"{cls.Namespace}.{cls.Name}BinaryOverlay")!;
        var factory = overlay.GetMethod($"{cls.Name}Factory", [typeof(OverlayStream), typeof(BinaryOverlayFactoryPackage), typeof(TypedParseParams)])!;
        return (IMajorRecordGetter)factory.Invoke(null, [new OverlayStream(record, meta), new BinaryOverlayFactoryPackage(meta), default(TypedParseParams)])!;
    }

    private static IEnumerable<long> RecordPositions(byte[] b, string type)
    {
        long pos = 24 + BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4));
        while (pos < b.Length)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan((int)pos + 4));
            if (b.AsSpan((int)pos, 4).SequenceEqual("GRUP"u8))
            {
                pos += 24;
                continue;
            }
            if (Encoding.ASCII.GetString(b, (int)pos, 4) == type) yield return pos;
            pos += 24 + size;
        }
    }

    // A record uncompressed, and the same without its first empty subrecord of a type
    private static (byte[] With, byte[] Without) Variants(byte[] b, string type, uint formId, string subrecord)
    {
        var pos = (int)RecordPositions(b, type).First(p => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan((int)p + 12)) == formId);
        var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos + 4));
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(pos + 8));
        var content = b.AsSpan(pos + 24, size).ToArray();
        if ((flags & 0x40000) != 0)
        {
            using var z = new ZLibStream(new MemoryStream(content, 4, content.Length - 4), CompressionMode.Decompress);
            var ms = new MemoryStream();
            z.CopyTo(ms);
            content = ms.ToArray();
            flags &= ~0x40000u;
        }
        var header = b.AsSpan(pos, 24).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), flags);
        int p = 0, cut = -1;
        while (p + 6 <= content.Length)
        {
            var s = Encoding.ASCII.GetString(content, p, 4);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(p + 4));
            if (s == "XXXX" && length == 4)
            {
                // The next subrecord's length is this one's content
                var big = (int)BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(p + 6));
                p += 10 + 6 + big;
                continue;
            }
            if (s == subrecord && length == 0)
            {
                cut = p;
                break;
            }
            p += 6 + length;
        }
        cut.ShouldBeGreaterThanOrEqualTo(0, $"{type} {formId:X8} has no empty {subrecord}");
        var without = content.Take(cut).Concat(content.Skip(cut + 6)).ToArray();
        return (Make(content), Make(without));

        byte[] Make(byte[] body)
        {
            var ret = new byte[24 + body.Length];
            header.CopyTo(ret, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(ret.AsSpan(4), (uint)body.Length);
            body.CopyTo(ret, 24);
            return ret;
        }
    }
}
