using PristonTale.Application.Sessions;
using PristonTale.Contracts.Texts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// The storage a caravan carries: no NPC (entity 0), open only while the player's caravan is out, the warehouse's grid and
// rules without gold. The storage read on open (NpcAppService) is changed on a copy and stored with the character.
internal static class CaravanTransactions
{
    public static bool Handles(NpcServiceCommand command) => command.Service == NpcServiceKind.Caravan;

    public static PreparedNpcOperation Prepare(PlayerSession session, WorldPlayer player, FieldInstance field, NpcServiceCommand command, long nowSeconds)
    {
        var original = player.Character;
        var storage = session.Caravan;
        if (player.IsDead || player.Shop is not null || original.Caravan is not { } caravan || !caravan.ActiveAt(nowSeconds))
        {
            return NpcServiceTransactions.Final(original, NpcServiceOutcome.Failed(NpcServiceStatus.OutOfReach, GameText.Of(GameTextId.CaravanAway), original.InventoryRevision));
        }

        if (command.Action == NpcServiceAction.Close)
        {
            session.Caravan = null;
            return new PreparedNpcOperation(original, null, null, new NpcServiceReply(NpcServiceTransactions.Done(original, null, GameText.None)));
        }

        if (storage is null)
        {
            return NpcServiceTransactions.Final(original, NpcServiceOutcome.Failed(NpcServiceStatus.NotOpen, GameText.Of(GameTextId.WarehouseNotOpen), original.InventoryRevision));
        }

        if (command.Action is NpcServiceAction.Open or NpcServiceAction.Refresh)
        {
            return new PreparedNpcOperation(original, null, null, new NpcServiceReply(NpcServiceTransactions.Done(original, storage, GameText.None)) { Warehouse = storage.View(original) });
        }

        if (command.Action is not (NpcServiceAction.Deposit or NpcServiceAction.Withdraw or NpcServiceAction.Move))
        {
            return NpcServiceTransactions.Final(original, NpcServiceOutcome.Failed(NpcServiceStatus.Rejected, GameText.Of(GameTextId.NpcNoSuchService), original.InventoryRevision));
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
            return NpcServiceTransactions.Final(original, NpcServiceOutcome.Failed(status, NpcServiceTransactions.Describe(status), original.InventoryRevision, storage.Revision));
        }

        candidate.RecordServiceOperation(command);
        var snapshot = CharacterRecordFactory.Capture(session.Loaded!, candidate, field.FieldId, player.Position);
        var reply = new NpcServiceReply(NpcServiceTransactions.Done(candidate, changed, GameText.None)) { Warehouse = changed.View(candidate) };
        return new PreparedNpcOperation(original, candidate, snapshot, reply, 0, changed, storage.Revision, CaravanStorage: true);
    }
}
