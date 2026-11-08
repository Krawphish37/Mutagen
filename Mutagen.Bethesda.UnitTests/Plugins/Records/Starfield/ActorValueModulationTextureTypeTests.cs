using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Starfield actor value modulation texture types (AVMD MODT) are Rough 0x02, Opacity 0x04, Normal 0x08, AO 0x10,
// Metal 0x20, Biome Conditions 0x40, Emissive 0x80 (xEdit's Starfield definitions). Starfield.esm's EditorIDs match:
// ..._Rough stores 2, ..._Emissive 128, ..._Biome_Conditions 64 (149 records, which Mutagen called Emissive).
public class ActorValueModulationTextureTypeTests
{
    [Theory]
    [InlineData("02-00-00-00", ActorValueModulation.TextureTypeEnum.Rough)]      // SimpleGroup_AVM_Grendel_AcidRain_Rough (2516A7)
    [InlineData("80-00-00-00", ActorValueModulation.TextureTypeEnum.Emissive)]   // SimpleGroup_AVM_Orion_Starborn_Orion_Emissive (0E5772)
    [InlineData("40-00-00-00", ActorValueModulation.TextureTypeEnum.Conditions)] // ComplexGroup_AA_QuadrupedC_TriAntler_Biome_Conditions (1FF9F0)
    public void TextureType_Reads(string modt, ActorValueModulation.TextureTypeEnum expected)
    {
        var bytes = Record("AVMD", Subrecord("MODT", modt));
        foreach (var avmd in ReadBoth<IActorValueModulationGetter>(bytes, f => ActorValueModulation.CreateFromBinary(f), (s, p) => ActorValueModulationBinaryOverlay.ActorValueModulationFactory(s, p)))
        {
            avmd.TextureType.ShouldBe(expected);
        }
    }

    [Theory]
    [InlineData(ActorValueModulation.TextureTypeEnum.Rough, 0x02u)]
    [InlineData(ActorValueModulation.TextureTypeEnum.Emissive, 0x80u)]
    public void TextureType_Writes(ActorValueModulation.TextureTypeEnum type, uint expected)
    {
        var avmd = new ActorValueModulation(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield) { TextureType = type };
        var written = GameConstants.Starfield.MajorRecord(Write(w => avmd.WriteToBinary(w)));
        BitConverter.ToUInt32(written.FindSubrecord(new RecordType("MODT")).Content.Slice(0, 4)).ShouldBe(expected);
    }
}
