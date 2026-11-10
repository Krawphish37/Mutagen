using System.Text;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// C114: the overlay read a counted list of typeless entries (objects of several subrecords without a header of their
// own) by finding where each entry starts from the order of the subrecord types (ParseRecordLocationsByCount). An entry
// holding a repeated subrecord, or a list's items after its counter (CITC, then CTDA, CTDA ...), was split into several
// entries, so the overlay disagreed with the full import (crowd CrowdRegion_NewAtlantis03: 2 entries read as 8).
// The overlay now reads the entries one after another, each as far as its own fields go, like the full import.
public class CountedTypelessEntryOverlayTests
{
    // A CTDA from CrowdRegion_NewAtlantis03 (00D5A8:Starfield.esm)
    private const string Ctda = "00000000000000004A00F944DCEB1400000000000000000000000000FFFFFFFF";
    // Its first PRPS entry
    private const string Prps = "08BC2B000000C84200000000";

    private static byte[] Str(string s) => Encoding.ASCII.GetBytes(s + "\0");

    // Synthetic crowd: entries [PRPS CITC CTDA CTDA STRV FLTV], [CITC CTDA STRV], [STRV FLTV], [STRV]
    private static byte[] Crowd() => Record("GBFM",
        Subrecord("EDID", Str("TestCrowd")),
        Subrecord("BFCB", Str("BGSCrowdComponent_Component")),
        Subrecord("CDND", "CDCCCC3D"),
        Subrecord("CDNS", "04000000"),
        Subrecord("PRPS", Prps),
        Subrecord("CITC", "02000000"),
        Subrecord("CTDA", Ctda),
        Subrecord("CTDA", Ctda),
        Subrecord("STRV", Str("First")),
        Subrecord("FLTV", "0000003F"),
        Subrecord("CITC", "01000000"),
        Subrecord("CTDA", Ctda),
        Subrecord("STRV", Str("Second")),
        Subrecord("STRV", Str("Third")),
        Subrecord("FLTV", "0000803F"),
        Subrecord("STRV", Str("Fourth")),
        Subrecord("BFCE", ""));

    private static IEnumerable<IGenericBaseFormGetter> Read(byte[] bytes) => ReadBoth<IGenericBaseFormGetter>(bytes,
        f => GenericBaseForm.CreateFromBinary(f), (s, p) => GenericBaseFormBinaryOverlay.GenericBaseFormFactory(s, p));

    private static byte[] ComponentBytes(byte[] content)
    {
        var text = Encoding.ASCII.GetString(content);
        var start = text.IndexOf("BFCB", StringComparison.Ordinal);
        var end = text.IndexOf("BFCE", StringComparison.Ordinal) + 6;
        return content[start..end];
    }

    [Fact]
    public void EntriesWithRepeatedSubrecords_ReadAlikeByBothReaders()
    {
        var bytes = Crowd();
        var read = Read(bytes).ToArray();
        foreach (var gbfm in read)
        {
            var items = gbfm.Components.OfType<ICrowdComponentGetter>().Single().Items.ShouldNotBeNull();
            items.Select(i => i.Name).ShouldBe(new[] { "First", "Second", "Third", "Fourth" });
            items.Select(i => i.Conditions?.Count ?? 0).ShouldBe(new[] { 2, 1, 0, 0 });
            items.Select(i => i.Properties?.Count ?? 0).ShouldBe(new[] { 1, 0, 0, 0 });
            items.Select(i => i.PopulationScale).ShouldBe(new float?[] { 0.5f, null, 1f, null });
        }
        read[0].Equals(read[1]).ShouldBeTrue();
        foreach (var gbfm in read)
        {
            var written = GameConstants.Starfield.MajorRecord(Write(w => gbfm.WriteToBinary(w)));
            var original = GameConstants.Starfield.MajorRecord(bytes);
            // GBFM also writes its required subrecords; compare the component, BFCB to BFCE
            ComponentBytes(written.Content.ToArray()).ShouldBe(ComponentBytes(original.Content.ToArray()));
        }
    }
}
