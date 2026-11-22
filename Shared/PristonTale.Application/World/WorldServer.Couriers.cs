using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// A Courier turn-in prepared on a copy of the character: the status, and on success the experience, gold and item it brings.
public sealed record CourierTurnIn(CourierStatus Status, int QuestId = 0, ulong Experience = 0, long Gold = 0);

// What a Courier shows a player: each of its hunts offered at the player's level (or already accepted) and how it stands.
public sealed record CourierBoardView(ulong NpcId, string Name, IReadOnlyList<(CourierQuest Quest, CourierStanding Standing, int[] Kills, long CooldownSeconds)> Quests);

// The Quest Couriers on the tick: accepting needs the player at the quest's giver, a turn-in at its turn-in Courier, both in
// reach; abandoning works anywhere. Kills count in FieldInstance.CreditCouriers. The catalog comes from CourierStore
// (data/couriers.json); without one no Courier stands and no hunt is offered.
public sealed partial class WorldServer
{
    public CourierCatalogStore? CourierStore { get; set; }

    public CourierCatalog Couriers => CourierStore?.Current ?? CourierCatalog.Empty;

    public Task<CourierBoardView?> CourierBoardAsync(PlayerSession session, ulong npcEntityId) =>
        EnqueueAsync<CourierBoardView?>(session.EntityId, () =>
        {
            if (!TryGetPlaying(session, out var connection))
            {
                return null;
            }

            if (npcEntityId == CourierCatalog.BookNpc)
            {
                return CourierBook(connection, npcEntityId);
            }

            if (!AtCourier(connection, npcEntityId, out var npc))
            {
                return null;
            }

            var catalog = Couriers;
            var now = _clock.GetUtcNow();
            var state = connection.Player.Character.Couriers;
            var level = connection.Player.Level;
            var shown = new List<(CourierQuest, CourierStanding, int[], long)>();
            foreach (var quest in catalog.Quests)
            {
                var accepted = state.Active.TryGetValue(quest.Id, out var progress);
                var here = quest.Giver == npc.Definition.Code || accepted && quest.TurnIn == npc.Definition.Code;
                if (!here || !accepted && !quest.Offered(level))
                {
                    continue;
                }

                var standing = state.Standing(quest, now, catalog.Schedule);
                var cooldown = standing == CourierStanding.Cooldown && state.TurnedIn.TryGetValue(quest.Id, out var at)
                    ? Math.Max(0, at + quest.CooldownMinutes * 60L - now.ToUnixTimeSeconds())
                    : 0;
                shown.Add((quest, standing, accepted ? [.. progress!.Kills] : new int[quest.Objectives.Count], cooldown));
            }

            return new CourierBoardView(npcEntityId, npc.Definition.Label, shown);
        });

    // The Quest Book's board (CourierCatalog.BookQuests), with each hunt's standing and kills as a Courier shows them.
    private CourierBoardView CourierBook(Connection connection, ulong bookId)
    {
        var catalog = Couriers;
        var now = _clock.GetUtcNow();
        var state = connection.Player.Character.Couriers;
        var shown = new List<(CourierQuest, CourierStanding, int[], long)>();
        foreach (var quest in catalog.BookQuests(connection.Player.Level, state.Active.ContainsKey))
        {
            var standing = state.Standing(quest, now, catalog.Schedule);
            var cooldown = standing == CourierStanding.Cooldown && state.TurnedIn.TryGetValue(quest.Id, out var at)
                ? Math.Max(0, at + quest.CooldownMinutes * 60L - now.ToUnixTimeSeconds())
                : 0;
            shown.Add((quest, standing, state.Active.TryGetValue(quest.Id, out var progress) ? [.. progress.Kills] : new int[quest.Objectives.Count], cooldown));
        }

        return new CourierBoardView(bookId, "", shown);
    }

    public Task<CourierStatus> AcceptCourierQuestAsync(PlayerSession session, ulong npcEntityId, int questId) =>
        EnqueueAsync(session.EntityId, () =>
        {
            if (!TryGetPlaying(session, out var connection) || !AtCourier(connection, npcEntityId, out var npc))
            {
                return CourierStatus.NotAtCourier;
            }

            var catalog = Couriers;
            if (catalog.Get(questId) is not { } quest || quest.Giver != npc.Definition.Code)
            {
                return CourierStatus.NotOffered;
            }

            return connection.Player.Character.Couriers.Accept(quest, connection.Player.Level, _clock.GetUtcNow(), catalog.Schedule);
        });

    public Task<CourierStatus> AbandonCourierQuestAsync(PlayerSession session, int questId) =>
        EnqueueAsync(session.EntityId, () =>
            TryGetPlaying(session, out var connection) ? connection.Player.Character.Couriers.Abandon(questId) : CourierStatus.Rejected);

