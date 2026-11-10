using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// Display case DCED (xEdit "Exclusivity"): one counted list of exclusive display slots per DCSD template slot, in the
// DCSD order, all in one subrecord (a u32 count, then that many u32 slots, repeated). Mutagen read it as a flat list of
// UInt32 (counts and slots mixed). Starfield.esm: 118 CONT, every DCED parses this way with exactly as many lists as its
// DCSD has slots; 107 are all empty lists, 11 are [1] [0] [].
// Crowd CDNS (xEdit "Populations Count") is now the counter of the crowd's population entries instead of a separate
// UInt32 (24 crowds in Starfield.esm, 46 entries, every count matches).
public class DisplayCaseCrowdCountTests
{
    // Loot_Display_WeaponsCase_Rifles_Common_100YearOld (005D2C:Starfield.esm), localized FULL left out
    private const string RifleCase = "4544494432004C6F6F745F446973706C61795F576561706F6E73436173655F5269666C65735F436F6D6D6F6E5F313030596561724F6C64004F424E44180000201CBF5E3381BE404723BB03201C3F9996903EFEB0593E4F4454590400000000005054543220000000000054771A0000000000458A170000000000000000000000000000000000534E54500400EB631F00424643421C00424753416E696D6174696F6E47726170685F436F6D706F6E656E7400414E414D3100416E696D54657874446174615C5461626C65735C4772617068735C53696D706C654F70656E436C6F736530312E61677800424E414D4C0067656E657269636265686176696F72735C616E696D74656D706C617465735C636F6E7461696E65725F72696730315C6368617261637465726173736574735C736B656C65746F6E2E72696700434E414D3A0067656E657269636265686176696F72735C616E696D74656D706C617465735C636F6E7461696E65725F72696730315C616E696D6174696F6E7300424643450000424643420F00424753446973706C61794361736500444353443000E9631F00000000000000000001000000E6631F00000000000100000001000000A8162500000000000200000001000000444345441400010000000100000001000000000000000000000042464345000046554C4C0400A63703004D4F444C32005365744472657373696E675C576561706F6E7343617365735C576561706F6E73436173655F5269666C657330312E6E696600464C4C44040001000000434F4354040001000000434E544F0800D047000001000000444154410100024B53495A0400040000004B5744411000C19E1A00BB24010093EA0000ACE700004E54524D0400C39E1A00505250530C003D0300000000803F0000000053544F500000574544302800854281EF43D89B8F097F714D3EFC3A87000000000000000000000000000000000000000000000000574544312800F247803D732E487667F48D92E7B7A0A40000000000000000000000000000000000000000000000004F4E414D04008F971900";

    private static byte[] WithoutFull(byte[] content)
    {
        var ret = new MemoryStream();
        for (int p = 0; p + 6 <= content.Length; p += 6 + BitConverter.ToUInt16(content, p + 4))
        {
            if (System.Text.Encoding.ASCII.GetString(content, p, 4) != "FULL") ret.Write(content, p, 6 + BitConverter.ToUInt16(content, p + 4));
        }
        return ret.ToArray();
    }

    [Fact]
    public void DisplayCase_ExclusivityPerSlot()
    {
        foreach (var cont in ReadBoth<IContainerGetter>(Record("CONT", Hex(RifleCase)),
                     f => Container.CreateFromBinary(f), (s, p) => ContainerBinaryOverlay.ContainerFactory(s, p)))
        {
            var display = cont.Components.OfType<IDisplayCaseComponentGetter>().Single();
            display.Items.Count.ShouldBe(3);
            var exclusivity = display.Exclusivity.ShouldNotBeNull();
            exclusivity.Select(e => e.ExclusiveDisplaySlots.ToArray()).ShouldBe(new[]
            {
                new uint[] { 1 },
                new uint[] { 0 },
                Array.Empty<uint>(),
            });
            var written = GameConstants.Starfield.MajorRecord(Write(w => cont.WriteToBinary(w)));
            WithoutFull(written.Content.ToArray()).ShouldBe(WithoutFull(Hex(RifleCase)));
        }
    }

    [Fact]
    public void Crowd_CountWrittenFromEntries()
    {
        var crowd = new CrowdComponent
        {
            Items = new Noggog.ExtendedList<CrowdComponentItem>
            {
                new CrowdComponentItem { Name = "A", PopulationScale = 1f },
                new CrowdComponentItem { Name = "B", PopulationScale = 0.5f },
                new CrowdComponentItem { Name = "C", PopulationScale = 2f },
            },
        };
        var gbfm = new GenericBaseForm(new Mutagen.Bethesda.Plugins.FormKey(TestModKey, 0x800), StarfieldRelease.Starfield)
        {
            EditorID = "TestCrowd",
        };
        gbfm.Components.Add(crowd);
        var written = Write(w => gbfm.WriteToBinary(w));
        var cdns = GameConstants.Starfield.MajorRecord(written).FindSubrecord(new RecordType("CDNS"));
        BitConverter.ToUInt32(cdns.Content).ShouldBe(3u);
        foreach (var reread in ReadBoth<IGenericBaseFormGetter>(written,
                     f => GenericBaseForm.CreateFromBinary(f), (s, p) => GenericBaseFormBinaryOverlay.GenericBaseFormFactory(s, p)))
        {
            reread.Components.OfType<ICrowdComponentGetter>().Single().Items!.Select(i => i.Name).ShouldBe(new[] { "A", "B", "C" });
        }
    }
}
