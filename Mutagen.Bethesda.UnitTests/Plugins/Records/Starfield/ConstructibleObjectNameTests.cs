using System.Buffers.Binary;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Starfield.Internals;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

public class ConstructibleObjectNameTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private static readonly MasterReferenceCollection Masters = new(TestModKey);

    private static ParsingMeta Meta() => new(
        GameConstants.Starfield,
        TestModKey,
        SeparatedMasterPackage.NotSeparate(Masters))
    {
        ThrowOnUnknown = true
    };

    private static byte[] Subrecord(string type, byte[] content)
    {
        var header = new byte[6];
        Encoding.ASCII.GetBytes(type).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), checked((ushort)content.Length));
        return header.Concat(content).ToArray();
    }

    private static byte[] NullTerminated(string s) => Encoding.ASCII.GetBytes(s + "\0");

    private static byte[] BuildCobjBytes(string? name)
    {
        var content = new List<byte>();
        content.AddRange(Subrecord("EDID", NullTerminated("TestRecipe")));
        if (name != null)
        {
            content.AddRange(Subrecord("FULL", NullTerminated(name)));
        }
        content.AddRange(Subrecord("DESC", NullTerminated("A description")));
        content.AddRange(Subrecord("BNAM", BitConverter.GetBytes(0x801u)));
        content.AddRange(Subrecord("NNAM", BitConverter.GetBytes((ushort)1)));
        content.AddRange(Subrecord("SNAM", BitConverter.GetBytes(0f)));
        content.AddRange(Subrecord("TNAM", new byte[] { 0 }));
        content.AddRange(Subrecord("DATA", BitConverter.GetBytes(0u)));
        var contentBytes = content.ToArray();

        var header = new byte[GameConstants.Starfield.MajorConstants.HeaderLength];
        Encoding.ASCII.GetBytes("COBJ").CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), checked((uint)contentBytes.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), 0x800);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), 555);

        return header.Concat(contentBytes).ToArray();
    }

    private static ConstructibleObject ParseDeep(byte[] bytes)
    {
        using var reader = new MutagenBinaryReadStream(new MemoryStream(bytes), Meta());
        return ConstructibleObject.CreateFromBinary(new MutagenFrame(reader));
    }

    private static IConstructibleObjectGetter ParseOverlay(byte[] bytes)
    {
        return ConstructibleObjectBinaryOverlay.ConstructibleObjectFactory(
            bytes,
            new BinaryOverlayFactoryPackage(Meta()));
    }

    private static IEnumerable<IConstructibleObjectGetter> ParseBoth(byte[] bytes)
    {
        yield return ParseDeep(bytes);
        yield return ParseOverlay(bytes);
    }

    private static byte[] Write(IConstructibleObjectGetter item)
    {
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(
                   stream,
                   new WritingBundle(GameConstants.Starfield)
                   {
                       MasterReferences = Masters,
                       SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(Masters),
                   }))
        {
            ConstructibleObjectBinaryWriteTranslation.Instance.Write(writer, item, default);
        }
        return stream.ToArray();
    }

    [Fact]
    public void Full_ReadsName()
    {
        foreach (var cobj in ParseBoth(BuildCobjBytes("Quality Upgrade")))
        {
            cobj.EditorID.ShouldBe("TestRecipe");
            cobj.Name!.String.ShouldBe("Quality Upgrade");
            ((INamedGetter)cobj).Name.ShouldBe("Quality Upgrade");
            cobj.Description!.String.ShouldBe("A description");
            cobj.WorkbenchKeyword.FormKey.ShouldBe(new FormKey(TestModKey, 0x801));
        }
    }

    [Fact]
    public void NoFull_NameIsNull()
    {
        foreach (var cobj in ParseBoth(BuildCobjBytes(null)))
        {
            cobj.Name.ShouldBeNull();
            cobj.Description!.String.ShouldBe("A description");
        }
    }

    [Theory]
    [InlineData("Quality Upgrade")]
    [InlineData(null)]
    public void Write_RoundTripsBytes(string? name)
    {
        var bytes = BuildCobjBytes(name);
        foreach (var cobj in ParseBoth(bytes))
        {
            Write(cobj).ShouldBe(bytes);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ModRoundTrip_KeepsName(bool localized)
    {
        var fileSystem = new MockFileSystem();
        var modPath = new ModPath(TestModKey, Path.Combine("C:\\", "Data", TestModKey.FileName));
        fileSystem.Directory.CreateDirectory(Path.GetDirectoryName(modPath.Path)!);

        var mod = new StarfieldMod(TestModKey, StarfieldRelease.Starfield);
        mod.UsingLocalization = localized;
        var cobj = mod.ConstructibleObjects.AddNew("TestRecipe");
        cobj.Name = "Quality Upgrade";
        mod.BeginWrite
            .ToPath(modPath)
            .WithNoLoadOrder()
            .WithFileSystem(fileSystem)
            .Write();

        if (localized)
        {
            fileSystem.File.Exists(Path.Combine(
                Path.GetDirectoryName(modPath.Path)!, "Strings", $"{TestModKey.Name}_en.STRINGS"))
                .ShouldBeTrue();
        }

        var readParams = new BinaryReadParameters()
        {
            FileSystem = fileSystem,
            ThrowOnUnknownSubrecord = true
        };
        using var overlay = StarfieldMod.CreateFromBinaryOverlay(modPath, StarfieldRelease.Starfield, readParams);
        overlay.UsingLocalization.ShouldBe(localized);
        overlay.ConstructibleObjects.Single().Name!.String.ShouldBe("Quality Upgrade");

        if (!localized)
        {
            // Deep reads do not resolve strings files from a MockFileSystem (same for Weapon names),
            // so the localized case is only checked through the overlay
            var deep = StarfieldMod.CreateFromBinary(modPath, StarfieldRelease.Starfield, readParams);
            deep.ConstructibleObjects.Single().Name!.String.ShouldBe("Quality Upgrade");
        }
    }
}