    public Task<PreparedCharacterOperation<CourierTurnIn>?> PrepareCourierTurnInAsync(PlayerSession session, ulong npcEntityId, int questId)
    {
        return EnqueueAsync<PreparedCharacterOperation<CourierTurnIn>?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection))
            {
                return null;
            }

            var player = connection.Player;
            var original = player.Character;
            var quest = Couriers.Get(questId);
            if (!AtCourier(connection, npcEntityId, out var npc) || quest is null || quest.TurnIn != npc.Definition.Code)
            {
                return new PreparedCharacterOperation<CourierTurnIn>(original, null, null, new CourierTurnIn(CourierStatus.NotAtCourier));
            }

            var status = original.Couriers.CanTurnIn(quest);
            if (status != CourierStatus.Ok)
            {
                return new PreparedCharacterOperation<CourierTurnIn>(original, null, null, new CourierTurnIn(status));
            }

            var candidate = original.CloneForInventoryOperation();
            if (quest.ItemCount > 0 && ItemCodes.TryParse(quest.ItemKey, out var code) && _worldData.Current.Items.TryGet(code, out var definition)
                && !candidate.ReceiveGroundLoot(new ItemInstance(definition, Math.Clamp(quest.ItemCount, 1, definition.MaxStack)), 0))
            {
                return new PreparedCharacterOperation<CourierTurnIn>(original, null, null, new CourierTurnIn(CourierStatus.NoRoom));
            }

            var experience = (ulong)Math.Max(0, quest.Experience);
            var gold = Math.Max(0, Math.Min(quest.Gold, MoneyLimits.CarryLimit(original.Level, original.Tier) - original.Money));
            var now = _clock.GetUtcNow();
            candidate.AwardExperience(experience);
            candidate.TryChangeMoney(gold);
            candidate.Couriers.MarkTurnedIn(questId, now);
            return new PreparedCharacterOperation<CourierTurnIn>(original, candidate,
                CharacterRecordFactory.Capture(session.Loaded, candidate, connection.FieldId, player.Position),
                new CourierTurnIn(CourierStatus.Ok, questId, experience, gold));
        });
    }

    // Exempt from the quota: the turn-in is stored.
    public Task<bool> CommitCourierTurnInAsync(PlayerSession session, PreparedCharacterOperation<CourierTurnIn> prepared)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            var candidate = CheckReceived(session, prepared, "a Courier turn-in");
            var turnIn = prepared.Outcome;
            prepared.Original.CommitInventoryOperation(candidate);
            prepared.Original.AwardExperience(turnIn.Experience);
            prepared.Original.TryChangeMoney(turnIn.Gold);
            prepared.Original.Couriers.MarkTurnedIn(turnIn.QuestId, _clock.GetUtcNow());
            return true;
        }, waitForCapacity: true);
    }

    // A game master's view and resets of a player's Courier hunts (by character name, in game).
    public Task<string?> CourierProgressAsync(string name) =>
        EnqueueAsync<string?>(NoOwner, () =>
        {
            if (FindPlaying(name) is not { } player)
            {
                return null;
            }

            var catalog = Couriers;
            var lines = player.Character.Couriers.Active.Select(pair => catalog.Get(pair.Key) is { } quest
                ? $"{quest.Id} {quest.Name}: " + string.Join(", ", quest.Objectives.Select((objective, index) =>
                    $"{objective.Monster} {(index < pair.Value.Kills.Length ? pair.Value.Kills[index] : 0)}/{objective.Count}"))
                : $"{pair.Key} (no longer defined)").ToList();
            lines.Add($"{player.Character.Couriers.TurnedIn.Count} quests turned in");
            return string.Join(" | ", lines);
        }, waitForCapacity: true);

    public Task<bool> ResetCourierQuestsAsync(string name, CourierCategory? category) =>
        EnqueueAsync(NoOwner, () =>
        {
            if (FindPlaying(name) is not { } player)
            {
                return false;
            }

            player.Character.Couriers.Reset(Couriers, category);
            return true;
        }, waitForCapacity: true);

    private WorldPlayer? FindPlaying(string name)
    {
        foreach (var connection in _connections.Values)
        {
            if (string.Equals(connection.Player.Character.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return connection.Player;
            }
        }

        return null;
    }

    private bool AtCourier(Connection connection, ulong npcEntityId, out WorldNpc npc) =>
        _fields[connection.FieldId].TryGetNpc(npcEntityId, out npc!) && !connection.Player.IsDead
        && NpcServiceTransactions.CanReach(connection.Player, npc) && npc.Definition.Find(NpcServiceKind.Courier) is not null;
}
