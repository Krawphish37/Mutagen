using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Noggog;
using Shouldly;
using Xunit;
using TempFile = Noggog.IO.TempFile;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// An object template / OMOD property is a fixed 24-byte struct. For the String value type (3)
// the value is 4 bytes at offset 12. It used to be read with ReadOnlySpan<byte>.ToString()
// (giving "System.ReadOnlySpan<Byte>[4]") and written null terminated (25 bytes), which
// shifted every following property in the subrecord.
public class ObjectTemplateStringPropertyTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Starfield, TestModKey, masters);
    }

    private static byte[] StringPropertyBytes(byte[] value, uint unused = 0, float step = 0)
    {
        var bytes = new byte[24];
        bytes[0] = 3; // ValueType.String
        bytes[4] = (byte)ObjectModProperty.FloatFunctionType.Add;
        BitConverter.GetBytes((uint)Weapon.Property.AmmoCapacity).CopyTo(bytes, 8);
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
                   new WritingBundle(GameConstants.Starfield)
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
        stringProp.Property.ShouldBe(Weapon.Property.AmmoCapacity);
        stringProp.FunctionType.ShouldBe(ObjectModProperty.FloatFunctionType.Add);
        stringProp.Step.ShouldBe(1.5f);
    }

    [Fact]
    public void ReadProperty_ShortStringValueIsNullTrimmed()
    {
        var bytes = StringPropertyBytes(new byte[] { 0x41, 0x42, 0, 0 });

        var prop = ObjectTemplateBinaryCreateTranslation<Weapon.Property>.ReadProperty(Meta(), bytes);

        prop.ShouldBeOfType<ObjectModStringProperty<Weapon.Property>>().Value.ShouldBe("AB");
    }

    [Theory]
    [InlineData("ABCD", new byte[] { 0x41, 0x42, 0x43, 0x44 })]
    [InlineData("AB", new byte[] { 0x41, 0x42, 0, 0 })]
    [InlineData("", new byte[] { 0, 0, 0, 0 })]
    public void WriteProperty_StringValueIsFixedFourBytes(string value, byte[] expectedValueBytes)
    {
        var bytes = Write(new ObjectModStringProperty<Weapon.Property>()
        {
            Property = Weapon.Property.AmmoCapacity,
            FunctionType = ObjectModProperty.FloatFunctionType.Add,
            Value = value,
            Unused = 0x12345678,
            Step = 1.5f,
        });

        bytes.ShouldBe(StringPropertyBytes(expectedValueBytes, unused: 0x12345678, step: 1.5f));
    }

    [Fact]
    public void WeaponObjectTemplate_StringThenFloatProperty_RoundTrips()
    {
        using var tmp = new TempFile(extraDirectoryPaths: TestPathing.TempFolderPath, suffix: ".esp");
        var mod = new StarfieldMod(ModKey.FromNameAndExtension("ObjectTemplateStringProperty.esp"), StarfieldRelease.Starfield);
        var weapon = mod.Weapons.AddNew("ObjectTemplateStringProperty");
        var template = new ObjectTemplate<Weapon.Property>();
        template.Properties.Add(new ObjectModStringProperty<Weapon.Property>()
        {
            Property = Weapon.Property.AmmoCapacity,
            Value = "ABCD",
            Step = 1f,
        });
        template.Properties.Add(new ObjectModFloatProperty<Weapon.Property>()
        {
            Property = Weapon.Property.AmmoCapacity,
            Value = 1.5f,
            Value2 = 2.5f,
            Step = 0.25f,
        });
        weapon.ObjectTemplates = new ExtendedList<ObjectTemplate<Weapon.Property>> { template };
        var path = new ModPath(mod.ModKey, tmp.File.Path);
        mod.BeginWrite
            .ToPath(path)
            .WithNoLoadOrder()
            .NoModKeySync()
            .Write();

        void Check(IReadOnlyList<IAObjectModPropertyGetter<Weapon.Property>> props)
        {
            props.Count.ShouldBe(2);
            var stringProp = props[0].ShouldBeAssignableTo<IObjectModStringPropertyGetter<Weapon.Property>>()!;
            stringProp.Value.ShouldBe("ABCD");
            stringProp.Step.ShouldBe(1f);
            var floatProp = props[1].ShouldBeAssignableTo<IObjectModFloatPropertyGetter<Weapon.Property>>()!;
            floatProp.Value.ShouldBe(1.5f);
            floatProp.Value2.ShouldBe(2.5f);
            floatProp.Step.ShouldBe(0.25f);
        }

        var reloaded = StarfieldMod.CreateFromBinary(path, StarfieldRelease.Starfield);
        Check(reloaded.Weapons.First().ObjectTemplates![0].Properties);

        using var overlay = StarfieldMod.CreateFromBinaryOverlay(path, StarfieldRelease.Starfield);
        Check(overlay.Weapons.First().ObjectTemplates![0].Properties);
    }
}
