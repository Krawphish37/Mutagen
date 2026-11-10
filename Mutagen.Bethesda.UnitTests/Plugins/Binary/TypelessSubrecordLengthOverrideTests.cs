using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary;

/// <summary>
/// A typeless subrecord object (its fields carry the record types) behind a parent overflowRecordType: the parent
/// reads XXXX and passes the real length of the next subrecord as translationParams.LengthOverride, because that
/// subrecord's own header says 0. Both the full import (PluginUtilityTranslation.SubrecordParse) and the overlay
/// (PluginBinaryOverlay.FillTypelessSubrecordTypes) must use that length for the object's first subrecord, and only
/// for the first one.
/// </summary>
public class TypelessSubrecordLengthOverrideTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private const int OverflowLength = 70_000;
    private static readonly RecordType DATA = new("DATA");
    private static readonly RecordType EDID = new("EDID");

    private static ParsingMeta Meta()
    {
        var masters = SeparatedMasterPackage.NotSeparate(new MasterReferenceCollection(TestModKey));
        return new ParsingMeta(GameConstants.Starfield, TestModKey, masters);
    }

    // What the parent hands over after reading XXXX: DATA with a 0 length in its header and 70,000 bytes of content,
    // then a normal EDID.
    private static byte[] Bytes()
    {
        var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes("DATA"));
        stream.Write(BitConverter.GetBytes((ushort)0));
        var payload = new byte[OverflowLength];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 7 + 1);
        stream.Write(payload);
        stream.Write(Encoding.ASCII.GetBytes("EDID"));
        stream.Write(BitConverter.GetBytes((ushort)3));
        stream.Write(Encoding.ASCII.GetBytes("ab\0"));
        return stream.ToArray();
    }

    [Fact]
    public void SubrecordParse_UsesLengthOverrideForFirstSubrecord()
    {
        var bytes = Bytes();
        var frame = new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta()));
        var seen = new List<(RecordType Type, int ContentLength)>();
        PluginUtilityTranslation.SubrecordParse(
            new object(),
            frame,
            TypedParseParams.FromLengthOverride(OverflowLength),
            fillTyped: (record, f, previous, counts, type, contentLength, parseParams) =>
            {
                seen.Add((type, contentLength));
                f.Position += f.MetaData.Constants.SubConstants.HeaderLength + contentLength;
                return new ParseResult(seen.Count, null);
            });
        seen.ShouldBe(new[] { (DATA, OverflowLength), (EDID, 3) });
        frame.Position.ShouldBe(bytes.Length);
    }

    private class OverlayProbe : PluginBinaryOverlay
    {
        public OverlayProbe(BinaryOverlayFactoryPackage package)
            : base(MemoryPair.RecordMemory(ReadOnlyMemorySlice<byte>.Empty), package)
        {
        }
    }

    [Fact]
    public void FillTypelessSubrecordTypes_UsesLengthOverrideForFirstSubrecord()
    {
        var bytes = Bytes();
        var meta = Meta();
        var package = new BinaryOverlayFactoryPackage(meta);
        var stream = new OverlayStream(bytes, package);
        var seen = new List<(RecordType Type, int Length, int? Override)>();
        new OverlayProbe(package).FillTypelessSubrecordTypes(
            stream,
            finalPos: stream.Length,
            offset: 0,
            translationParams: TypedParseParams.FromLengthOverride(OverflowLength),
            fill: (s, finalPos, offset, type, lastParsed, counts, parseParams) =>
            {
                seen.Add((type, finalPos - s.Position - meta.Constants.SubConstants.HeaderLength, lastParsed.LengthOverride));
                s.Position = finalPos;
                return new ParseResult(seen.Count, null);
            });
        seen.ShouldBe(new (RecordType, int, int?)[] { (DATA, OverflowLength, OverflowLength), (EDID, 3, null) });
        stream.Position.ShouldBe(bytes.Length);
    }
}
