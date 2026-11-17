using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Quests;

namespace PristonTale.Server.Net;

// Courier hunts on the wire (CourierWireQuest): the definition, the player's standing and kills.
internal static class CourierWireMapping
{
    public static CourierWireQuest Quest(CourierQuest quest, CourierStanding standing, IReadOnlyList<int> kills, long cooldownSeconds, bool withDescription) =>
        new(quest.Id, quest.Name, withDescription ? quest.Description : "", (byte)quest.Category, (byte)quest.Difficulty, quest.FieldCode, quest.MinLevel,
            quest.MaxLevel, quest.Experience, quest.Gold, quest.ItemKey ?? "", quest.ItemCount, (byte)standing, (int)Math.Min(cooldownSeconds, int.MaxValue),
            [.. quest.Objectives.Select((objective, index) => new CourierWireObjective(objective.Monster, objective.Count, index < kills.Count ? kills[index] : 0))]);
}
