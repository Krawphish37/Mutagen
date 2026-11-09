using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Photo mode feature FNAM (29 bytes) per xEdit: Type, X/Y offset, Texture Width, Texture Height, Content Width,
// Content Height, Flags (Tile X, Tile Y, Top Most). Mutagen read Texture Width + Height as one Int64 (UnknownFNAM1),
// called the content size Width/Height and kept Flags as UnknownFNAM2. Same bytes.
public class PhotoModeFeatureDataTests
{
    // PhotoModeFrame_LogoMed (100A2A:Starfield.esm): Frame, offset (-0.7, -0.6), content 1024 x 1024
    private const string LogoMed = "45444944170050686F746F4D6F64654672616D655F4C6F676F4D65640046554C4C04002F3D0300464E414D1D0001333333BF9A9919BF0000000000000000000400000004000000000000484E414D4400446174615C54657874757265735C696E746572666163655C50686F746F6D6F64655C4672616D655C50686F746F6D6F64655F4C6F676F4D65645F636F6C6F722E64647300";

    private static IEnumerable<IPhotoModeFeatureGetter> Read(string hex)
        => ReadBoth<IPhotoModeFeatureGetter>(Record("PMFT", Hex(hex)), f => PhotoModeFeature.CreateFromBinary(f), (s, p) => PhotoModeFeatureBinaryOverlay.PhotoModeFeatureFactory(s, p));

    [Fact]
    public void LogoMed_Reads()
    {
        foreach (var pmft in Read(LogoMed))
        {
            pmft.FeatureType.ShouldBe(PhotoModeFeature.Mode.Frame);
            pmft.XOffset.ShouldBe(-0.7f);
            pmft.YOffset.ShouldBe(-0.6f);
            pmft.TextureWidth.ShouldBe(0u);
            pmft.TextureHeight.ShouldBe(0u);
            pmft.ContentWidth.ShouldBe(1024u);
            pmft.ContentHeight.ShouldBe(1024u);
            pmft.Flags.ShouldBe((PhotoModeFeature.Flag)0);
        }
    }

    [Fact]
    public void LogoMed_WritesSameBytes()
    {
        foreach (var pmft in Read(LogoMed))
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => pmft.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(LogoMed));
        }
    }

    [Fact]
    public void Flags_Values()
    {
        ((uint)PhotoModeFeature.Flag.TileX).ShouldBe(0x1u);
        ((uint)PhotoModeFeature.Flag.TileY).ShouldBe(0x2u);
        ((uint)PhotoModeFeature.Flag.TopMost).ShouldBe(0x4u);
    }
}
