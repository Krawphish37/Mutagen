using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Fallout4;

// An object template / OMOD property is a fixed 24-byte struct. For the String value type (3)
// the value is 4 bytes at offset 12. It used to be read with ReadOnlySpan<byte>.ToString()
// and written null terminated (25 bytes), which shifted every following property.
public class ObjectTemplateStringPropertyTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Fallout4, TestModKey, masters);
    }

    private static byte[] StringPropertyBytes(byte[] value, uint unused = 0, float step = 0)
    {
        var bytes = new byte[24];
        bytes[0] = 3; // ValueType.String
        bytes[4] = (byte)ObjectModProperty.FloatFunctionType.Add;
        BitConverter.GetBytes((uint)Weapon.Property.Reach).CopyTo(bytes, 8);
        value.CopyTo(bytes, 12);
        BitConverter.GetBytes(unused).CopyTo(bytes, 16);
        BitConverter.GetBytes(step).CopyTo(bytes, 20);
        return bytes;
    }

    private static byte[] Write(IAObjectModPropertyGetter<Weapon.Property> property)
    {
        var masters = new MasterReferenceCollection(TestModKey);
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(
                   stream,
                   new WritingBundle(GameConstants.Fallout4)
                   {
                       MasterReferences = masters,
                       SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(masters),
                   }))
        {
            ObjectTemplateBinaryWriteTranslation.WriteProperty(writer, property);
        }
        return stream.ToArray();
    }

    [Fact]
    public void ReadProperty_StringValue()
    {
        var bytes = StringPropertyBytes("ABCD"u8.ToArray(), unused: 0x12345678, step: 1.5f);

        var prop = ObjectTemplateBinaryCreateTranslation<Weapon.Property>.ReadProperty(Meta(), bytes);

        var stringProp = prop.ShouldBeOfType<ObjectModStringProperty<Weapon.Property>>();
        stringProp.Value.ShouldBe("ABCD");
        stringProp.Unused.ShouldBe(0x12345678u);
        stringProp.Property.ShouldBe(Weapon.Property.Reach);
        stringProp.Step.ShouldBe(1.5f);
    }

    [Theory]
    [InlineData("ABCD", new byte[] { 0x41, 0x42, 0x43, 0x44 })]
    [InlineData("AB", new byte[] { 0x41, 0x42, 0, 0 })]
    [InlineData("", new byte[] { 0, 0, 0, 0 })]
    public void WriteProperty_StringValueIsFixedFourBytes(string value, byte[] expectedValueBytes)
    {
        var bytes = Write(new ObjectModStringProperty<Weapon.Property>()
        {
            Property = Weapon.Property.Reach,
            FunctionType = ObjectModProperty.FloatFunctionType.Add,
            Value = value,
            Unused = 0x12345678,
            Step = 1.5f,
        });

        bytes.ShouldBe(StringPropertyBytes(expectedValueBytes, unused: 0x12345678, step: 1.5f));
    }
}
