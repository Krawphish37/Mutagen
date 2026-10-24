using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Condition functions that exist in the Creation Kit's function table but used to be
// parsed as UnknownConditionData, which keeps a form parameter as a raw Int32.
public class ConditionFunctionTests
{
    private static readonly ModKey TestModKey = ModKey.FromNameAndExtension("Test.esp");
    private static readonly ModKey OtherModKey = ModKey.FromNameAndExtension("Other.esm");
    private static readonly ModKey MasterModKey = ModKey.FromNameAndExtension("Master.esm");

    private static MasterReferenceCollection Masters(params ModKey[] masters)
    {
        return new MasterReferenceCollection(
            TestModKey,
            masters.Select(m => new MasterReference() { Master = m }));
    }

    private static ParsingMeta Meta(MasterReferenceCollection masters)
    {
        return new ParsingMeta(GameConstants.Starfield, TestModKey, SeparatedMasterPackage.NotSeparate(masters));
    }

    private static byte[] MakeCtda(ushort function, uint parameterOne, uint parameterTwo = 0)
    {
        var content = new MemoryStream();
        content.WriteByte(0); // Flags / compare operator
        content.Write(new byte[3]);
        content.Write(BitConverter.GetBytes(1f)); // Comparison value
        content.Write(BitConverter.GetBytes(function));
        content.Write(BitConverter.GetBytes((ushort)0)); // Unknown2
        content.Write(BitConverter.GetBytes(parameterOne));
        content.Write(BitConverter.GetBytes(parameterTwo));
        content.Write(BitConverter.GetBytes((int)Condition.RunOnType.Subject));
        content.Write(BitConverter.GetBytes(0)); // Reference
        content.Write(BitConverter.GetBytes(-1)); // Unknown3
        var body = content.ToArray();

        var ret = new MemoryStream();
        ret.Write(Encoding.ASCII.GetBytes("CTDA"));
        ret.Write(BitConverter.GetBytes(checked((ushort)body.Length)));
        ret.Write(body);
        return ret.ToArray();
    }

    private static IConditionGetter ReadDirect(byte[] bytes, ParsingMeta meta)
    {
        return Condition.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, meta)),
            default);
    }

    private static IConditionGetter ReadOverlay(byte[] bytes, ParsingMeta meta)
    {
        return ConditionBinaryOverlay.ConditionFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    private static IEnumerable<IConditionGetter> ReadBoth(byte[] bytes, MasterReferenceCollection masters)
    {
        yield return ReadDirect(bytes, Meta(masters));
        yield return ReadOverlay(bytes, Meta(masters));
    }

    private static byte[] Write(IConditionGetter condition, MasterReferenceCollection masters)
    {
        var bundle = new WritingBundle(GameConstants.Starfield)
        {
            MasterReferences = masters,
            SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(masters),
        };
        var memStream = new MemoryStream();
        using (var writer = new MutagenWriter(memStream, bundle, dispose: false))
        {
            condition.WriteToBinary(writer);
        }
        return memStream.ToArray();
    }

    public static TheoryData<ushort, Type> Functions => new()
    {
        { 961, typeof(GetGameplayOptionCurrentValueConditionData) },
        { 965, typeof(IsInVehicleConditionData) },
        { 966, typeof(AreVehiclesUnlockedConditionData) },
        { 967, typeof(IsLevelGreaterThanGameplayOptionConditionData) },
        { 962, typeof(UnknownConditionData) },
    };

    [Theory]
    [MemberData(nameof(Functions))]
    public void Read_ProducesFunctionSpecificData(ushort function, Type expectedType)
    {
        var bytes = MakeCtda(function, 0);
        foreach (var condition in ReadBoth(bytes, Masters()))
        {
            condition.Data.ShouldBeOfType(expectedType);
            condition.Data.Function.ShouldBe((Condition.Function)function);
        }
    }

    [Theory]
    [InlineData((ushort)961)]
    [InlineData((ushort)967)]
    public void GameplayOptionParameter_IsFormLink(ushort function)
    {
        var expected = new FormKey(MasterModKey, 0x801);
        var bytes = MakeCtda(function, 0x0000_0801);
        foreach (var condition in ReadBoth(bytes, Masters(MasterModKey)))
        {
            var parameters = (IConditionParametersGetter)condition.Data;
            parameters.Parameter1Type.ShouldBe(typeof(IFormLinkOrIndexGetter<IGameplayOptionGetter>));
            var link = parameters.Parameter1.ShouldBeAssignableTo<IFormLinkOrIndexGetter<IGameplayOptionGetter>>()!;
            link.Link.FormKey.ShouldBe(expected);
            condition.EnumerateFormLinks().Select(x => x.FormKey).ShouldContain(expected);
        }
    }

    [Theory]
    [InlineData((ushort)961)]
    [InlineData((ushort)967)]
    public void GameplayOptionParameter_RemappedToWriterMasters(ushort function)
    {
        // Read with Master.esm at index 1, write with Master.esm at index 0
        var bytes = MakeCtda(function, 0x0100_0801);
        foreach (var condition in ReadBoth(bytes, Masters(OtherModKey, MasterModKey)))
        {
            Write(condition, Masters(MasterModKey))
                .ShouldBe(MakeCtda(function, 0x0000_0801));
        }
    }

    [Theory]
    [MemberData(nameof(Functions))]
    public void RoundTrip_KeepsBytes(ushort function, Type expectedType)
    {
        var masters = Masters(MasterModKey);
        var bytes = MakeCtda(function, 0x0000_0801, 0x1234);
        foreach (var condition in ReadBoth(bytes, masters))
        {
            condition.Data.ShouldBeOfType(expectedType);
            Write(condition, masters).ShouldBe(bytes);
        }
    }
}
