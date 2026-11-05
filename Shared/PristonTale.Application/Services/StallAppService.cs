using System.Numerics;
using PristonTale.Application.Chat;
using PristonTale.Application.Saves;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Application.World.Stalls;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.Social;
using PristonTale.Persistence.Abstractions.Stalls;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.Services;

// Offline stalls outside the tick. Setting one up stores the seller's character (without the listed items) and the stall
// in one transaction before the commit; closing removes the stored stall and puts its unsold items in the account's Item
// Distributor in one transaction, then takes it out of the world. A purchase holds the buyer's save gate (and a playing
// owner's, without waiting), stores the buyer, the stall's stock and the owner's gold together, then commits.
public sealed class StallAppService(ISessionRegistry sessions, ICharacterSaves saves, IStallRepository stalls, WorldServer world, WorldDataHolder worldData,
    ChatDirectory? chat = null)
{
    private readonly DurableOperations _durable = new(saves);

    // The "/stall" command: leaves the open personal shop as a stall. The answer is the line the player reads.
    public async Task<string> PlaceAsync(PlayerSession session, CancellationToken ct = default)
    {
        var already = false;
        long stallId = 0;
        using var lease = await _durable.AdmitAsync(session, TimeSpan.Zero, ct);
        var placed = await lease.RunAsync(
            leased => world.PrepareStallAsync(leased),
            async (leased, prepared) => await world.CommitStallAsync(leased, prepared, stallId),
            static prepared => prepared.Stall is not null,
            static _ => false,
            async (leased, prepared) =>
            {
                var draft = prepared.Stall!;
                var record = new StallRecord(0, draft.AccountId, draft.OwnerCharacterId, draft.FieldId, draft.Position.X, draft.Position.Y, draft.Position.Z,
                    draft.Facing, StallCodec.CaptureLook(LookOf(draft)), StallCodec.CaptureItems(draft.Shop, draft.Stock));
                if (await stalls.CreateWithCharacterAsync(leased.Stamp(prepared.Snapshot!), record, CancellationToken.None) is not { } id)
                {
                    already = true;
                    return DurableFailure.Conflict;
                }

                stallId = id;
                return null;
            },
            storeFirst: static prepared => prepared.Stall is not null);
        return placed ? StallMessages.Placed : already ? StallMessages.AlreadyOne : lease.Failure is null ? StallMessages.NoShop : StallMessages.Failed;
    }

    // The "/stall close" command, from anywhere.
    public async Task<string> CloseAsync(PlayerSession session, CancellationToken ct = default)
    {
        if (await stalls.CloseAsync(session.AccountId, "Your stall", ct) is null)
        {
            return StallMessages.NoStall;
        }

        await world.RemoveStallAsync(session.AccountId);
        return StallMessages.Closed;
    }

    // The stored stalls stand in their fields again, at start.
    public async Task RestoreAsync(CancellationToken ct = default)
    {
        foreach (var record in await stalls.ListAsync(ct))
        {
            var look = StallCodec.RestoreLook(record.Look);
            var (entries, stock) = StallCodec.RestoreItems(record.Items, worldData.Current.Items);
            var appearance = new PlayerAppearanceView(look.Name, (PlayerClass)look.Class, look.Tier, look.Face, look.Hair, look.Model, look.Model2)
            {
                WeaponKey = look.Weapon,
                ArmorKey = look.Armor,
                OffHandKey = look.OffHand
            };
            var stall = new Stall(record.Id, record.AccountId, record.CharacterId, record.FieldId, new Vector3((float)record.X, (float)record.Y, (float)record.Z),
                (float)record.Facing, appearance, look.Level, look.Tier);
            await world.RestoreStallAsync(stall, StallRules.Listing(look.Sign, entries), stock);
        }
    }

    public async Task<PersonalShopReply> BuyAsync(PlayerSession buyer, DurableLease lease, PersonalShopPurchase purchase, CancellationToken ct = default)
    {
        if (await world.StallOwnerAsync(buyer, purchase.SellerId) is not { } owner)
        {
            return new PersonalShopReply(PersonalShopOutcome.Failed(PersonalShopStatus.Closed, 0));
        }

        PlayerSession? ownerSession = sessions.TryGetCharacterSession(owner.CharacterId, out var playing) ? playing : null;
        if (owner.AccountId == buyer.AccountId)
        {
            return new PersonalShopReply(new PersonalShopOutcome(false, PersonalShopStatus.Rejected, 0, StallMessages.OwnStall));
        }

        using var ownerLease = ownerSession is null ? null : await _durable.AdmitAsync(ownerSession, TimeSpan.Zero, ct);
        if (ownerLease?.Failure is not null)
        {
            return new PersonalShopReply(PersonalShopOutcome.Failed(PersonalShopStatus.Busy, 0));
        }

        PersonalShopListing? listing = null;
        var refused = PersonalShopStatus.StorageFailed;
        var reply = await lease.RunAsync(
            session => world.PrepareStallPurchaseAsync(session, ownerSession, purchase),
            async (session, prepared) =>
            {
                listing = (await world.CommitStallPurchaseAsync(session, prepared)).Listing;
                Announce(buyer, ownerSession, prepared, purchase.Count);
            },
            prepared => new PersonalShopReply(prepared.Outcome, listing),
            failure => new PersonalShopReply(PersonalShopOutcome.Failed(failure == DurableFailure.Busy ? PersonalShopStatus.Busy : refused, 0)),
            async (session, prepared) =>
            {
                var stall = prepared.Stall!;
                var sale = new StallSale(session.Stamp(prepared.Snapshot!), stall.Id, StallCodec.CaptureItems(prepared.Shop!, prepared.Stock!), prepared.Shop!.IsEmpty,
                    stall.OwnerCharacterId, prepared.Price, MoneyLimits.CarryLimit(stall.Level, stall.Tier),
                    prepared.OwnerSnapshot is { } snapshot ? ownerSession!.Stamp(snapshot) : null);
                var stored = await stalls.SellAsync(sale, CancellationToken.None);
                refused = stored switch
                {
                    StallSaleStore.Gone => PersonalShopStatus.Closed,
                    StallSaleStore.GoldLimit => PersonalShopStatus.GoldLimit,
                    _ => PersonalShopStatus.StorageFailed
                };
                return stored == StallSaleStore.Stored ? null : DurableFailure.StorageFailed;
            });
        return reply;
    }

    private void Announce(PlayerSession buyer, PlayerSession? owner, PreparedStallPurchase prepared, int count)
    {
        if (chat is null)
        {
            return;
        }

        if (chat.TryGet(buyer.EntityId, out var member))
        {
            member.Link.Deliver(new ChatLine(ChatKind.Trade, 0, PersonalShopMessages.Sold(prepared.Original.Name, count, prepared.ItemName, prepared.Price)));
        }

        if (owner is not null && chat.TryGet(owner.EntityId, out var seller))
        {
            seller.Link.Deliver(new ChatLine(ChatKind.Trade, 0, StallMessages.Sold(prepared.Original.Name, count, prepared.ItemName, prepared.Price)));
        }
    }

    private static StallLook LookOf(Stall stall) =>
        new(stall.Look.Name ?? "", (int)stall.Look.Class, stall.Look.Tier, stall.Look.FaceIndex, stall.Look.HairIndex, stall.Look.ModelName ?? "",
            stall.Look.ModelName2 ?? "", stall.Look.WeaponKey, stall.Look.ArmorKey, stall.Look.OffHandKey, stall.Shop.Sign, stall.Level);
}
