using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Starfield;
using Shouldly;
using Xunit;
using static Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield.StarfieldTestRecords;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Starfield;

// RFGP MNAM is three 4-byte values (xEdit: wbArray(MNAM, 'Unknown', wbUnknown(4), 3), no name). Mutagen read them as a
// UInt32 list; they are floats. All 80,584 MNAM in Starfield.esm are 12 bytes; 77,417 are all zero and the rest
// are float triples (e.g. 0.3516, -0.4876, -0.7516; 1.25 is the most common third value; -0.0 occurs). The field
// stays unnamed (MNAM) but becomes a P3Float, written back bit for bit.
// Each test reads a whole reference group from Starfield.esm with both readers and writes it back byte for byte.
public class ReferenceGroupMnamTests
{
    private static IEnumerable<IReferenceGroupGetter> Read(string hex)
        => ReadBoth<IReferenceGroupGetter>(Record("RFGP", Hex(hex)), f => ReferenceGroup.CreateFromBinary(f), (s, p) => ReferenceGroupBinaryOverlay.ReferenceGroupFactory(s, p));

    private static float F(string hex) => BitConverter.ToSingle(Hex(hex));

    // [no EditorID] (003F81:Starfield.esm)
    private const string Group1 = "524E414D0400D19C1C004C4E414D0400561B15004D4E414D0C00D707B43E08A4F9BEC06740BF";

    // [no EditorID] (007301:Starfield.esm)
    private const string Group2 = "524E414D0400007300004C4E414D0400D01200004D4E414D0C0023F3053F66C56C3E057069BD";

    [Theory]
    [InlineData(Group1, "D707B43E", "08A4F9BE", "C06740BF")]
    [InlineData(Group2, "23F3053F", "66C56C3E", "057069BD")]
    public void Mnam_IsThreeFloats(string hex, string x, string y, string z)
    {
        foreach (var rfgp in Read(hex))
        {
            var mnam = rfgp.MNAM.ShouldNotBeNull();
            mnam.X.ShouldBe(F(x));
            mnam.Y.ShouldBe(F(y));
            mnam.Z.ShouldBe(F(z));
            var written = GameConstants.Starfield.MajorRecord(Write(w => rfgp.WriteToBinary(w)));
            written.Content.ToArray().ShouldBe(Hex(hex));
        }
    }
}
