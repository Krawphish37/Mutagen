using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Testing;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Order;

public class LoadOrderTests
{
    private static readonly ModKey A = TestConstants.MasterModKey;
    private static readonly ModKey B = TestConstants.MasterModKey2;
    private static readonly ModKey C = TestConstants.MasterModKey3;
    private static readonly ModKey D = TestConstants.MasterModKey4;

    private static ModListing Listing(ModKey modKey) => new ModListing(modKey, enabled: true, modExists: true);

    private static LoadOrder<ModListing> Make(params ModKey[] modKeys) =>
        new LoadOrder<ModListing>(modKeys.Select(Listing));

    private static void ShouldBeConsistent(LoadOrder<ModListing> lo, params ModKey[] expected)
    {
        lo.ListedOrder.Select(x => x.ModKey).ShouldBe(expected);
        lo.Count.ShouldBe(expected.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            lo.IndexOf(expected[i]).ShouldBe(i);
            lo[i].ModKey.ShouldBe(expected[i]);
            lo.TryGetAtIndex(i)!.ModKey.ShouldBe(expected[i]);
            lo.ContainsKey(expected[i]).ShouldBeTrue();
        }
    }

    [Fact]
    public void AddAtIndex_Middle()
    {
        var lo = Make(A, B, C);
        lo.Add(Listing(D), 1);
        ShouldBeConsistent(lo, A, D, B, C);
    }

    [Fact]
    public void AddAtIndex_Front()
    {
        var lo = Make(A, B, C);
        lo.Add(Listing(D), 0);
        ShouldBeConsistent(lo, D, A, B, C);
    }

    [Fact]
    public void AddAtIndex_End()
    {
        var lo = Make(A, B, C);
        lo.Add(Listing(D), 3);
        ShouldBeConsistent(lo, A, B, C, D);
    }

    [Fact]
    public void AddAtIndex_Empty()
    {
        var lo = Make();
        lo.Add(Listing(A), 0);
        ShouldBeConsistent(lo, A);
    }

    [Fact]
    public void AddAtIndex_OutOfRange()
    {
        var lo = Make(A, B);
        Assert.Throws<ArgumentException>(() => lo.Add(Listing(D), 3));
        Assert.Throws<ArgumentException>(() => lo.Add(Listing(D), -1));
        ShouldBeConsistent(lo, A, B);
    }

    [Fact]
    public void AddAtIndex_Duplicate()
    {
        var lo = Make(A, B);
        Assert.Throws<ArgumentException>(() => lo.Add(Listing(B), 0));
        ShouldBeConsistent(lo, A, B);
    }

    [Fact]
    public void AddAtIndex_ThenRemoveKey()
    {
        var lo = Make(A, B, C);
        lo.Add(Listing(D), 1);
        lo.RemoveKey(D).ShouldBeTrue();
        ShouldBeConsistent(lo, A, B, C);
        lo.ContainsKey(D).ShouldBeFalse();
    }

    [Fact]
    public void AddAtIndex_ThenRemoveAt()
    {
        var lo = Make(A, B, C);
        lo.Add(Listing(D), 1);
        lo.RemoveAt(2);
        ShouldBeConsistent(lo, A, D, C);
        lo.ContainsKey(B).ShouldBeFalse();
    }
}
