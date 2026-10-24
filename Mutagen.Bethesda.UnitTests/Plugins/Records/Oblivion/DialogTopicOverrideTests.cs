using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Testing;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Oblivion;

public class DialogTopicOverrideTests
{
    [Fact]
    public void TopicOverrideLeavesOutItems()
    {
        var master = new OblivionMod(TestConstants.MasterModKey, OblivionRelease.Oblivion);
        var topic = master.DialogTopics.AddNew("Topic");
        topic.Name = "Name";
        topic.Items.Add(new DialogItem(master));
        topic.Items.Add(new DialogItem(master));

        var patch = new OblivionMod(TestConstants.PluginModKey, OblivionRelease.Oblivion);
        var over = patch.DialogTopics.GetOrAddAsOverride((IDialogTopicGetter)topic);

        over.FormKey.ShouldBe(topic.FormKey);
        over.Name.ShouldBe("Name");
        over.Items.ShouldBeEmpty();
        topic.Items.Count.ShouldBe(2);
    }

    [Fact]
    public void ItemContextOverrideBringsOnlyThatItem()
    {
        var master = new OblivionMod(TestConstants.MasterModKey, OblivionRelease.Oblivion);
        var topic = master.DialogTopics.AddNew("Topic");
        var item1 = new DialogItem(master);
        var item2 = new DialogItem(master);
        topic.Items.Add(item1);
        topic.Items.Add(item2);
        var linkCache = master.ToImmutableLinkCache<IOblivionMod, IOblivionModGetter>();

        var patch = new OblivionMod(TestConstants.PluginModKey, OblivionRelease.Oblivion);
        linkCache.ResolveContext<IDialogItem, IDialogItemGetter>(item1.FormKey)
            .GetOrAddAsOverride(patch);

        var overTopic = patch.DialogTopics.ShouldHaveSingleItem();
        overTopic.FormKey.ShouldBe(topic.FormKey);
        overTopic.Items.ShouldHaveSingleItem().FormKey.ShouldBe(item1.FormKey);
    }
}
