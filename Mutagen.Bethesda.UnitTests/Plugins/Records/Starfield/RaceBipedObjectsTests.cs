using System.Buffers.Binary;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Starfield.Internals;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

public class RaceBipedObjectsTests
{
    private const int NumBipedObjects = 64;
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private static readonly FormKey TestFormKey = new(TestModKey, 0x800);

    private static Race NewRace() => new(TestFormKey, StarfieldRelease.Starfield)
    {
        EditorID = "TestRace"
    };

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Starfield, TestModKey, masters);
    }

    private static byte[] Write(IRaceGetter race)
    {
        var masters = new MasterReferenceCollection(TestModKey);
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(stream, new WritingBundle(GameConstants.Starfield)
               {
                   MasterReferences = masters,
                   SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(masters),
               }, dispose: false))
        {
            ((IBinaryItem)race).WriteToBinary(writer);
        }
        return stream.ToArray();
    }

    private static byte[] MakeRaceWithoutNamesBytes()
    {
        var bytes = Write(NewRace());
        var constants = GameConstants.Starfield;
        var header = new MajorRecordFrame(constants, bytes);
        var ret = new MemoryStream();
        ret.Write(bytes.AsSpan(0, header.HeaderLength));
        foreach (var subrecord in header.EnumerateSubrecords())
        {
            if (subrecord.RecordType == RecordTypes.NAME
                || subrecord.RecordType == RecordTypes.RBPC) continue;
            ret.Write(subrecord.HeaderAndContentData);
        }
        var stripped = ret.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(stripped.AsSpan(4), stripped.Length - header.HeaderLength);
        return stripped;
    }

    private static IRaceGetter ReadDirect(byte[] bytes)
    {
        return Race.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
    }

    private static IRaceGetter ReadOverlay(byte[] bytes)
    {
        var meta = Meta();
        return RaceBinaryOverlay.RaceFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    [Fact]
    public void OverlayWithoutNames_MatchesDirectRead()
    {
        var bytes = MakeRaceWithoutNamesBytes();
        var direct = ReadDirect(bytes);
        var overlay = ReadOverlay(bytes);

        direct.BipedObjects.Count.ShouldBe(NumBipedObjects);
        overlay.BipedObjects.Count.ShouldBe(NumBipedObjects);
        overlay.BipedObjects[BipedObject.HideHair].Name.ShouldBe(string.Empty);
        overlay.BipedObjects[BipedObject.HideHair].Conditions.IsNull.ShouldBeTrue();
        overlay.DeepCopy().BipedObjects.Count.ShouldBe(NumBipedObjects);
        // Whole record Equals is not usable here, gendered fields compare by reference
        overlay.BipedObjects.SequenceEqual(direct.BipedObjects).ShouldBeTrue();
    }

    [Fact]
    public void OverlayWithoutNames_Writes()
    {
        var bytes = MakeRaceWithoutNamesBytes();
        var expected = Write(ReadDirect(bytes));
        expected.ShouldBe(Write(NewRace()));
        Write(ReadOverlay(bytes)).ShouldBe(expected);
        Write(ReadOverlay(bytes).DeepCopy()).ShouldBe(expected);
    }

    [Fact]
    public void MutableWithMissingBipedObjects_WritesEmptyEntries()
    {
        var expected = Write(NewRace());
        var race = NewRace();
        race.BipedObjects.Remove(BipedObject.HideHair);
        Write(race).ShouldBe(expected);
        race.BipedObjects.Clear();
        Write(race).ShouldBe(expected);
    }
}
