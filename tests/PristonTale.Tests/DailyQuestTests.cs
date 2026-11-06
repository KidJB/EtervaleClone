using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Quests;

namespace PristonTale.Tests;

// Daily quests: the table, accepting by level, kills counting toward accepted hunts, a claim once a day, the day turning,
// the saved form and the messages.
internal static class DailyQuestTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("daily quests: unique ids in order, each band reachable, rewards scaled to the level", TableAsync);
        yield return ("daily quests: accepted by level, kills count toward accepted hunts, claimed once a day, and a new day starts clean", StateAsync);
        yield return ("daily quests: the state survives the save and the wire", WireAsync);
    }

    private static Task TableAsync()
    {
        var all = DailyQuests.All;
        Assert.True(all.Select(quest => quest.Id).SequenceEqual(Enumerable.Range(1, all.Count)) && DailyQuests.Get(0) is null && DailyQuests.Get(all.Count + 1) is null,
            "daily quest ids are not 1 to the table's size");
        Assert.True(all.All(quest => quest.Kills > 0 && quest.MinLevel <= quest.MaxLevel && quest.MonsterLevel >= 1 && quest.Coins > 0 && quest.ItemCount > 0),
            "a daily quest has no hunt or reward");
        Assert.True(Enumerable.Range(10, 140).All(level => all.Any(quest => DailyQuests.Offered(quest, level))), "a level from 10 has no daily quest");
        Assert.True(DailyQuests.Experience(all[0], 10) > 0 && DailyQuests.Experience(all[^1], 120) > DailyQuests.Experience(all[0], 10),
            "the experience reward does not grow with the level");
        return Task.CompletedTask;
    }

    private static Task StateAsync()
    {
        var state = new DailyQuestState();
        const int Day = 739_000;
        Assert.True(state.Accept(8, 50, Day) == DailyQuestStatus.NotOffered && state.Accept(5, 50, Day) == DailyQuestStatus.Ok
            && state.Accept(5, 50, Day) == DailyQuestStatus.AlreadyAccepted, "a quest outside the level was accepted, or one twice");
        var quest = DailyQuests.Get(5);
        state.OnKill(quest.MonsterLevel - 1, Day);
        Assert.True(state.Accepted[5] == 0 && state.CanClaim(5, Day) == DailyQuestStatus.NotFinished, "a monster below the hunt's level counted");
        for (var kill = 0; kill < quest.Kills + 5; kill++)
        {
            state.OnKill(quest.MonsterLevel, Day);
        }

        Assert.True(state.Accepted[5] == quest.Kills && state.CanClaim(5, Day) == DailyQuestStatus.Ok, "the kills did not complete the hunt, or passed it");
        state.MarkClaimed(5);
        Assert.True(state.CanClaim(5, Day) == DailyQuestStatus.DoneToday && state.Accept(5, 50, Day) == DailyQuestStatus.DoneToday,
            "a claimed quest was offered again the same day");
        Assert.True(state.Accept(5, 50, Day + 1) == DailyQuestStatus.Ok && state.Done.Count == 0, "the next day did not offer the quest again");
        Assert.True(UsableItemCatalog.TryGet(quest.ItemKey, out _) || quest.ItemKey.StartsWith("PL", StringComparison.Ordinal), "a reward item is not a potion");
        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        var state = new DailyQuestState();
        state.Accept(3, 30, 739_001);
        state.OnKill(40, 739_001);
        state.Accept(4, 40, 739_001);
        state.MarkClaimed(4);
        var copy = new DailyQuestState();
        DailyQuestCodec.Restore(copy, DailyQuestCodec.Capture(state));
        Assert.True(copy.Day == 739_001 && copy.Accepted[3] == 1 && copy.Done.Contains(4) && DailyQuestCodec.Capture(new DailyQuestState()) == "",
            "the daily quests did not survive the save");

        var buffer = new byte[FrameCodec.MaxFrameSize];
        var packet = new DailyQuestStatePacket(739_001, [new DailyQuestEntry(3, 1, DailyQuestEntry.Accepted), new DailyQuestEntry(4, 0, DailyQuestEntry.Claimed)]);
        var read = DailyQuestStatePacket.Read(buffer.AsSpan(0, packet.Write(buffer)));
        Assert.True(read.Day == 739_001 && read.Entries.SequenceEqual(packet.Entries), "the daily state did not survive the wire");
        var request = new DailyQuestRequestPacket(Guid.NewGuid(), 72, 3, DailyQuestRequestPacket.Claim);
        Assert.True(DailyQuestRequestPacket.Read(buffer.AsSpan(0, request.Write(buffer))) == request, "the request did not survive the wire");
        var result = new DailyQuestResultPacket(request.OperationId, 5);
        Assert.True(DailyQuestResultPacket.Read(buffer.AsSpan(0, result.Write(buffer))) == result, "the result did not survive the wire");
        return Task.CompletedTask;
    }
}
