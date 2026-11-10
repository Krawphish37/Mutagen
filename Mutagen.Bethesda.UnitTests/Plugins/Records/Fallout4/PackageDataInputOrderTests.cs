using System.Buffers.Binary;
using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Fallout4;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Fallout4;

// Package data inputs (ANAM + value, then one UNAM index each) are read into a dictionary in the order the record holds
// them, but the writer sorted them by index, so a record holding them in another order (Starfield.esm does, e.g.
// 0E 0A 03 07 ...; C97) came back with its subrecords in another order. The writer now keeps the dictionary's order.
// The record here is Mutagen's own output with its two data inputs swapped (index 2 before index 0).
public class PackageDataInputOrderTests
{
    private static readonly ModKey TestModKey = new("Test", ModType.Plugin);
    private static readonly MasterReferenceCollection Masters = new(TestModKey);

    private static ParsingMeta Meta() => new(GameConstants.Fallout4, TestModKey, SeparatedMasterPackage.NotSeparate(Masters));

    private static byte[] Write(IPackageGetter item)
    {
        var stream = new MemoryStream();
        using (var writer = new MutagenWriter(stream, new WritingBundle(GameConstants.Fallout4)
               {
                   MasterReferences = Masters,
                   SeparatedMasterPackage = SeparatedMasterPackage.NotSeparate(Masters),
               }))
        {
            item.WriteToBinary(writer);
        }
        return stream.ToArray();
    }

    private static IEnumerable<IPackageGetter> ReadBoth(byte[] bytes)
    {
        yield return Package.CreateFromBinary(new MutagenFrame(new MutagenMemoryReadStream(bytes, Meta())));
        var meta = Meta();
        yield return PackageBinaryOverlay.PackageFactory(new OverlayStream(bytes, meta), new BinaryOverlayFactoryPackage(meta));
    }

    private static List<(string Type, byte[] Bytes)> Subrecords(byte[] record, int headerLength)
    {
        var ret = new List<(string, byte[])>();
        for (int p = headerLength; p + 6 <= record.Length; p += 6 + BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(p + 4)))
        {
            ret.Add((Encoding.ASCII.GetString(record, p, 4), record[p..(p + 6 + BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(p + 4)))]));
        }
        return ret;
    }

    // Package with data inputs 0 (Int 5) and 2 (Bool true) as Mutagen writes it, then the same with 2 first
    private static byte[] SwappedRecord()
    {
        var package = new Package(new FormKey(TestModKey, 0x800), Fallout4Release.Fallout4);
        package.Data[0] = new PackageDataInt { Data = 5 };
        package.Data[2] = new PackageDataBool { Data = true };
        var written = Write(package);
        var headerLength = GameConstants.Fallout4.MajorConstants.HeaderLength;
        var subs = Subrecords(written, headerLength);
        var first = subs.FindIndex(s => s.Type == "ANAM");
        // ANAM Int, CNAM, ANAM Bool, CNAM, UNAM 00, UNAM 02
        subs.Select(s => s.Type).Skip(first).Take(6).ShouldBe(new[] { "ANAM", "CNAM", "ANAM", "CNAM", "UNAM", "UNAM" });
        var swapped = subs.Take(first)
            .Append(subs[first + 2]).Append(subs[first + 3]).Append(subs[first]).Append(subs[first + 1])
            .Append(subs[first + 5]).Append(subs[first + 4])
            .Concat(subs.Skip(first + 6));
        return written[..headerLength].Concat(swapped.SelectMany(s => s.Bytes)).ToArray();
    }

    [Fact]
    public void DataInputsInFileOrder_WrittenBackByteForByte()
    {
        var bytes = SwappedRecord();
        foreach (var package in ReadBoth(bytes))
        {
            package.Data.Keys.ShouldBe(new sbyte[] { 2, 0 });
            Write(package).ShouldBe(bytes);
        }
    }
}
