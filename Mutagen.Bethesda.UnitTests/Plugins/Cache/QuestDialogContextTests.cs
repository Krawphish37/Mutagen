using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Starfield;
using Mutagen.Bethesda.Testing;
using Noggog.Testing.Extensions;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Cache;

/// <summary>
/// Overriding a record nested in a quest (topic, response) should only bring along
/// the record and its parents, not the quest's other topics and responses.
/// The quest's own subrecords (such as its dialog conditions) are still copied.
/// </summary>
public class QuestDialogContextTests
{
    private static (Starfield.StarfieldMod Mod, Starfield.Quest Quest, Starfield.DialogTopic Topic, Starfield.DialogResponses Response) StarfieldQuest()
    {
        var mod = new Starfield.StarfieldMod(TestConstants.PluginModKey, Starfield.StarfieldRelease.Starfield);
        var quest = mod.Quests.AddNew();
        quest.DialogConditions.Add(new Starfield.ConditionFloat() { Data = new Starfield.GetIsIDConditionData() });
        var topic = new Starfield.DialogTopic(mod.GetNextFormKey(), Starfield.StarfieldRelease.Starfield);
        var response = new Starfield.DialogResponses(mod.GetNextFormKey(), Starfield.StarfieldRelease.Starfield);
        topic.Responses.Add(response);
        topic.Responses.Add(new Starfield.DialogResponses(mod.GetNextFormKey(), Starfield.StarfieldRelease.Starfield));
        var otherTopic = new Starfield.DialogTopic(mod.GetNextFormKey(), Starfield.StarfieldRelease.Starfield);
        otherTopic.Responses.Add(new Starfield.DialogResponses(mod.GetNextFormKey(), Starfield.StarfieldRelease.Starfield));
        quest.DialogTopics.Add(topic);
        quest.DialogTopics.Add(otherTopic);
        quest.Scenes.Add(new Starfield.Scene(mod.GetNextFormKey(), Starfield.StarfieldRelease.Starfield));
        return (mod, quest, topic, response);
    }

    private static (Fallout4.Fallout4Mod Mod, Fallout4.Quest Quest, Fallout4.DialogTopic Topic, Fallout4.DialogResponses Response) Fallout4Quest()
    {
        var mod = new Fallout4.Fallout4Mod(TestConstants.PluginModKey, Fallout4.Fallout4Release.Fallout4);
        var quest = mod.Quests.AddNew();
        quest.DialogConditions.Add(new Fallout4.ConditionFloat() { Data = new Fallout4.FunctionConditionData() });
        var topic = new Fallout4.DialogTopic(mod.GetNextFormKey(), Fallout4.Fallout4Release.Fallout4);
        var response = new Fallout4.DialogResponses(mod.GetNextFormKey(), Fallout4.Fallout4Release.Fallout4);
        topic.Responses.Add(response);
        topic.Responses.Add(new Fallout4.DialogResponses(mod.GetNextFormKey(), Fallout4.Fallout4Release.Fallout4));
        var otherTopic = new Fallout4.DialogTopic(mod.GetNextFormKey(), Fallout4.Fallout4Release.Fallout4);
        otherTopic.Responses.Add(new Fallout4.DialogResponses(mod.GetNextFormKey(), Fallout4.Fallout4Release.Fallout4));
        quest.DialogTopics.Add(topic);
        quest.DialogTopics.Add(otherTopic);
        quest.Scenes.Add(new Fallout4.Scene(mod.GetNextFormKey(), Fallout4.Fallout4Release.Fallout4));
        return (mod, quest, topic, response);
    }

    [Fact]
    public void StarfieldQuestOverrideLeavesOutTopics()
    {
        var (mod, quest, _, _) = StarfieldQuest();
        var mod2 = new Starfield.StarfieldMod(TestConstants.PluginModKey2, Starfield.StarfieldRelease.Starfield);
        mod2.Quests.GetOrAddAsOverride(quest);
        mod2.Quests.ShouldHaveCount(1);
        mod2.Quests.First().DialogTopics.ShouldBeEmpty();
        mod2.Quests.First().Scenes.ShouldBeEmpty();
        mod2.Quests.First().DialogConditions.ShouldHaveCount(1);
        mod2.EnumerateMajorRecords().ShouldHaveCount(1);
    }

    [Fact]
    public void StarfieldTopicInQuest()
    {
        var (mod, quest, topic, _) = StarfieldQuest();
        var cache = mod.ToImmutableLinkCache();
        var context = mod.EnumerateMajorRecordContexts<Starfield.IDialogTopic, Starfield.IDialogTopicGetter>(linkCache: cache)
            .Single(x => x.Record.FormKey == topic.FormKey);

        var mod2 = new Starfield.StarfieldMod(TestConstants.PluginModKey2, Starfield.StarfieldRelease.Starfield);
        var topicOverride = context.GetOrAddAsOverride(mod2);
        topicOverride.FormKey.ShouldBe(topic.FormKey);
        topicOverride.Responses.ShouldBeEmpty();
        mod2.Quests.ShouldHaveCount(1);
        var questOverride = mod2.Quests.First();
        questOverride.FormKey.ShouldBe(quest.FormKey);
        questOverride.DialogConditions.ShouldHaveCount(1);
        questOverride.DialogTopics.ShouldHaveCount(1);
        questOverride.DialogTopics.First().ShouldBeSameAs(topicOverride);
        mod2.EnumerateMajorRecords().ShouldHaveCount(2);
    }

