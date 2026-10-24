using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.UnitTests.Placeholders;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Binary.Translations;

public class ListBinaryTranslationParallelTests
{
    private static readonly RecordType ItemType = new("TEST");
    private const int ItemCount = 64;
    private const ushort ParentFormVersion = 7;

    private static ParsingMeta GetMeta() => new(
        GameConstants.SkyrimSE,
        ModKey.FromNameAndExtension("Test.esp"),
        SeparatedMasterPackage.EmptyNull);

    /// <summary>
    /// Major records of type TEST, each with 4 bytes of content and its own index as form version
    /// </summary>
    private static byte[] GetItems()
    {
        var headerLen = GameConstants.SkyrimSE.MajorConstants.HeaderLength;
        var versionOffset = GameConstants.SkyrimSE.MajorConstants.FormVersionLocationOffset!.Value;
        var itemLen = headerLen + 4;
        var bytes = new byte[ItemCount * itemLen];
        for (int i = 0; i < ItemCount; i++)
        {
            var span = bytes.AsSpan(i * itemLen, itemLen);
            BinaryPrimitives.WriteInt32LittleEndian(span, ItemType.TypeInt);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), 4);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(versionOffset), (short)(100 + i));
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(headerLen), i);
        }
        return bytes;
    }

    [Fact]
    public void ParallelItemsDoNotShareParsingMeta()
    {
        var meta = GetMeta();
        meta.FormVersion = ParentFormVersion;
        meta.InWorldspace = true;
        var frame = new MutagenFrame(new MutagenMemoryReadStream(GetItems(), meta));

        var seenMetas = new ConcurrentBag<ParsingMeta>();
        var seen = new ConcurrentDictionary<int, (ushort? StartVersion, bool StartInWorldspace, ushort? HeaderVersion, ushort? ReadBack)>();
        ListBinaryTranslation<TestMajorRecord>.Instance.ParseParallel(
            frame,
            ItemType,
            (MutagenFrame r, [MaybeNullWhen(false)] out TestMajorRecord item, TypedParseParams _) =>
            {
                seenMetas.Add(r.MetaData);
                var startVersion = r.MetaData.FormVersion;
                var startInWorldspace = r.MetaData.InWorldspace;
                // What MajorRecordParse does around every record
                var headerVersion = (ushort?)r.Reader.GetMajorRecordHeader().FormVersion;
                r.MetaData.FormVersion = headerVersion;
                r.Position += r.MetaData.Constants.MajorConstants.HeaderLength;
                var index = r.ReadInt32();
                Thread.Yield();
                var readBack = r.MetaData.FormVersion;
                r.MetaData.FormVersion = null;
                r.MetaData.InWorldspace = false;
                seen[index] = (startVersion, startInWorldspace, headerVersion, readBack);
                item = new TestMajorRecord(FormKey.Null);
                return true;
            }).Count().ShouldBe(ItemCount);

        seen.Count.ShouldBe(ItemCount);
        seenMetas.ShouldNotContain(meta);
        seenMetas.Distinct(ReferenceEqualityComparer.Instance).Count().ShouldBe(ItemCount);
        foreach (var (index, s) in seen)
        {
            // Each item starts from the state of the meta it was spawned from, not what a sibling left behind
            s.StartVersion.ShouldBe(ParentFormVersion);
            s.StartInWorldspace.ShouldBeTrue();
            // and reads back its own version, not a sibling's
            s.HeaderVersion.ShouldBe((ushort)(100 + index));
            s.ReadBack.ShouldBe(s.HeaderVersion);
        }
        // Items setting and clearing their trackers leaves the parent's untouched
        meta.FormVersion.ShouldBe(ParentFormVersion);
        meta.InWorldspace.ShouldBeTrue();
    }

    [Fact]
    public void CloneForParallelParseSharesServicesButNotTrackers()
    {
        var meta = GetMeta();
        meta.FormVersion = ParentFormVersion;
        meta.Parallel = true;
        var clone = meta.CloneForParallelParse();
        clone.ShouldNotBeSameAs(meta);
        clone.Constants.ShouldBeSameAs(meta.Constants);
        clone.MasterReferences.ShouldBeSameAs(meta.MasterReferences);
        clone.ModKey.ShouldBe(meta.ModKey);
        clone.Encodings.ShouldBe(meta.Encodings);
        clone.Parallel.ShouldBeTrue();
        clone.FormVersion.ShouldBe(ParentFormVersion);
        clone.FormVersion = null;
        meta.FormVersion.ShouldBe(ParentFormVersion);
    }
}
