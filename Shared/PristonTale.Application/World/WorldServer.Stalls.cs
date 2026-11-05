using PristonTale.Application.Sessions;
using PristonTale.Application.World.Stalls;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// A personal shop left as a stall: the listed items taken out of a copy of the seller's bag into the stall's stock, with the
// stall (Id 0 until stored) standing where the seller stands. Null Stall: no open shop to leave.
public sealed record PreparedStall(PlayerCharacter Original, PlayerCharacter? Candidate, CharacterSaveSnapshot? Snapshot, Stall? Stall)
    : PreparedOperation(Original, Candidate, Snapshot);

// A validated purchase from a stall: the buyer's change (the base record), the stock after it, and the owner's change when
// it plays (its candidate and snapshot), else the gold the stored owner row takes.
public sealed record PreparedStallPurchase(
    PlayerCharacter Original,
    PlayerCharacter? Candidate,
    CharacterSaveSnapshot? Snapshot,
    PersonalShopOutcome Outcome,
    Stall? Stall = null,
    PersonalShop? Shop = null,
    Inventory? Stock = null,
    PlayerCharacter? Owner = null,
    PlayerCharacter? OwnerCandidate = null,
    CharacterSaveSnapshot? OwnerSnapshot = null,
    long Price = 0,
    string ItemName = "")
    : PreparedOperation(Original, Candidate, Snapshot);

