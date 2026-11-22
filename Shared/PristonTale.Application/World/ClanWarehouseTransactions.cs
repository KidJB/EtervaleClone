using PristonTale.Application.Sessions;
using PristonTale.Contracts.Texts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Social.Clans;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// The clan warehouse (ClanWarehouse): no NPC (entity 0), the warehouse's grid and rules over the clan's pages, open in a
// town to a member of a clan, changed by its rank's rights. The clan is the player's on the server (WorldPlayer.Clan),
// never the client's word. The clan's one copy in the world (storage) is changed on a clone, stored with the character
// and the log lines against its revision, and replaced at commit.
internal static class ClanWarehouseTransactions
{
    public static bool Handles(NpcServiceCommand command) => command.Service == NpcServiceKind.ClanWarehouse;

    public static PreparedNpcOperation Prepare(PlayerSession session, WorldPlayer player, FieldInstance field, NpcServiceCommand command,
        WarehouseStorage? storage, long nowSeconds)
    {
        var original = player.Character;
        if (command.Action == NpcServiceAction.Close)
        {
            player.OpenClanWarehouse = 0;
            return new PreparedNpcOperation(original, null, null, new NpcServiceReply(NpcServiceTransactions.Done(original, null, GameText.None)));
        }

        if (player.Clan is not { } clan)
        {
            player.OpenClanWarehouse = 0;
            return Refused(original, NpcServiceStatus.NotOpen, GameText.Of(GameTextId.ClanNotInClan));
        }

        // Opened at a warehouse keeper offering the clan warehouse, within reach.
        if (!field.TryGetNpc(command.NpcEntityId, out var keeper) || !NpcServiceTransactions.CanReach(player, keeper)
            || keeper.Definition.Find(NpcServiceKind.ClanWarehouse) is null)
        {
            return Refused(original, NpcServiceStatus.OutOfReach, GameText.Of(GameTextId.NpcOutOfReach));
        }

        if (player.IsDead || player.Shop is not null || !player.InVillage)
        {
            return Refused(original, NpcServiceStatus.OutOfReach, GameText.Of(GameTextId.ClanWarehouseNotInTown));
        }

        if (storage is null || command.Action != NpcServiceAction.Open && player.OpenClanWarehouse != clan.ClanId)
        {
            return Refused(original, NpcServiceStatus.NotOpen, GameText.Of(GameTextId.WarehouseNotOpen));
        }

        if (!ClanWarehouse.Allows(clan.Rights, command.Action))
        {
            return Refused(original, NpcServiceStatus.NotPermitted, GameText.Of(GameTextId.ClanWarehouseNotPermitted), storage.Revision);
        }

        if (command.Action is NpcServiceAction.Open or NpcServiceAction.Refresh)
        {
            player.OpenClanWarehouse = clan.ClanId;
            return new PreparedNpcOperation(original, null, null, new NpcServiceReply(NpcServiceTransactions.Done(original, storage, GameText.None))
            {
                Warehouse = storage.View(original)
            });
        }

        if (NpcServiceTransactions.Settled(original, storage, command) is { } settled)
        {
            return settled;
        }

        var candidate = original.CloneForInventoryOperation();
        var changed = storage.Clone();
        var status = command.Action switch
        {
            NpcServiceAction.Deposit => changed.TryDeposit(candidate, command.ItemId, command.Anchor, command.Covered),
            NpcServiceAction.Withdraw => changed.TryWithdraw(candidate, command.ItemId, command.Anchor, command.Covered),
            _ => changed.TryMove(command.ItemId, command.Anchor, command.Covered)
        };
        if (status != NpcServiceStatus.Ok)
        {
            return Refused(original, status, NpcServiceTransactions.Describe(status), storage.Revision);
        }

        candidate.RecordServiceOperation(command);
        var snapshot = CharacterRecordFactory.Capture(session.Loaded!, candidate, field.FieldId, player.Position);
        var reply = new NpcServiceReply(NpcServiceTransactions.Done(candidate, changed, GameText.None)) { Warehouse = changed.View(candidate) };
        return new PreparedNpcOperation(original, candidate, snapshot, reply, 0, changed, storage.Revision, ClanWarehouse: clan.ClanId,
            ClanLog: Log(original, storage, command, nowSeconds));
    }

    // The items that changed hands: the one the command names and the one it was exchanged with, if any. Moves are not logged.
    private static ClanWarehouseLogEntry[] Log(Simulation.Characters.PlayerCharacter original, WarehouseStorage storage, NpcServiceCommand command, long now)
    {
        var name = original.Name;
        return command.Action switch
        {
            NpcServiceAction.Deposit => Lines(now, name, Carried(original, command.ItemId), ClanWarehouseLogAction.Deposit,
                storage.Find(command.Covered), ClanWarehouseLogAction.Withdraw),
            NpcServiceAction.Withdraw => Lines(now, name, storage.Find(command.ItemId), ClanWarehouseLogAction.Withdraw,
                Carried(original, command.Covered), ClanWarehouseLogAction.Deposit),
            _ => []
        };
    }

    private static ItemInstance? Carried(Simulation.Characters.PlayerCharacter character, Guid itemId) =>
        itemId != Guid.Empty && character.Inventory.Find(itemId) is var at && at >= 0 ? character.Inventory[at] : null;

    private static ClanWarehouseLogEntry[] Lines(long now, string name, ItemInstance? item, ClanWarehouseLogAction action, ItemInstance? other,
        ClanWarehouseLogAction otherAction)
    {
        var lines = new List<ClanWarehouseLogEntry>(2);
        if (item is not null)
        {
            lines.Add(Line(now, name, item, action));
        }

        if (other is not null)
        {
            lines.Add(Line(now, name, other, otherAction));
        }

        return lines.ToArray();
    }

    private static ClanWarehouseLogEntry Line(long now, string name, ItemInstance item, ClanWarehouseLogAction action) =>
        new(now, name, action, item.Definition.CatalogKey, item.Definition.Name, item.Count);

    private static PreparedNpcOperation Refused(Simulation.Characters.PlayerCharacter original, NpcServiceStatus status, GameText message, long storageRevision = 0) =>
        NpcServiceTransactions.Final(original, NpcServiceOutcome.Failed(status, message, original.InventoryRevision, storageRevision));
}
