using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// A daily quest claim prepared on a copy of the character: the status, and on success the experience, gold, item and coins
// it brings (the coins are stored with the character, the rest commits on the tick).
public sealed record DailyClaim(DailyQuestStatus Status, int QuestId = 0, ulong Experience = 0, long Gold = 0, int Coins = 0);

// Daily quests on the tick: accepting and claiming need the player at their giver (an NPC offering DailyQuest); kills count
// in FieldInstance.CreditQuests. The day turns at midnight on the server's clock (DailyQuests.DayOf).
public sealed partial class WorldServer
{
    private int Today => DailyQuests.DayOf(_clock.GetUtcNow());

    public Task<DailyQuestStatus> AcceptDailyQuestAsync(PlayerSession session, ulong npcEntityId, int questId) =>
        EnqueueAsync(session.EntityId, () =>
        {
            if (!TryGetPlaying(session, out var connection) || !AtDailyGiver(connection, npcEntityId))
            {
                return DailyQuestStatus.NotAtGiver;
            }

            return connection.Player.Character.DailyQuests.Accept(questId, connection.Player.Level, Today);
        });

    public Task<PreparedCharacterOperation<DailyClaim>?> PrepareDailyClaimAsync(PlayerSession session, ulong npcEntityId, int questId)
    {
        return EnqueueAsync<PreparedCharacterOperation<DailyClaim>?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection))
            {
                return null;
            }

            var player = connection.Player;
            var original = player.Character;
            if (!AtDailyGiver(connection, npcEntityId))
            {
                return new PreparedCharacterOperation<DailyClaim>(original, null, null, new DailyClaim(DailyQuestStatus.NotAtGiver));
            }

            var status = original.DailyQuests.CanClaim(questId, Today);
            if (status != DailyQuestStatus.Ok)
            {
                return new PreparedCharacterOperation<DailyClaim>(original, null, null, new DailyClaim(status));
            }

            var quest = DailyQuests.Get(questId);
            var candidate = original.CloneForInventoryOperation();
            if (_worldData.Current.Items.TryGet(ItemCodes.Parse(quest.ItemKey), out var definition)
                && !candidate.ReceiveGroundLoot(new ItemInstance(definition, Math.Min(quest.ItemCount, definition.MaxStack)), 0))
            {
                return new PreparedCharacterOperation<DailyClaim>(original, null, null, new DailyClaim(DailyQuestStatus.NoRoom));
            }

            var experience = DailyQuests.Experience(quest, original.Level);
            var gold = Math.Max(0, Math.Min(quest.Gold, MoneyLimits.CarryLimit(original.Level, original.Tier) - original.Money));
            candidate.AwardExperience(experience);
            candidate.TryChangeMoney(gold);
            candidate.DailyQuests.MarkClaimed(questId);
            return new PreparedCharacterOperation<DailyClaim>(original, candidate,
                CharacterRecordFactory.Capture(session.Loaded, candidate, connection.FieldId, player.Position),
                new DailyClaim(DailyQuestStatus.Ok, questId, experience, gold, quest.Coins));
        });
    }

    // Exempt from the quota: the claim is stored.
    public Task<bool> CommitDailyClaimAsync(PlayerSession session, PreparedCharacterOperation<DailyClaim> prepared)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            var candidate = CheckReceived(session, prepared, "a daily quest claim");
            var claim = prepared.Outcome;
            prepared.Original.CommitInventoryOperation(candidate);
            prepared.Original.AwardExperience(claim.Experience);
            prepared.Original.TryChangeMoney(claim.Gold);
            prepared.Original.DailyQuests.MarkClaimed(claim.QuestId);
            return true;
        }, waitForCapacity: true);
    }

    private bool AtDailyGiver(Connection connection, ulong npcEntityId) =>
        !connection.Player.IsDead && _fields[connection.FieldId].TryGetNpc(npcEntityId, out var npc)
        && NpcServiceTransactions.CanReach(connection.Player, npc) && npc.Definition.Find(NpcServiceKind.DailyQuest) is not null;
}
