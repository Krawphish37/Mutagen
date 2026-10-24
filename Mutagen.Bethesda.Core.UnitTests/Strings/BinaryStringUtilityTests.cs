using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Strings.DI;
using Mutagen.Bethesda.Testing;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Strings;

public class BinaryStringUtilityTests
{
    [Fact]
    public void ToZStringFrenchSeExample()
    {
        var ret = new MutagenEncodingProvider().GetEncoding(GameRelease.SkyrimSE, Language.French)
            .GetString(File.ReadAllBytes(TestDataPathing.FrenchSeString));
        ret.ShouldBe("Livre de sort - Paralysie générale");
    }

    [Fact]
    public void ToZStringRussianLeExample()
    {
        var ret = new MutagenEncodingProvider().GetEncoding(GameRelease.SkyrimLE, Language.Russian)
            .GetString(File.ReadAllBytes(TestDataPathing.RussianLeString));
        ret.ShouldBe("Распорядок Гая Марона");
    }

    [Fact]
    public void ParsePrependedStringTypical()
    {
        BinaryStringUtility.ParsePrependedString(File.ReadAllBytes(TestDataPathing.PrependedString), 2, MutagenEncoding._1252)
            .ShouldBe("HelloWorld");
    }

    [Fact]
    public void ParsePrependedStringZeroLength()
    {
        BinaryStringUtility.ParsePrependedString(File.ReadAllBytes(TestDataPathing.ZeroContentPrependedString), 2, MutagenEncoding._1252)
            .ShouldBe("");
    }

    [Fact]
    public void ProcessNullTerminationOnEmpty()
    {
        var span = BinaryStringUtility.ProcessNullTermination(Array.Empty<byte>().AsSpan());
        span.Length.ShouldBe(0);
    }

    [Fact]
    public void ProcessNullTerminationOnExtraFluff()
    {
        var span = BinaryStringUtility.ProcessNullTermination(File.ReadAllBytes(TestDataPathing.FluffedNullString));
        span.Length.ShouldBe(0xB);
        var str = BinaryStringUtility.ToZString(span, MutagenEncoding._utf8);
        str.ShouldBe("Hello world");
    }

    // --- WriteNullPaddedFixedLength ---------------------------------------

    private static byte[] WriteFixed(string str, int byteLength)
    {
        var ms = new MemoryStream();
        using (var writer = new MutagenWriter(ms, GameConstants.Oblivion, dispose: false))
        {
            BinaryStringUtility.WriteNullPaddedFixedLength(writer, str.AsSpan(), byteLength, MutagenEncoding._1252);
        }
        return ms.ToArray();
    }

    [Fact]
    public void WriteNullPaddedFixedLength_ShortStringIsZeroPadded()
    {
        var bytes = WriteFixed("Wood", byteLength: 30);
        bytes.Length.ShouldBe(30);
        bytes.AsSpan(0, 4).ToArray().ShouldBe(new byte[] { (byte)'W', (byte)'o', (byte)'o', (byte)'d' });
        // Tail must be all zeros
        for (int i = 4; i < 30; i++) bytes[i].ShouldBe((byte)0);
    }

    [Fact]
    public void WriteNullPaddedFixedLength_EmptyStringIsAllZeros()
    {
        var bytes = WriteFixed(string.Empty, byteLength: 30);
        bytes.Length.ShouldBe(30);
        for (int i = 0; i < 30; i++) bytes[i].ShouldBe((byte)0);
    }

    [Fact]
    public void WriteNullPaddedFixedLength_ExactFitNoPadding()
    {
        var s = new string('A', 30); // fills the slot exactly, no room for terminator
        var bytes = WriteFixed(s, byteLength: 30);
        bytes.Length.ShouldBe(30);
        for (int i = 0; i < 30; i++) bytes[i].ShouldBe((byte)'A');
    }

    [Fact]
    public void WriteNullPaddedFixedLength_OversizedThrows()
    {
        Should.Throw<ArgumentException>(() => WriteFixed(new string('A', 31), byteLength: 30));
    }

    // --- Write (large strings) --------------------------------------------

    private static byte[] WriteString(string str, StringBinaryType binaryType, IMutagenEncoding encoding)
    {
        var ms = new MemoryStream();
        using (var writer = new MutagenWriter(ms, GameConstants.Oblivion, dispose: false))
        {
            writer.Write(str.AsSpan(), binaryType, encoding);
        }
        return ms.ToArray();
    }

