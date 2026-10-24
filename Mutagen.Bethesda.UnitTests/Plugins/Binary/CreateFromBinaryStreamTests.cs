using Mutagen.Bethesda.Fallout3;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Utility;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

/// <summary>
/// The Stream overloads of CreateFromBinary / CopyInFromBinary must parse the mod header
/// with the release that was passed in, not with Oblivion's 20 byte record header.
/// </summary>
public class CreateFromBinaryStreamTests
{
    private static readonly ModKey TestModKey = ModKey.FromFileName("StreamTest.esp");
    private static readonly ModKey MasterModKey = ModKey.FromFileName("StreamMaster.esm");

    private static byte[] Write(IModGetter mod)
    {
        using var stream = new MemoryStream();
        mod.WriteToBinary(stream);
        return stream.ToArray();
    }

    private static RecordTypeInfoCacheReader InfoCache(byte[] bytes, GameRelease release)
    {
        return new RecordTypeInfoCacheReader(
            () => new MutagenMemoryReadStream(
                bytes,
                new ParsingMeta(
                    GameConstants.Get(release),
                    TestModKey,
                    SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey)))),
            TestModKey);
    }

    [Fact]
    public void Starfield()
    {
        var mod = new StarfieldMod(TestModKey, StarfieldRelease.Starfield);
        mod.Keywords.AddNew("StreamKeyword");
        var bytes = Write(mod);

        var read = StarfieldMod.CreateFromBinary(
            new MemoryStream(bytes), TestModKey, StarfieldRelease.Starfield,
            InfoCache(bytes, GameRelease.Starfield));
        read.Keywords.Select(x => x.EditorID).ShouldBe(new[] { "StreamKeyword" });

        read = StarfieldMod.CreateFromBinary(
            new MemoryStream(bytes), TestModKey, StarfieldRelease.Starfield,
            InfoCache(bytes, GameRelease.Starfield), errorMask: null);
        read.Keywords.Select(x => x.EditorID).ShouldBe(new[] { "StreamKeyword" });

        var copyIn = new StarfieldMod(TestModKey, StarfieldRelease.Starfield);
        copyIn.CopyInFromBinary(
            new MemoryStream(bytes), TestModKey, StarfieldRelease.Starfield,
            InfoCache(bytes, GameRelease.Starfield));
        copyIn.Keywords.Select(x => x.EditorID).ShouldBe(new[] { "StreamKeyword" });
    }

    [Fact]
    public void SkyrimSE()
    {
        var mod = new SkyrimMod(TestModKey, SkyrimRelease.SkyrimSE);
        mod.Keywords.AddNew("StreamKeyword");
        var bytes = Write(mod);

        var read = SkyrimMod.CreateFromBinary(
            new MemoryStream(bytes), TestModKey, SkyrimRelease.SkyrimSE,
            InfoCache(bytes, GameRelease.SkyrimSE));
        read.Keywords.Select(x => x.EditorID).ShouldBe(new[] { "StreamKeyword" });

        var copyIn = new SkyrimMod(TestModKey, SkyrimRelease.SkyrimSE);
        copyIn.CopyInFromBinary(
            new MemoryStream(bytes), TestModKey, SkyrimRelease.SkyrimSE,
            InfoCache(bytes, GameRelease.SkyrimSE));
        copyIn.Keywords.Select(x => x.EditorID).ShouldBe(new[] { "StreamKeyword" });
    }

    [Fact]
    public void SkyrimSE_MutableBuilderFromStreamFactory()
    {
        var mod = new SkyrimMod(TestModKey, SkyrimRelease.SkyrimSE);
        mod.Keywords.AddNew("StreamKeyword");
        var bytes = Write(mod);

        var read = SkyrimMod.Create(SkyrimRelease.SkyrimSE)
            .FromStreamFactory(() => new MemoryStream(bytes), TestModKey)
            .Mutable()
            .Construct();
        read.Keywords.Select(x => x.EditorID).ShouldBe(new[] { "StreamKeyword" });
    }

    [Fact]
    public void SkyrimSE_WithMaster()
    {
        var mod = new SkyrimMod(TestModKey, SkyrimRelease.SkyrimSE);
        mod.Keywords.Add(new Mutagen.Bethesda.Skyrim.Keyword(new FormKey(MasterModKey, 0x801), SkyrimRelease.SkyrimSE)
        {
            EditorID = "OverriddenKeyword"
        });
        var bytes = Write(mod);

        var read = SkyrimMod.CreateFromBinary(
            new MemoryStream(bytes), TestModKey, SkyrimRelease.SkyrimSE,
            InfoCache(bytes, GameRelease.SkyrimSE));
        read.ModHeader.MasterReferences.Select(x => x.Master).ShouldBe(new[] { MasterModKey });
        read.Keywords.Select(x => x.FormKey).ShouldBe(new[] { new FormKey(MasterModKey, 0x801) });
    }

    [Fact]
    public void Fallout4()
    {
        var mod = new Fallout4Mod(TestModKey, Fallout4Release.Fallout4);
        mod.Keywords.AddNew("StreamKeyword");
        var bytes = Write(mod);

        var read = Fallout4Mod.CreateFromBinary(
            new MemoryStream(bytes), TestModKey, Fallout4Release.Fallout4,
            InfoCache(bytes, GameRelease.Fallout4));
        read.Keywords.Select(x => x.EditorID).ShouldBe(new[] { "StreamKeyword" });
    }

    [Fact]
    public void Fallout3()
    {
        var mod = new Fallout3Mod(TestModKey, Fallout3Release.Fallout3);
        mod.Factions.AddNew("StreamFaction");
        var bytes = Write(mod);

        var read = Fallout3Mod.CreateFromBinary(
            new MemoryStream(bytes), TestModKey, Fallout3Release.Fallout3,
            InfoCache(bytes, GameRelease.Fallout3));
        read.Factions.Select(x => x.EditorID).ShouldBe(new[] { "StreamFaction" });
    }

    [Fact]
    public void Oblivion()
    {
        var mod = new OblivionMod(TestModKey, OblivionRelease.Oblivion);
        mod.Factions.AddNew("StreamFaction");
        var bytes = Write(mod);

        var read = OblivionMod.CreateFromBinary(
            new MemoryStream(bytes), TestModKey, OblivionRelease.Oblivion,
            InfoCache(bytes, GameRelease.Oblivion));
        read.Factions.Select(x => x.EditorID).ShouldBe(new[] { "StreamFaction" });
    }
}
