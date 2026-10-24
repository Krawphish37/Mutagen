using Mutagen.Bethesda.Fallout3;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Testing;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Fallout3;

public class DialogTopicOverrideTests
{
    [Fact]
    public void TopicOverrideLeavesOutResponses()
    {
        var master = new Fallout3Mod(TestConstants.MasterModKey, Fallout3Release.Fallout3);
        var topic = master.DialogTopics.AddNew("Topic");
        topic.Name = "Name";
        topic.Responses.Add(new DialogResponses(master));
        topic.Responses.Add(new DialogResponses(master));

        var patch = new Fallout3Mod(TestConstants.PluginModKey, Fallout3Release.Fallout3);
        var over = patch.DialogTopics.GetOrAddAsOverride((IDialogTopicGetter)topic);

        over.FormKey.ShouldBe(topic.FormKey);
        over.Name.ShouldBe("Name");
        over.Responses.ShouldBeEmpty();
        topic.Responses.Count.ShouldBe(2);
    }

    [Fact]
    public void ResponsesContextOverrideBringsOnlyThoseResponses()
    {
        var master = new Fallout3Mod(TestConstants.MasterModKey, Fallout3Release.Fallout3);
        var topic = master.DialogTopics.AddNew("Topic");
        var responses1 = new DialogResponses(master);
        var responses2 = new DialogResponses(master);
        topic.Responses.Add(responses1);
        topic.Responses.Add(responses2);
        var linkCache = master.ToImmutableLinkCache<IFallout3Mod, IFallout3ModGetter>();

        var patch = new Fallout3Mod(TestConstants.PluginModKey, Fallout3Release.Fallout3);
        linkCache.ResolveContext<IDialogResponses, IDialogResponsesGetter>(responses1.FormKey)
            .GetOrAddAsOverride(patch);

        var overTopic = patch.DialogTopics.ShouldHaveSingleItem();
        overTopic.FormKey.ShouldBe(topic.FormKey);
        overTopic.Responses.ShouldHaveSingleItem().FormKey.ShouldBe(responses1.FormKey);
    }
}
