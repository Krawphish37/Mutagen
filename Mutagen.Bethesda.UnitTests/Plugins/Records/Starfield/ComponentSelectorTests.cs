using System.Text;
using System.Xml.Linq;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C57 (W4): which BFCB text picks which component kind is in Component.xml: selectedBy="subrecord:BFCB:string" and
// selectorEnum="AComponent.ComponentType" on AComponent, selectorValue (the member name) on each of its kinds. Held
// against the hand-written code: AComponent.CreateFromBinary and AComponentBinaryOverlay.AComponentFactory make the kind
// the xml names for each value, and AComponentBinaryWriteTranslation writes that value back for the kind.
public class ComponentSelectorTests
{
    private static readonly XNamespace Loqui = "http://tempuri.org/LoquiSource.xsd";

    private static string ComponentXml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Mutagen.Bethesda.Starfield", "Records", "Common Subrecords", "Component.xml");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Component.xml");
    }

    // kind -> selectorValue, for every object deriving from AComponent
    private static Dictionary<string, string?> Kinds(out XElement aComponent)
    {
        var objects = XDocument.Load(ComponentXml()).Root!.Elements(Loqui + "Object").ToList();
        aComponent = objects.Single(o => (string?)o.Attribute("name") == "AComponent");
        return objects.Where(o => (string?)o.Attribute("baseClass") == "AComponent")
            .ToDictionary(o => (string)o.Attribute("name")!, o => (string?)o.Attribute("selectorValue"));
    }

    private static byte[] Bfcb(string text)
    {
        var content = Encoding.ASCII.GetBytes(text + "\0");
        return [.. Subrecord("BFCB", content), .. Subrecord("BFCE", Array.Empty<byte>())];
    }

    [Fact]
    public void EveryKind_HasTheValueTheReaderAndWriterUse()
    {
        var kinds = Kinds(out var aComponent);
        ((string?)aComponent.Attribute("selectedBy")).ShouldBe("subrecord:BFCB:string");
        ((string?)aComponent.Attribute("selectorEnum")).ShouldBe("AComponent.ComponentType");
        kinds.Count.ShouldBe(67);
        kinds.Where(k => k.Value == null).Select(k => k.Key).ShouldBeEmpty("component kinds without a selectorValue");
        // C63: the catch-all, for a BFCB that names no kind exactly
        kinds.Where(k => k.Value == "*").Select(k => k.Key).ShouldBe(["UnknownComponent"]);
        kinds.Remove("UnknownComponent");
        // one kind per member of the enum the reader parses BFCB into
        kinds.Values.Order().ShouldBe(Enum.GetNames<AComponent.ComponentType>().Order());

        foreach (var (kind, value) in kinds)
        {
            var bytes = Bfcb(value!);
            IAComponentGetter full = AComponent.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())), default);
            full.GetType().Name.ShouldBe(kind, value);
            var meta = Meta();
            var overlay = AComponentBinaryOverlay.AComponentFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta), default);
            overlay.GetType().Name.ShouldBe(kind + "BinaryOverlay", value);
            var written = Write(w => full.WriteToBinary(w));
            Encoding.ASCII.GetString(written, 6, BitConverter.ToUInt16(written, 4)).ShouldBe(value + "\0", kind);
        }
    }

    // C63: the reader matches the names exactly. A BFCB that names no kind (a number, white space, two names, another
    // case, an unknown name) is read, with both readers, as an UnknownComponent that keeps its text and its subrecords,
    // and the record writes back byte for byte. No plugin in Data has one (census of the 370 plugins: 549,798 BFCB, all
    // exactly a member's name and a zero).
    [Theory]
    [InlineData("5")]
    [InlineData(" BGSScannable")]
    [InlineData("BGSScannable ")]
    [InlineData("BGSScannable, BGSDisplayCase")]
    [InlineData("bgsscannable")]
    [InlineData("BGSNotAComponent")]
    [InlineData("")]
    public void NotAnExactName_IsKeptAsStored_BothReaders(string text)
    {
        // the malformed component holds a subrecord, an XXXX-sized one, then a known component follows
        byte[] components =
        [
            .. Subrecord("BFCB", Encoding.ASCII.GetBytes(text + "\0")),
            .. Subrecord("ANAM", Encoding.ASCII.GetBytes("abc\0")),
            .. Subrecord("XXXX", BitConverter.GetBytes(3)), .. Encoding.ASCII.GetBytes("DATA"), 0, 0, 1, 2, 3,
            .. Subrecord("BFCE", Array.Empty<byte>()),
            .. Bfcb("BGSScannable"),
        ];
        var bytes = Record("STAT", [.. Subrecord("EDID", Encoding.ASCII.GetBytes("Layout\0")), .. components]);
        foreach (var stat in ReadBoth<IStaticGetter>(bytes, f => Static.CreateFromBinary(f), (s, p) => StaticBinaryOverlay.StaticFactory(s, p)))
        {
            stat.Components.Count.ShouldBe(2);
            var unknown = stat.Components[0].ShouldBeAssignableTo<IUnknownComponentGetter>()!;
            unknown.TypeName.ShouldBe(text);
            unknown.Data.Length.ShouldBe(6 + 4 + 6 + 4 + 6 + 3);
            stat.Components[1].ShouldBeAssignableTo<IScannableComponentGetter>();
            var written = Write(w => stat.WriteToBinary(w));
            var from = Array.IndexOf(written, (byte)'B');
            while (Encoding.ASCII.GetString(written, from, 4) != "BFCB") from = Array.IndexOf(written, (byte)'B', from + 1);
            written.AsSpan(from, components.Length).ToArray().ShouldBe(components, text);
        }
        Should.Throw<ArgumentException>(() => AComponent.GetComponentType(
            new Mutagen.Bethesda.Plugins.Binary.Headers.SubrecordFrame(GameConstants.Starfield, components)));
    }

    [Fact]
    public void ExactName_PicksTheKind()
    {
        var bfcb = new Mutagen.Bethesda.Plugins.Binary.Headers.SubrecordFrame(GameConstants.Starfield, Bfcb("BGSScannable"));
        AComponent.TryGetComponentType(bfcb, out var kind).ShouldBeTrue();
        kind.ShouldBe(AComponent.ComponentType.BGSScannable);
        // no closing zero, or bytes after it: no kind
        AComponent.TryGetComponentType(new Mutagen.Bethesda.Plugins.Binary.Headers.SubrecordFrame(GameConstants.Starfield,
            Subrecord("BFCB", Encoding.ASCII.GetBytes("BGSScannable"))), out _).ShouldBeFalse();
        AComponent.TryGetComponentType(new Mutagen.Bethesda.Plugins.Binary.Headers.SubrecordFrame(GameConstants.Starfield,
            Subrecord("BFCB", Encoding.ASCII.GetBytes("BGSScannable\0x\0"))), out _).ShouldBeFalse();
    }
}
