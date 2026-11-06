using System.Text.Json;
using PristonTale.Simulation.Quests;

namespace PristonTale.Persistence.Abstractions.Characters;

// A character's daily quests as stored (characters.daily_quests): the day, the accepted quests with their kills and the
// quests claimed that day. An empty column is a character with none.
public sealed record SavedDailyQuests(int Day, Dictionary<int, int> Accepted, int[] Done);

public static class DailyQuestCodec
{
    public static string Capture(DailyQuestState state) =>
        state.Day == 0 && state.Accepted.Count == 0 && state.Done.Count == 0
            ? ""
            : JsonSerializer.Serialize(new SavedDailyQuests(state.Day, new Dictionary<int, int>(state.Accepted), [.. state.Done]), CharacterStateCodec.Options);

    public static void Restore(DailyQuestState state, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var saved = JsonSerializer.Deserialize<SavedDailyQuests>(json, CharacterStateCodec.Options) ?? throw new InvalidDataException("Empty daily quests.");
        state.Restore(saved.Day, saved.Accepted ?? [], saved.Done ?? []);
    }
}