// Offline stalls in the fields: set up from an open personal shop, restored at start, viewed and bought from as a shop is,
// and closed by their account from anywhere.
public sealed partial class WorldServer
{
    public Task<PreparedStall?> PrepareStallAsync(PlayerSession session)
    {
        return EnqueueAsync<PreparedStall?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection))
            {
                return null;
            }

            var player = connection.Player;
            player.PruneShop();
            if (player.Shop is not { } shop || player.IsDead || player.Trade is not null)
            {
                return new PreparedStall(player.Character, null, null, null);
            }

            var candidate = player.Character.CloneForInventoryOperation();
            if (StallRules.TakeStock(candidate, shop) is not { } stock)
            {
                return new PreparedStall(player.Character, null, null, null);
            }

            var stall = new Stall(0, session.AccountId, session.Loaded.Id, connection.FieldId, player.Position, player.Facing, player.Appearance,
                player.Level, player.Character.Tier);
            stall.SetStock(shop, stock);
            return new PreparedStall(player.Character, candidate,
                CharacterRecordFactory.Capture(session.Loaded, candidate, connection.FieldId, player.Position), stall);
        });
    }

    // Exempt from the quota: the stall is stored. The seller's bag loses the listed items, its shop closes and the stall
    // stands in the field under its stored id.
    public Task<bool> CommitStallAsync(PlayerSession session, PreparedStall prepared, long stallId)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            var candidate = CheckReceived(session, prepared, "setting up a stall");
            var draft = prepared.Stall!;
            prepared.Original.CommitInventoryOperation(candidate);
            if (TryGetOwnConnection(session, out var connection))
            {
                connection.Player.CloseShop();
            }

            AddStall(new Stall(stallId, draft.AccountId, draft.OwnerCharacterId, draft.FieldId, draft.Position, draft.Facing, draft.Look, draft.Level,
                draft.Tier), draft.Shop, draft.Stock);
            return true;
        }, waitForCapacity: true);
    }

    public Task RestoreStallAsync(Stall stall, PersonalShop shop, Inventory stock) =>
        EnqueueAsync(NoOwner, () =>
        {
            AddStall(stall, shop, stock);
            return true;
        }, waitForCapacity: true);

    // Takes the account's stall out of its field; false when it has none standing.
    public Task<bool> RemoveStallAsync(long accountId) =>
        EnqueueAsync(NoOwner, () =>
        {
            foreach (var field in _fields.Values)
            {
                if (field.RemoveStallOf(accountId))
                {
                    return true;
                }
            }

            return false;
        }, waitForCapacity: true);

    // The owner of a stall in the viewer's field: its character and account, or null.
    public Task<(long CharacterId, long AccountId)?> StallOwnerAsync(PlayerSession viewer, ulong stallEntityId) =>
        EnqueueAsync(viewer.EntityId, () =>
            TryGetPlaying(viewer, out var connection) && _fields[connection.FieldId].TryGetStall(stallEntityId, out var stall)
                ? (stall.OwnerCharacterId, stall.AccountId)
                : ((long, long)?)null);

    public Task<PreparedStallPurchase?> PrepareStallPurchaseAsync(PlayerSession buyer, PlayerSession? owner, PersonalShopPurchase purchase)
    {
        return EnqueueAsync<PreparedStallPurchase?>(buyer.EntityId, () =>
        {
            if (buyer.Loaded is null || !TryGetPlaying(buyer, out var connection))
            {
                return null;
            }

            var player = connection.Player;
            var original = player.Character;
            var revision = original.InventoryRevision;
            if (original.ShopPurchases.Find(purchase.OperationId) is { } applied)
            {
                return applied.Purchase == purchase
                    ? new PreparedStallPurchase(original, null, null, new PersonalShopOutcome(true, PersonalShopStatus.Ok, applied.Revision, "already committed"))
                    : StallRefused(original, PersonalShopStatus.Conflict);
            }

            if (!_fields[connection.FieldId].TryGetStall(purchase.SellerId, out var stall))
            {
                return StallRefused(original, PersonalShopStatus.Closed);
            }

            if (player.IsDead || !NpcInteraction.WithinReach(player.Position, stall.Position))
            {
                return StallRefused(original, PersonalShopStatus.OutOfReach);
            }

            if (player.Shop is not null)
            {
                return StallRefused(original, PersonalShopStatus.Selling);
            }

            if (purchase.OperationId == Guid.Empty || purchase.ExpectedRevision != revision)
            {
                return StallRefused(original, PersonalShopStatus.Stale);
            }

            // A playing owner takes the gold into its live character; one still entering the world cannot be paid yet.
            WorldPlayer? ownerPlayer = null;
            if (owner is not null)
            {
                if (owner.Loaded is null || !TryGetPlaying(owner, out var ownerConnection))
                {
                    return StallRefused(original, PersonalShopStatus.Busy);
                }

                ownerPlayer = ownerConnection.Player;
            }

            var candidate = original.CloneForInventoryOperation();
            var stock = StoreGrid.Create();
            stock.CopyFrom(stall.Stock);
            var anchor = stall.Stock.Find(purchase.ItemId);
            var item = anchor >= 0 ? stall.Stock[anchor] : null;
            var ownerMoney = ownerPlayer?.Character.Money ?? 0;
            var status = StallRules.TrySell(stall.Shop, stock, candidate, purchase, ownerPlayer?.Level ?? stall.Level, ownerPlayer?.Character.Tier ?? stall.Tier,
                ownerMoney, out var price);
            if (status != PersonalShopStatus.Ok)
            {
                return new PreparedStallPurchase(original, null, null,
                    PersonalShopOutcome.Failed(status, revision, stall.Shop.Find(purchase.ItemId)?.UnitPrice ?? 0, item?.Count ?? 0));
            }

            candidate.RecordShopPurchase(purchase);
            PlayerCharacter? ownerCandidate = null;
            CharacterSaveSnapshot? ownerSnapshot = null;
            if (ownerPlayer is not null && owner?.Loaded is { } ownerLoaded && TryGetPlaying(owner, out var paid))
            {
                ownerCandidate = ownerPlayer.Character.CloneForInventoryOperation();
                ownerCandidate.TryChangeMoney(price);
                ownerSnapshot = CharacterRecordFactory.Capture(ownerLoaded, ownerCandidate, paid.FieldId, ownerPlayer.Position);
            }

            return new PreparedStallPurchase(original, candidate, CharacterRecordFactory.Capture(buyer.Loaded, candidate, connection.FieldId, player.Position),
                new PersonalShopOutcome(true, PersonalShopStatus.Ok, candidate.InventoryRevision, ""), stall, StallRules.Remaining(stall.Shop, stock), stock,
                ownerPlayer?.Character, ownerCandidate, ownerSnapshot, price, item!.DisplayName);
        });
    }

    // Exempt from the quota: the sale is stored. The reply carries the stall as the buyer sees it now (none once sold out).
    public Task<PersonalShopReply> CommitStallPurchaseAsync(PlayerSession buyer, PreparedStallPurchase prepared)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (!ReferenceEquals(buyer.Character, prepared.Original) || prepared.Candidate is null || prepared.Stall is not { } stall
                || !prepared.Original.CanChangeMoney(-prepared.Price))
            {
                throw new CommitInvariantException("Character ownership changed during a stall purchase.");
            }

            if (Transfer(Persistence.Abstractions.GameLogs.ItemTransferKind.PersonalShop, Guid.NewGuid(), stall.FieldId) is { } log)
            {
                log.Arrived(prepared.Original, prepared.Candidate, stall.OwnerCharacterId, CharacterIdOf(buyer));
                log.Gold(CharacterIdOf(buyer), stall.OwnerCharacterId, prepared.Price);
                Log(log);
            }

            prepared.Original.CommitInventoryOperation(prepared.Candidate);
            prepared.Original.TryChangeMoney(-prepared.Price);
            if (prepared.Owner is { } owner && prepared.OwnerCandidate is not null)
            {
                owner.TryChangeMoney(prepared.Price);
            }

            stall.SetStock(prepared.Shop!, prepared.Stock!);
            if (stall.Shop.IsEmpty)
            {
                _fields[stall.FieldId].RemoveStall(stall.EntityId);
                return new PersonalShopReply(prepared.Outcome);
            }

            return new PersonalShopReply(prepared.Outcome, TryGetOwnConnection(buyer, out var connection) ? stall.Listing(connection.Player.Character) : null);
        }, waitForCapacity: true);
    }

    private void AddStall(Stall stall, PersonalShop shop, Inventory stock)
    {
        stall.SetStock(shop, stock);
        if (_fields.TryGetValue(stall.FieldId, out var field))
        {
            field.AddStall(stall);
        }
    }

    // A stall in the viewer's field, viewed as a shop is.
    private bool TryViewStall(Connection viewer, ulong stallEntityId, out PersonalShopReply reply)
    {
        reply = null!;
        if (!Stall.IsStallId(stallEntityId) || !_fields[viewer.FieldId].TryGetStall(stallEntityId, out var stall))
        {
            return false;
        }

        var revision = viewer.Player.Character.InventoryRevision;
        reply = !viewer.Player.IsDead && NpcInteraction.WithinReach(viewer.Player.Position, stall.Position)
            ? new PersonalShopReply(new PersonalShopOutcome(true, PersonalShopStatus.Ok, revision, ""), stall.Listing(viewer.Player.Character))
            : new PersonalShopReply(PersonalShopOutcome.Failed(PersonalShopStatus.OutOfReach, revision));
        return true;
    }

    private static PreparedStallPurchase StallRefused(PlayerCharacter original, PersonalShopStatus status) =>
        new(original, null, null, PersonalShopOutcome.Failed(status, original.InventoryRevision));
}
