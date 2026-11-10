using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Sound marker and sound keyword mapping fields get xEdit's names and types (wbDefinitionsSF1 SOUN, KSSM):
// - SOUN SMLS is the Sound; DEVT is the sound Level (SoundLevel enum, values 0..5 in 649 SOUN) and the float Radius.
// - KSSM WED0 is the Sound. The first RSMC is the Count of the Reverb Sounds that follow, each an RSMC Class
//   (Default, Class A..E) and an RSMH sound. Mutagen read the count as an extra item with no sound, so the list had
//   one item too many and adding an item did not update the count (500 KSSM: 500 counts + 830 entries).
// Each test reads a whole record from Starfield.esm with both readers and writes it back byte for byte.
public class SoundRecordFieldTests
{
    private static void WritesBack(Action<MutagenWriter> write, string hex)
    {
        var written = GameConstants.Starfield.MajorRecord(Write(write));
        written.Content.ToArray().ShouldBe(Hex(hex));
    }

    // AMBIntShipLargeMarkerPipeLiquidGas (00142E:Starfield.esm)
    private const string PipeLiquidGas = "454449442300414D42496E74536869704C617267654D61726B6572506970654C6971756964476173004F424E44180000C06AC10080EBBD0080EBBD00C06A410080EB3D0080EB3D4F445459040000000000534D4C532800724FFB42649517FB3BA68480DE5A18867B4FDA2C84AB23ABFE0B07AD4A08118E00000000000000004445565408000200000000001041";
    // SKMHandRifleMagShearPCAutoLoopHighVelocity (00AC15:Starfield.esm)
    private const string MagShear = "454449442B00534B4D48616E645269666C654D6167536865617250434175746F4C6F6F704869676856656C6F6369747900574544302800CD4E8195A1D84497003649CE286E7485DE415863DA5947E0B0DB0E03E80FFFBC00000000000000004B4E414D0400483F29004B4E414D0400FDD523004B4E414D04004717050052534D4304000600000052534D4304000500000052534D482800E24D8CAB0681C91EC653294A9DDBCA9B0340151450B46DA861895409F921EB9F000000000000000052534D4304000400000052534D4828008441E46F8EB921ABB582BE5BC3021582D448A78B0FC2B6ABA75AA9DA36D3808E000000000000000052534D4304000100000052534D4828004948EC46AE6824C75C2925B73AD87A8F0D422BEEB86FBCF6EDF1ECC605FD3494000000000000000052534D4304000000000052534D482800004FDB823D0B97B7E6AB04617A8DCCB660438F1FDC35BCC74580FE5FCB09D9B6000000000000000052534D4304000300000052534D482800C44C56CDABAB4D82EA215790E79962819F437ADC68182980F2B0685BE95738B4000000000000000052534D4304000200000052534D482800AC45A63D22E901B2812F3733D2CBAF8ED84771B132B8A373CEF3B54E1D8C83850000000000000000";

    [Fact]
    public void SoundMarker_LevelAndRadius()
    {
        foreach (var soun in ReadBoth<ISoundMarkerGetter>(Record("SOUN", Hex(PipeLiquidGas)), f => SoundMarker.CreateFromBinary(f), (s, p) => SoundMarkerBinaryOverlay.SoundMarkerFactory(s, p)))
        {
            soun.Sound.ShouldNotBeNull();
            soun.Level.ShouldBe(SoundLevel.Quiet);
            soun.Radius.ShouldBe(9f);
            WritesBack(w => soun.WriteToBinary(w), PipeLiquidGas);
        }
    }

    [Fact]
    public void SoundKeywordMapping_ReverbSoundsCountedByRsmc()
    {
        foreach (var kssm in ReadBoth<ISoundKeywordMappingGetter>(Record("KSSM", Hex(MagShear)), f => SoundKeywordMapping.CreateFromBinary(f), (s, p) => SoundKeywordMappingBinaryOverlay.SoundKeywordMappingFactory(s, p)))
        {
            kssm.Sound.ShouldNotBeNull();
            kssm.Keywords.ShouldNotBeNull().Count.ShouldBe(3);
            kssm.ReverbSounds.Select(r => r.Class!.Value).ShouldBe(new[]
            {
                SoundKeywordMapping.ReverbClass.ClassE,
                SoundKeywordMapping.ReverbClass.ClassD,
                SoundKeywordMapping.ReverbClass.ClassA,
                SoundKeywordMapping.ReverbClass.Default,
                SoundKeywordMapping.ReverbClass.ClassC,
                SoundKeywordMapping.ReverbClass.ClassB,
            });
            kssm.ReverbSounds.ShouldAllBe(r => r.Sound != null);
            WritesBack(w => kssm.WriteToBinary(w), MagShear);
        }
    }

    [Fact]
    public void SoundKeywordMapping_AddedItemUpdatesCount()
    {
        var kssm = SoundKeywordMapping.CreateFromBinary(new Mutagen.Bethesda.Plugins.Binary.Streams.MutagenFrame(
            new MutagenMemoryReadStream(Record("KSSM", Hex(MagShear)), Meta())));
        kssm.ReverbSounds.Add(new SoundKeywordMappingItem() { Class = SoundKeywordMapping.ReverbClass.ClassA, Sound = new SoundReference() });
        var written = GameConstants.Starfield.MajorRecord(Write(w => ((ISoundKeywordMappingGetter)kssm).WriteToBinary(w)));
        var counts = written.Where(p => p.RecordType == new Mutagen.Bethesda.Plugins.RecordType("RSMC")).Select(p => p.Content.ToArray()).ToArray();
        BitConverter.ToUInt32(counts[0]).ShouldBe(7u);
        counts.Length.ShouldBe(8);
    }
}