    private static void RunOnThreadWithStack(Action action, int maxStackSize)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                ex = e;
            }
        }, maxStackSize);
        thread.Start();
        thread.Join();
        if (ex != null) throw new AggregateException(ex);
    }

    [Fact]
    public void Write_StringLargerThanStack_DoesNotOverflow()
    {
        // Encoded length is 4x the thread's stack, so it must not be stackalloc'd
        const int Length = 4 * 1024 * 1024;
        byte[] bytes = null!;
        RunOnThreadWithStack(
            () => bytes = WriteString(new string('a', Length), StringBinaryType.Plain, MutagenEncoding._1252),
            maxStackSize: 1024 * 1024);
        bytes.Length.ShouldBe(Length);
        bytes.AsSpan().IndexOfAnyExcept((byte)'a').ShouldBe(-1);
    }

    [Fact]
    public void Write_LargePrependLengthWithNullIfContent_TerminatorIsZero()
    {
        const int Length = 5000;
        // Leave a dirty buffer in the shared pool, so a rental that is not cleared would show up
        var dirty = System.Buffers.ArrayPool<byte>.Shared.Rent(Length + 1);
        dirty.AsSpan().Fill(0xFF);
        System.Buffers.ArrayPool<byte>.Shared.Return(dirty);

        var bytes = WriteString(new string('b', Length), StringBinaryType.PrependLengthWithNullIfContent, MutagenEncoding._1252);
        bytes.Length.ShouldBe(4 + Length + 1);
        BitConverter.ToInt32(bytes, 0).ShouldBe(Length + 1);
        bytes.AsSpan(4, Length).IndexOfAnyExcept((byte)'b').ShouldBe(-1);
        bytes[^1].ShouldBe((byte)0);
    }

    [Theory]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(1026)]
    [InlineData(100_000)]
    public void Write_AroundStackAllocLimit_MatchesEncoding(int byteCount)
    {
        // Mix of 1 and 2 byte UTF8 characters, padded to exactly the requested encoded length
        var sb = new System.Text.StringBuilder();
        var count = 0;
        while (count + 3 <= byteCount)
        {
            sb.Append("aé");
            count += 3;
        }
        sb.Append('a', byteCount - count);
        var str = sb.ToString();
        var expected = System.Text.Encoding.UTF8.GetBytes(str);
        expected.Length.ShouldBe(byteCount);

        var bytes = WriteString(str, StringBinaryType.NullTerminate, MutagenEncoding._utf8);
        bytes.Length.ShouldBe(byteCount + 1);
        bytes.AsSpan(0, byteCount).SequenceEqual(expected).ShouldBeTrue();
        bytes[^1].ShouldBe((byte)0);
        BinaryStringUtility.ToZString(BinaryStringUtility.ProcessNullTermination(bytes), MutagenEncoding._utf8)
            .ShouldBe(str);
    }

    // --- ParseNullPaddedFixedLength + StringBinaryTranslation ------------

    private static IMutagenReadStream MakeReadStream(byte[] bytes) =>
        new MutagenBinaryReadStream(
            new MemoryStream(bytes, writable: false),
            new ParsingMeta(GameConstants.Oblivion, ModKey.Null, masterReferences: null!),
            dispose: true);

    [Fact]
    public void ParseNullPaddedFixedLength_TrimsTrailingZeros()
    {
        var slot = new byte[30];
        slot[0] = (byte)'W'; slot[1] = (byte)'o'; slot[2] = (byte)'o'; slot[3] = (byte)'d';
        using var reader = MakeReadStream(slot);
        var result = StringBinaryTranslation.Instance.ParseNullPaddedFixedLength(reader, byteLength: 30);
        result.ShouldBe("Wood");
        reader.Position.ShouldBe(30); // exactly byteLength consumed
    }

    [Fact]
    public void ParseNullPaddedFixedLength_AllZerosYieldsEmpty()
    {
        using var reader = MakeReadStream(new byte[30]);
        var result = StringBinaryTranslation.Instance.ParseNullPaddedFixedLength(reader, byteLength: 30);
        result.ShouldBe(string.Empty);
        reader.Position.ShouldBe(30);
    }

    [Fact]
    public void ParseNullPaddedFixedLength_ExactFitReturnsWholeSlot()
    {
        var slot = new byte[30];
        for (int i = 0; i < 30; i++) slot[i] = (byte)'A';
        using var reader = MakeReadStream(slot);
        var result = StringBinaryTranslation.Instance.ParseNullPaddedFixedLength(reader, byteLength: 30);
        result.ShouldBe(new string('A', 30));
    }

    [Fact]
    public void ParseNullPaddedFixedLength_AdvancesByteLengthEvenIfStringShorter()
    {
        // Two slots back-to-back; the parser must consume exactly byteLength from the first
        // so the second is read correctly.
        var buf = new byte[60];
        buf[0] = (byte)'A';
        buf[30] = (byte)'B';
        using var reader = MakeReadStream(buf);
        StringBinaryTranslation.Instance.ParseNullPaddedFixedLength(reader, byteLength: 30).ShouldBe("A");
        StringBinaryTranslation.Instance.ParseNullPaddedFixedLength(reader, byteLength: 30).ShouldBe("B");
        reader.Position.ShouldBe(60);
    }

    [Theory]
    [InlineData("", 30)]
    [InlineData("Wood", 30)]
    [InlineData("ConcSolid", 30)]
    [InlineData("MetalHollow", 30)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", 30)] // exact fit
    public void RoundTrip_WriteThenParse(string input, int byteLength)
    {
        var bytes = WriteFixed(input, byteLength);
        bytes.Length.ShouldBe(byteLength);
        using var reader = MakeReadStream(bytes);
        var result = StringBinaryTranslation.Instance.ParseNullPaddedFixedLength(reader, byteLength);
        result.ShouldBe(input);
    }

    [Fact]
    public void RoundTrip_TenSlotImpfShape()
    {
        // Mirrors the IMPF use case: 10 named slots × 30 bytes each.
        var labels = new[]
        {
            "ConcSolid", "ConcBroken", "MetalSolid", "MetalHollow", "MetalSheet",
            "Wood", "Sand", "Dirt", "Grass", "Water"
        };
        var ms = new MemoryStream();
        using (var writer = new MutagenWriter(ms, GameConstants.Oblivion, dispose: false))
        {
            foreach (var label in labels)
            {
                BinaryStringUtility.WriteNullPaddedFixedLength(writer, label.AsSpan(), 30, MutagenEncoding._1252);
            }
        }
        ms.Length.ShouldBe(300);

        using var reader = MakeReadStream(ms.ToArray());
        foreach (var label in labels)
        {
            StringBinaryTranslation.Instance.ParseNullPaddedFixedLength(reader, byteLength: 30).ShouldBe(label);
        }
        reader.Position.ShouldBe(300);
    }
}