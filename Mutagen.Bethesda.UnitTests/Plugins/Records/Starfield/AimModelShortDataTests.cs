using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// AMDL ANAM is 65 bytes in Starfield.esm (110 aim models) and every other plugin of the Data folder, but older aim
// models (form version 575) stop before the last field, Cone of Fire Ignores Movement: 64 bytes. xEdit marks the
// struct SetOptionalFrom(16). Mutagen wrote such a record back 1 byte longer (a 0 added). Only weap_starlight.esm has
// them: starlight_Lawgiver_SA_AM, starlight_M1919_SA_AM and starlight_Novalight_SA_AM (000847-000849:weap_starlight.esm).
public class AimModelShortDataTests
{
    // starlight_Lawgiver_SA_AM (000847:weap_starlight.esm), read only from the Data folder: EDID and a 64-byte ANAM
    private const string Short = "454449441900737461726C696768745F4C617767697665725F53415F414D00414E414D40000000A03F0000B8409A99593F9A99F93F3333333FCDCC4C3F000016430000803F00000040000080403333B33F020000000000A041000020403333F33ECDCC2C3F";

    // MagShot_AM (01E7D3:Starfield.esm): EDID and a 65-byte ANAM
    private const string Full = "454449440B004D616753686F745F414D00414E414D410000004040000000410000803F000000409A99193ECDCC4C3F00007A430000803F3333B33ECDCC0C3F0000A03F0200000000000000000000000000003F9A99593F00";

    private static IEnumerable<IAimModelGetter> Read(string hex)
        => ReadBoth<IAimModelGetter>(Record("AMDL", Hex(hex)), f => AimModel.CreateFromBinary(f), (s, p) => AimModelBinaryOverlay.AimModelFactory(s, p));

    [Fact]
    public void ShortData_WritesBackShort()
    {
        foreach (var amdl in Read(Short))
        {
            amdl.EditorID.ShouldBe("starlight_Lawgiver_SA_AM");
            amdl.ConeOfFireDegreesX.ShouldBe(1.25f);
            amdl.RecoilShotsForRunaway.ShouldBe(2u);
            amdl.ConeOfFireIgnoresMovement.ShouldBeFalse();
            var written = GameConstants.Starfield.MajorRecord(Write(w => amdl.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(Short));
        }
    }

    [Fact]
    public void FullData_WritesBackFull()
    {
        foreach (var amdl in Read(Full))
        {
            amdl.EditorID.ShouldBe("MagShot_AM");
            var written = GameConstants.Starfield.MajorRecord(Write(w => amdl.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(Full));
        }
    }
}
