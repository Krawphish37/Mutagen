using System.Buffers.Binary;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Testing.AutoData;
using Shouldly;
using Xunit;
using Fo4 = Mutagen.Bethesda.Fallout4;
using Sf = Mutagen.Bethesda.Starfield;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

public class QuestChildrenRecordCountTests
{
    private static void WrittenCountMatchesFile(IModGetter mod, GameRelease release)
    {
        using var stream = new MemoryStream();
        mod.WriteToBinary(stream);
        var bytes = stream.ToArray();

        var meta = GameConstants.Get(release);
        var majorHeaderLength = meta.MajorConstants.HeaderLength;
        var headerCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(majorHeaderLength + 10));

        // Every record and every group (nested ones included) after the mod header
        uint fileCount = 0;
        var pos = majorHeaderLength + checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        while (pos < bytes.Length)
        {
            fileCount++;
            var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 4)));
            pos += bytes.AsSpan(pos, 4).SequenceEqual("GRUP"u8)
                ? meta.GroupConstants.HeaderLength
                : majorHeaderLength + length;
        }

        mod.GetRecordCount().ShouldBe(fileCount);
        headerCount.ShouldBe(fileCount);
    }

    [Theory, MutagenAutoData]
    public void StarfieldQuestWithoutChildrenAddsNoGroup(ModKey modKey)
    {
        var mod = new Sf.StarfieldMod(modKey, Sf.StarfieldRelease.Starfield);
        mod.Quests.AddNew();
        mod.Quests.AddNew();

        WrittenCountMatchesFile(mod, GameRelease.Starfield);
    }

    [Theory, MutagenAutoData]
    public void StarfieldQuestChildrenGroupsCounted(ModKey modKey)
    {
        var mod = new Sf.StarfieldMod(modKey, Sf.StarfieldRelease.Starfield);
        mod.Quests.AddNew();
        mod.Quests.AddNew().Scenes.Add(new Sf.Scene(mod));
        mod.Quests.AddNew().DialogBranches.Add(new Sf.DialogBranch(mod));
        mod.Quests.AddNew().DialogTopics.Add(new Sf.DialogTopic(mod));
        var topic = new Sf.DialogTopic(mod);
        topic.Responses.Add(new Sf.DialogResponses(mod));
        mod.Quests.AddNew().DialogTopics.Add(topic);

        WrittenCountMatchesFile(mod, GameRelease.Starfield);
    }

    [Theory, MutagenAutoData]
    public void Fallout4QuestWithoutChildrenAddsNoGroup(ModKey modKey)
    {
        var mod = new Fo4.Fallout4Mod(modKey, Fo4.Fallout4Release.Fallout4);
        mod.Quests.AddNew();
        mod.Quests.AddNew();

        WrittenCountMatchesFile(mod, GameRelease.Fallout4);
    }

    [Theory, MutagenAutoData]
    public void Fallout4QuestChildrenGroupsCounted(ModKey modKey)
    {
        var mod = new Fo4.Fallout4Mod(modKey, Fo4.Fallout4Release.Fallout4);
        mod.Quests.AddNew();
        mod.Quests.AddNew().Scenes.Add(new Fo4.Scene(mod));
        mod.Quests.AddNew().DialogBranches.Add(new Fo4.DialogBranch(mod));
        mod.Quests.AddNew().DialogTopics.Add(new Fo4.DialogTopic(mod));
        var topic = new Fo4.DialogTopic(mod);
        topic.Responses.Add(new Fo4.DialogResponses(mod));
        mod.Quests.AddNew().DialogTopics.Add(topic);

        WrittenCountMatchesFile(mod, GameRelease.Fallout4);
    }
}
