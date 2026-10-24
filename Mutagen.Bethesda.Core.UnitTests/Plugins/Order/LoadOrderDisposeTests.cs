using Shouldly;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Testing;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Order;

public class LoadOrderDisposeTests
{
    private static readonly ModKey A = TestConstants.MasterModKey;
    private static readonly ModKey B = TestConstants.MasterModKey2;
    private static readonly ModKey C = TestConstants.MasterModKey3;

    private class DisposableItem : IModKeyed, IDisposable
    {
        public ModKey ModKey { get; }
        public bool Disposed { get; private set; }

        public DisposableItem(ModKey modKey)
        {
            ModKey = modKey;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private static (LoadOrder<DisposableItem> LoadOrder, DisposableItem[] Items) Make(bool disposeItems)
    {
        var items = new[] { new DisposableItem(A), new DisposableItem(B), new DisposableItem(C) };
        return (new LoadOrder<DisposableItem>(items, disposeItems: disposeItems), items);
    }

    [Fact]
    public void RemoveKeyDisposesRemovedItem()
    {
        var (lo, items) = Make(disposeItems: true);
        lo.RemoveKey(B).ShouldBeTrue();
        items[1].Disposed.ShouldBeTrue();
        items[0].Disposed.ShouldBeFalse();
        items[2].Disposed.ShouldBeFalse();
        lo.ListedOrder.Select(x => x.ModKey).ShouldBe(new[] { A, C });
        lo.IndexOf(C).ShouldBe(1);
    }

    [Fact]
    public void RemoveKeyDoesNotDisposeWhenNotDisposingItems()
    {
        var (lo, items) = Make(disposeItems: false);
        lo.RemoveKey(B).ShouldBeTrue();
        items.ShouldAllBe(x => !x.Disposed);
        lo.ListedOrder.Select(x => x.ModKey).ShouldBe(new[] { A, C });
    }

    [Fact]
    public void RemoveKeyMissingDisposesNothing()
    {
        var (lo, items) = Make(disposeItems: true);
        lo.RemoveKey(TestConstants.MasterModKey4).ShouldBeFalse();
        items.ShouldAllBe(x => !x.Disposed);
        lo.Count.ShouldBe(3);
    }

    [Fact]
    public void RemoveKeyThenLoadOrderDisposeDisposesEveryItem()
    {
        var (lo, items) = Make(disposeItems: true);
        lo.RemoveKey(B);
        lo.Dispose();
        items.ShouldAllBe(x => x.Disposed);
    }

    [Fact]
    public void RemoveAtDisposesRemovedItem()
    {
        var (lo, items) = Make(disposeItems: true);
        lo.RemoveAt(1);
        items[1].Disposed.ShouldBeTrue();
        items[0].Disposed.ShouldBeFalse();
        items[2].Disposed.ShouldBeFalse();
    }

    [Fact]
    public void SetDisposesReplacedItem()
    {
        var (lo, items) = Make(disposeItems: true);
        var replacement = new DisposableItem(B);
        lo.Set(replacement);
        items[1].Disposed.ShouldBeTrue();
        replacement.Disposed.ShouldBeFalse();
        lo[1].ShouldBeSameAs(replacement);
    }

    [Fact]
    public void ClearDisposesItems()
    {
        var (lo, items) = Make(disposeItems: true);
        lo.Clear();
        items.ShouldAllBe(x => x.Disposed);
        lo.Count.ShouldBe(0);
    }
}
