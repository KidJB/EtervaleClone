using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.GameLogs;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// The Item Distributor on the tick: whether the player stands at an NPC offering it, an item taken out of the bag to send
// (stored with the receiving account's box before its commit), and the commits of items taken in or sent away.
public sealed partial class WorldServer
{
    // The player is alive, in the world and within reach of that NPC, which offers the service.
    public Task<bool> AtNpcServiceAsync(PlayerSession session, ulong npcEntityId, NpcServiceKind service) =>
        EnqueueAsync(session.EntityId, () => TryGetPlaying(session, out var connection) && !connection.Player.IsDead
            && _fields[connection.FieldId].TryGetNpc(npcEntityId, out var npc) && NpcServiceTransactions.CanReach(connection.Player, npc)
            && npc.Definition.Find(service) is not null);

    // A bag item the player may hand over, taken out of a copy of its character; the outcome is the item, or null when
    // there is none to send (gone, bound, carried in a trade or a shop).
    public Task<PreparedCharacterOperation<ItemInstance?>?> PrepareItemBoxSendAsync(PlayerSession session, Guid itemId)
    {
        return EnqueueAsync<PreparedCharacterOperation<ItemInstance?>?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection))
            {
                return null;
            }

            var player = connection.Player;
            var original = player.Character;
            var anchor = original.Inventory.Find(itemId);
            if (anchor < 0 || player.Trade is not null || player.Shop is not null || original.Inventory[anchor] is not { } item
                || !BoundItems.CanHandOver(original, item))
            {
                return new PreparedCharacterOperation<ItemInstance?>(original, null, null, null);
            }

            var candidate = original.CloneForInventoryOperation();
            var sent = candidate.Inventory.TakeAt(candidate.Inventory.Find(itemId));
            return new PreparedCharacterOperation<ItemInstance?>(original, candidate,
                CharacterRecordFactory.Capture(session.Loaded, candidate, connection.FieldId, player.Position), sent);
        });
    }

    // Exempt from the quota: the box is already stored. kind: ItemBoxTaken (into the bag) or ItemBoxSent (out of it).
    public Task<bool> CommitItemBoxAsync(PlayerSession session, PreparedOperation operation, ItemTransferKind kind)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            var candidate = CheckReceived(session, operation, "an Item Distributor transfer");
            if (Transfer(kind, Guid.NewGuid(), session.FieldId) is { } log)
            {
                if (kind == ItemTransferKind.ItemBoxSent)
                {
                    log.Left(operation.Original, candidate, CharacterIdOf(session), null, session.AccountId);
                }
                else
                {
                    log.Arrived(operation.Original, candidate, null, CharacterIdOf(session), session.AccountId);
                }

                Log(log);
            }

            operation.Original.CommitInventoryOperation(candidate);
            return true;
        }, waitForCapacity: true);
    }
}
