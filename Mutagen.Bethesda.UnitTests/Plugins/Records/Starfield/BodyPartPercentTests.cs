using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Noggog;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// A Starfield body part's Health Percent and To Hit Chance (BPD2) are stored as 0-100 bytes (xEdit's Starfield
// definitions). To Hit Chance is 100 on 195 of 230 parts in Starfield.esm; divided by 255 it read as 0.392.
public class BodyPartPercentTests
{
    [Fact]
    public void HealthAndToHit_ReadAsHundredths()
    {
        // HexapodABodyPartData (BPTD 2CCAAB), first part: health percent 25, to hit chance 100
        var bytes = Record("BPTD",
            Subrecord("BPTN", Encoding.ASCII.GetBytes("Body\0")),
            Subrecord("BPNN", Encoding.ASCII.GetBytes("\0")),
            Subrecord("BPNT", Encoding.ASCII.GetBytes("\0")),
            Subrecord("BPD2", "00-00-80-3F-00-0E-19-8E-03-00-00-64-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-80-3F-00-00"));
        foreach (var data in ReadBoth<IBodyPartDataGetter>(bytes, f => BodyPartData.CreateFromBinary(f), (s, p) => BodyPartDataBinaryOverlay.BodyPartDataFactory(s, p)))
        {
            var part = data.Parts.Single();
            part.HealthPercent.ShouldBe(new Percent(0.25));
            part.ToHitChance.ShouldBe(new Percent(1.0));
        }
    }

    [Fact]
    public void ToHitChance_WritesHundred()
    {
        var data = new BodyPartData(new FormKey(TestModKey, 0x800), StarfieldRelease.Starfield);
        data.Parts.Add(new BodyPart { HealthPercent = new Percent(0.25), ToHitChance = new Percent(1.0) });
        var written = GameConstants.Starfield.MajorRecord(Write(w => data.WriteToBinary(w)));
        var bpd2 = written.FindSubrecord(new RecordType("BPD2")).Content;
        bpd2[6].ShouldBe((byte)25);
        bpd2[11].ShouldBe((byte)100);
    }
}
