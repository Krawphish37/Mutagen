using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Extensions;

public class ReadOnlyDictionaryExtTests
{
    [Fact]
    public void BothNull()
    {
        IReadOnlyDictionary<int, string>? lhs = null;
        lhs.DictEqualsNullable(null).ShouldBeTrue();
    }

    [Fact]
    public void OneNull()
    {
        IReadOnlyDictionary<int, string>? lhs = null;
        var rhs = new Dictionary<int, string>();
        lhs.DictEqualsNullable(rhs).ShouldBeFalse();
        rhs.DictEqualsNullable(lhs).ShouldBeFalse();
    }

    [Fact]
    public void BothEmpty()
    {
        new Dictionary<int, string>().DictEqualsNullable(new Dictionary<int, string>()).ShouldBeTrue();
    }

    [Fact]
    public void DifferentInsertionOrder()
    {
        var lhs = new Dictionary<int, string>()
        {
            { 2, "Two" },
            { 0, "Zero" },
        };
        var rhs = new Dictionary<int, string>()
        {
            { 0, "Zero" },
            { 2, "Two" },
        };
        lhs.DictEqualsNullable(rhs).ShouldBeTrue();
        rhs.DictEqualsNullable(lhs).ShouldBeTrue();
    }

    [Fact]
    public void DifferentValue()
    {
        var lhs = new Dictionary<int, string>()
        {
            { 0, "Zero" },
            { 2, "Two" },
        };
        var rhs = new Dictionary<int, string>()
        {
            { 2, "Other" },
            { 0, "Zero" },
        };
        lhs.DictEqualsNullable(rhs).ShouldBeFalse();
    }

    [Fact]
    public void DifferentKeys()
    {
        var lhs = new Dictionary<int, string?>()
        {
            { 0, "Zero" },
            { 2, null },
        };
        var rhs = new Dictionary<int, string?>()
        {
            { 0, "Zero" },
            { 3, null },
        };
        lhs.DictEqualsNullable(rhs).ShouldBeFalse();
        lhs.DictEqualsNullable(new Dictionary<int, string?>() { { 0, "Zero" } }).ShouldBeFalse();
    }
}
