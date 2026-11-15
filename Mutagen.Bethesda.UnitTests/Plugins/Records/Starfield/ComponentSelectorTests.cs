using System.Text;
using System.Xml.Linq;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
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
        kinds.Count.ShouldBe(66);
        kinds.Where(k => k.Value == null).Select(k => k.Key).ShouldBeEmpty("component kinds without a selectorValue");
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
}
