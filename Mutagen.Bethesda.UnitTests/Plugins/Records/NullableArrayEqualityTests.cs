using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Fallout3;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing;
using Shouldly;
using Xunit;
using FO3 = Mutagen.Bethesda.Fallout3;
using FO4 = Mutagen.Bethesda.Fallout4;
using SF = Mutagen.Bethesda.Starfield;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

/// <summary>
/// Equality of nullable fixed size arrays of primitives (exposed as ReadOnlyMemorySlice&lt;T&gt;? on getters)
/// </summary>
public class NullableArrayEqualityTests
{
    [Fact]
    public void StarfieldSnapTemplateBnamBothNull()
    {
        var item = new SF.SnapTemplate(TestConstants.Form1, SF.StarfieldRelease.Starfield);
        item.BNAM.ShouldBeNull();
        item.Equals(item.DeepCopy()).ShouldBeTrue();
        item.DeepCopy().Equals(item).ShouldBeTrue();
    }

    [Fact]
    public void StarfieldSnapTemplateBnam()
    {
        var item = new SF.SnapTemplate(TestConstants.Form1, SF.StarfieldRelease.Starfield);
        var other = item.DeepCopy();
        other.BNAM = new float[] { 1, 2, 3, 4, 5, 6 };
        item.Equals(other).ShouldBeFalse();
        other.Equals(item).ShouldBeFalse();

        item.BNAM = new float[] { 1, 2, 3, 4, 5, 6 };
        item.Equals(other).ShouldBeTrue();

        other.BNAM[5] = 7;
        item.Equals(other).ShouldBeFalse();
    }

    [Fact]
    public void StarfieldWeatherNam4BothNull()
    {
        var item = new SF.Weather(TestConstants.Form1, SF.StarfieldRelease.Starfield);
        item.NAM4.ShouldBeNull();
        item.Equals(item.DeepCopy()).ShouldBeTrue();
    }

    [Fact]
    public void StarfieldWeatherNam4()
    {
        var item = new SF.Weather(TestConstants.Form1, SF.StarfieldRelease.Starfield);
        var other = item.DeepCopy();
        other.NAM4 = new float[32];
        item.Equals(other).ShouldBeFalse();
        item.NAM4 = new float[32];
        item.Equals(other).ShouldBeTrue();
        other.NAM4[31] = 1;
        item.Equals(other).ShouldBeFalse();
    }

    [Fact]
    public void Fallout4WeatherNam4BothNull()
    {
        var item = new FO4.Weather(TestConstants.Form1, FO4.Fallout4Release.Fallout4);
        item.NAM4.ShouldBeNull();
        item.Equals(item.DeepCopy()).ShouldBeTrue();
    }

    [Fact]
    public void Fallout4WeatherNam4()
    {
        var item = new FO4.Weather(TestConstants.Form1, FO4.Fallout4Release.Fallout4);
        var other = item.DeepCopy();
        other.NAM4 = new float[32];
        item.Equals(other).ShouldBeFalse();
        item.NAM4 = new float[32];
        item.Equals(other).ShouldBeTrue();
    }

    [Fact]
    public void Fallout3SoundAttenuationCurveBothNull()
    {
        var item = new FO3.Sound(TestConstants.Form1, FO3.Fallout3Release.Fallout3);
        item.AttenuationCurve.ShouldBeNull();
        item.Equals(item.DeepCopy()).ShouldBeTrue();
    }

    [Fact]
    public void Fallout3SoundAttenuationCurve()
    {
        var item = new FO3.Sound(TestConstants.Form1, FO3.Fallout3Release.Fallout3);
        var other = item.DeepCopy();
        other.AttenuationCurve = new short[] { 1, 2, 3, 4, 5 };
        item.Equals(other).ShouldBeFalse();
        item.AttenuationCurve = new short[] { 1, 2, 3, 4, 5 };
        item.Equals(other).ShouldBeTrue();
        other.AttenuationCurve[0] = 9;
        item.Equals(other).ShouldBeFalse();
    }

    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);

    private static ParsingMeta StarfieldMeta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Starfield, TestModKey, masters);
    }

    private static byte[] MakeSnapTemplateBytes(float[]? bnam)
    {
        var content = new MemoryStream();
        var edid = Encoding.ASCII.GetBytes("TestSnapTemplate\0");
        content.Write(Encoding.ASCII.GetBytes("EDID"));
        content.Write(BitConverter.GetBytes((ushort)edid.Length));
        content.Write(edid);
        if (bnam != null)
        {
            content.Write(Encoding.ASCII.GetBytes("BNAM"));
            content.Write(BitConverter.GetBytes((ushort)(bnam.Length * 4)));
            foreach (var f in bnam)
            {
                content.Write(BitConverter.GetBytes(f));
            }
        }
        var body = content.ToArray();

        var record = new MemoryStream();
        record.Write(Encoding.ASCII.GetBytes("STMP"));
        record.Write(BitConverter.GetBytes(body.Length));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes(0x800));
        record.Write(BitConverter.GetBytes(0));
        record.Write(BitConverter.GetBytes((ushort)555));
        record.Write(BitConverter.GetBytes((ushort)0));
        record.Write(body);
        return record.ToArray();
    }

    private static SF.ISnapTemplateGetter ReadDirect(byte[] bytes)
    {
        return SF.SnapTemplate.CreateFromBinary(
            new MutagenFrame(new MutagenMemoryReadStream(bytes, StarfieldMeta())));
    }

    private static SF.ISnapTemplateGetter ReadOverlay(byte[] bytes)
    {
        var meta = StarfieldMeta();
        return SF.SnapTemplateBinaryOverlay.SnapTemplateFactory(
            new OverlayStream(bytes, meta),
            new BinaryOverlayFactoryPackage(meta));
    }

    [Fact]
    public void StarfieldSnapTemplateReadWithoutBnam()
    {
        var bytes = MakeSnapTemplateBytes(null);
        var direct = ReadDirect(bytes);
        var overlay = ReadOverlay(bytes);
        direct.BNAM.ShouldBeNull();
        overlay.BNAM.ShouldBeNull();
        direct.Equals(overlay).ShouldBeTrue();
        overlay.Equals(direct).ShouldBeTrue();
        overlay.Equals(overlay.DeepCopy()).ShouldBeTrue();
        overlay.DeepCopy().Equals(overlay).ShouldBeTrue();
    }

    [Fact]
    public void StarfieldSnapTemplateReadWithBnam()
    {
        var bnam = new float[] { 1, 2, 3, 4, 5, 6 };
        var with = ReadOverlay(MakeSnapTemplateBytes(bnam));
        var without = ReadOverlay(MakeSnapTemplateBytes(null));
        with.Equals(ReadDirect(MakeSnapTemplateBytes(bnam))).ShouldBeTrue();
        with.Equals(with.DeepCopy()).ShouldBeTrue();
        with.Equals(without).ShouldBeFalse();
        without.Equals(with).ShouldBeFalse();
    }
}