    [Fact]
    public void StarfieldResponseInQuest()
    {
        var (mod, quest, topic, response) = StarfieldQuest();
        var cache = mod.ToImmutableLinkCache();
        var context = mod.EnumerateMajorRecordContexts<Starfield.IDialogResponses, Starfield.IDialogResponsesGetter>(linkCache: cache)
            .Single(x => x.Record.FormKey == response.FormKey);

        var mod2 = new Starfield.StarfieldMod(TestConstants.PluginModKey2, Starfield.StarfieldRelease.Starfield);
        var responseOverride = context.GetOrAddAsOverride(mod2);
        responseOverride.FormKey.ShouldBe(response.FormKey);
        mod2.Quests.ShouldHaveCount(1);
        var questOverride = mod2.Quests.First();
        questOverride.FormKey.ShouldBe(quest.FormKey);
        questOverride.DialogConditions.ShouldHaveCount(1);
        questOverride.DialogTopics.ShouldHaveCount(1);
        var topicOverride = questOverride.DialogTopics.First();
        topicOverride.FormKey.ShouldBe(topic.FormKey);
        topicOverride.Responses.ShouldHaveCount(1);
        topicOverride.Responses.First().ShouldBeSameAs(responseOverride);
        mod2.EnumerateMajorRecords().ShouldHaveCount(3);
    }

    [Fact]
    public void StarfieldTopicInQuestDuplicateLeavesOutResponses()
    {
        var (mod, quest, topic, _) = StarfieldQuest();
        var cache = mod.ToImmutableLinkCache();
        var context = mod.EnumerateMajorRecordContexts<Starfield.IDialogTopic, Starfield.IDialogTopicGetter>(linkCache: cache)
            .Single(x => x.Record.FormKey == topic.FormKey);

        var mod2 = new Starfield.StarfieldMod(TestConstants.PluginModKey2, Starfield.StarfieldRelease.Starfield);
        var dup = context.DuplicateIntoAsNewRecord(mod2);
        dup.FormKey.ModKey.ShouldBe(TestConstants.PluginModKey2);
        dup.Responses.ShouldBeEmpty();
        mod2.Quests.ShouldHaveCount(1);
        mod2.Quests.First().FormKey.ShouldBe(quest.FormKey);
        mod2.Quests.First().DialogConditions.ShouldHaveCount(1);
        mod2.Quests.First().DialogTopics.ShouldHaveCount(1);
        mod2.Quests.First().DialogTopics.First().ShouldBeSameAs(dup);
        mod2.EnumerateMajorRecords().ShouldHaveCount(2);
    }

    [Fact]
    public void Fallout4QuestOverrideLeavesOutTopics()
    {
        var (mod, quest, _, _) = Fallout4Quest();
        var mod2 = new Fallout4.Fallout4Mod(TestConstants.PluginModKey2, Fallout4.Fallout4Release.Fallout4);
        mod2.Quests.GetOrAddAsOverride(quest);
        mod2.Quests.ShouldHaveCount(1);
        mod2.Quests.First().DialogTopics.ShouldBeEmpty();
        mod2.Quests.First().Scenes.ShouldBeEmpty();
        mod2.Quests.First().DialogConditions.ShouldHaveCount(1);
        mod2.EnumerateMajorRecords().ShouldHaveCount(1);
    }

    [Fact]
    public void Fallout4TopicInQuest()
    {
        var (mod, quest, topic, _) = Fallout4Quest();
        var cache = mod.ToImmutableLinkCache();
        var context = mod.EnumerateMajorRecordContexts<Fallout4.IDialogTopic, Fallout4.IDialogTopicGetter>(linkCache: cache)
            .Single(x => x.Record.FormKey == topic.FormKey);

        var mod2 = new Fallout4.Fallout4Mod(TestConstants.PluginModKey2, Fallout4.Fallout4Release.Fallout4);
        var topicOverride = context.GetOrAddAsOverride(mod2);
        topicOverride.FormKey.ShouldBe(topic.FormKey);
        topicOverride.Responses.ShouldBeEmpty();
        mod2.Quests.ShouldHaveCount(1);
        var questOverride = mod2.Quests.First();
        questOverride.FormKey.ShouldBe(quest.FormKey);
        questOverride.DialogConditions.ShouldHaveCount(1);
        questOverride.DialogTopics.ShouldHaveCount(1);
        questOverride.DialogTopics.First().ShouldBeSameAs(topicOverride);
        mod2.EnumerateMajorRecords().ShouldHaveCount(2);
    }

    [Fact]
    public void Fallout4ResponseInQuest()
    {
        var (mod, quest, topic, response) = Fallout4Quest();
        var cache = mod.ToImmutableLinkCache();
        var context = mod.EnumerateMajorRecordContexts<Fallout4.IDialogResponses, Fallout4.IDialogResponsesGetter>(linkCache: cache)
            .Single(x => x.Record.FormKey == response.FormKey);

        var mod2 = new Fallout4.Fallout4Mod(TestConstants.PluginModKey2, Fallout4.Fallout4Release.Fallout4);
        var responseOverride = context.GetOrAddAsOverride(mod2);
        responseOverride.FormKey.ShouldBe(response.FormKey);
        mod2.Quests.ShouldHaveCount(1);
        var questOverride = mod2.Quests.First();
        questOverride.FormKey.ShouldBe(quest.FormKey);
        questOverride.DialogConditions.ShouldHaveCount(1);
        questOverride.DialogTopics.ShouldHaveCount(1);
        var topicOverride = questOverride.DialogTopics.First();
        topicOverride.FormKey.ShouldBe(topic.FormKey);
        topicOverride.Responses.ShouldHaveCount(1);
        topicOverride.Responses.First().ShouldBeSameAs(responseOverride);
        mod2.EnumerateMajorRecords().ShouldHaveCount(3);
    }
}
