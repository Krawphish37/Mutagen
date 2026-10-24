using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Testing;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Skyrim;

public class DialogTopicOverrideTests
{
    [Fact]
    public void TopicOverrideLeavesOutResponses()
    {
        var master = new SkyrimMod(TestConstants.MasterModKey, SkyrimRelease.SkyrimSE);
        var topic = master.DialogTopics.AddNew("Topic");
        topic.Responses.Add(new DialogResponses(master));
        topic.Responses.Add(new DialogResponses(master));

        var patch = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        var over = patch.DialogTopics.GetOrAddAsOverride((IDialogTopicGetter)topic);

        over.FormKey.ShouldBe(topic.FormKey);
        over.Responses.ShouldBeEmpty();
        topic.Responses.Count.ShouldBe(2);
    }

    [Fact]
    public void ResponsesContextOverrideBringsOnlyThoseResponses()
    {
        var master = new SkyrimMod(TestConstants.MasterModKey, SkyrimRelease.SkyrimSE);
        var topic = master.DialogTopics.AddNew("Topic");
        var responses1 = new DialogResponses(master);
        var responses2 = new DialogResponses(master);
        topic.Responses.Add(responses1);
        topic.Responses.Add(responses2);
        var linkCache = master.ToImmutableLinkCache<ISkyrimMod, ISkyrimModGetter>();

        var patch = new SkyrimMod(TestConstants.PluginModKey, SkyrimRelease.SkyrimSE);
        linkCache.ResolveContext<IDialogResponses, IDialogResponsesGetter>(responses1.FormKey)
            .GetOrAddAsOverride(patch);

        var overTopic = patch.DialogTopics.ShouldHaveSingleItem();
        overTopic.FormKey.ShouldBe(topic.FormKey);
        overTopic.Responses.ShouldHaveSingleItem().FormKey.ShouldBe(responses1.FormKey);
    }
}
