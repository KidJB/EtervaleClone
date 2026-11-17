using System.Text.Json;
using PristonTale.Simulation.Quests;

namespace PristonTale.Persistence.Abstractions.Characters;

// A character's Courier hunts as stored (characters.courier_quests): each accepted hunt's kills by objective and when it was
// accepted, and when each quest was last turned in (Unix seconds). An empty column is a character with none.
public sealed record SavedCourierHunt(int[] Kills, long AcceptedAt);

public sealed record SavedCourierQuests(Dictionary<int, SavedCourierHunt> Active, Dictionary<int, long> TurnedIn);

public static class CourierQuestCodec
{
    public static string Capture(CourierQuestState state) =>
        state.Active.Count == 0 && state.TurnedIn.Count == 0
            ? ""
            : JsonSerializer.Serialize(new SavedCourierQuests(
                state.Active.ToDictionary(static pair => pair.Key, static pair => new SavedCourierHunt([.. pair.Value.Kills], pair.Value.AcceptedAt)),
                new Dictionary<int, long>(state.TurnedIn)), CharacterStateCodec.Options);

    public static void Restore(CourierQuestState state, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var saved = JsonSerializer.Deserialize<SavedCourierQuests>(json, CharacterStateCodec.Options) ?? throw new InvalidDataException("Empty Courier quests.");
        state.Restore(
            (saved.Active ?? []).Select(static pair => KeyValuePair.Create(pair.Key, new CourierProgress(pair.Value.Kills ?? [], pair.Value.AcceptedAt))),
            saved.TurnedIn ?? []);
    }
}
