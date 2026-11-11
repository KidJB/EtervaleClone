using PristonTale.Application.Chat;
using PristonTale.Application.Saves;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Contracts.Social;
using PristonTale.Persistence.Abstractions.Characters;
using System.Collections.Concurrent;
using PristonTale.Persistence.Abstractions.Coins;
using PristonTale.Simulation.PersonalShops;

namespace PristonTale.Application.Services;

// Personal shops outside the tick. A purchase holds the buyer's save gate and then the seller's (neither waits, so two
// players buying from each other never block), stores both characters in one transaction (with the coins moved between the
// accounts for a coin offer) and commits both on the tick. Opening and closing hold the seller's gate too, so a listing never
// changes between a purchase's prepare and commit. A buyer waits the cooldown after a purchase; a seller hears who looked.
public sealed class PersonalShopAppService(ISessionRegistry sessions, ICharacterSaves saves, ICharacterRepository characters, WorldServer world,
    ChatDirectory? chat = null, StallAppService? stalls = null, ICoinRepository? coins = null, Markets.MarketOptionsHolder? options = null)
{
    // A seller hears of the same visitor once in this long.
    private const long VisitNoticeMs = 60_000;

    private readonly DurableOperations _durable = new(saves);
    private readonly ConcurrentDictionary<ulong, long> _boughtAt = new();
    private readonly ConcurrentDictionary<(ulong Viewer, ulong Seller), long> _visitedAt = new();

    // Milliseconds of a steady clock; tests set their own.
    public Func<long> NowMs { get; init; } = static () => Environment.TickCount64;

    public async Task<PersonalShopOutcome> OpenAsync(string sessionToken, string sign, IReadOnlyList<PersonalShopEntry> entries, CancellationToken ct = default)
    {
        using var lease = await _durable.AdmitAsync(sessions, sessionToken, ct);
        return lease.Failure is { } refused ? Failed(refused) : await world.OpenPersonalShopAsync(lease.Session, sign, entries);
    }

    public async Task<PersonalShopOutcome> CloseAsync(string sessionToken, CancellationToken ct = default)
    {
        using var lease = await _durable.AdmitAsync(sessions, sessionToken, ct);
        return lease.Failure is { } refused ? Failed(refused) : await world.ClosePersonalShopAsync(lease.Session);
    }

    public async Task<PersonalShopReply> ViewAsync(string sessionToken, ulong sellerId)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return new PersonalShopReply(Rejected());
        }

        var reply = await world.ViewPersonalShopAsync(session, sellerId);
        if (reply.Listing is not null && sellerId != session.EntityId)
        {
            NoticeVisit(session, sellerId);
        }

        return reply;
    }

    private void NoticeVisit(PlayerSession viewer, ulong sellerId)
    {
        var now = NowMs();
        var key = (viewer.EntityId, sellerId);
        if (chat is null || _visitedAt.TryGetValue(key, out var last) && now - last < VisitNoticeMs || !chat.TryGet(sellerId, out var seller))
        {
            return;
        }

        _visitedAt[key] = now;
        seller.Link.Deliver(new ChatLine(ChatKind.Trade, 0, PersonalShopMessages.Visited(viewer.CharacterName ?? "")));
    }

    // marketNpc, when not 0, is the Marketplace NPC the buyer stands at: the seller may then be anywhere.
    public async Task<PersonalShopReply> BuyAsync(string sessionToken, PersonalShopPurchase purchase, CancellationToken ct = default, ulong marketNpc = 0)
    {
        using var lease = await _durable.AdmitAsync(sessions, sessionToken, ct);
        if (lease.Failure is { } refused)
        {
            return new PersonalShopReply(Failed(refused));
        }

        var buyer = lease.Session;
        var cooldown = options?.PurchaseCooldownMs ?? 0;
        if (cooldown > 0 && _boughtAt.TryGetValue(buyer.EntityId, out var boughtAt) && NowMs() - boughtAt < cooldown)
        {
            return new PersonalShopReply(PersonalShopOutcome.Failed(PersonalShopStatus.TooSoon, 0));
        }

        if (stalls is not null && PristonTale.Application.World.Stalls.Stall.IsStallId(purchase.SellerId))
        {
            var bought = await stalls.BuyAsync(buyer, lease, purchase, ct, marketNpc);
            Bought(buyer, bought.Outcome);
            return bought;
        }

        var seller = purchase.SellerId == buyer.EntityId ? null : await world.PersonalShopSellerAsync(buyer, purchase.SellerId, marketNpc);
        using var sellerLease = seller is null ? null : await _durable.AdmitAsync(seller, TimeSpan.Zero, ct);
        if (sellerLease?.Failure is not null)
        {
            // A retried purchase still answers its first outcome while the seller is busy.
            var replay = await world.PreparePersonalShopPurchaseAsync(buyer, null, purchase, marketNpc);
            return new PersonalShopReply(replay?.Outcome is { Status: PersonalShopStatus.Ok or PersonalShopStatus.Conflict } outcome
                ? outcome
                : Failed(DurableFailure.Busy));
        }

        PersonalShopListing? listing = null;
        var sold = false;
        var coinsShort = false;
        var reply = await lease.RunAsync(
            session => world.PreparePersonalShopPurchaseAsync(session, seller, purchase, marketNpc),
            async (session, prepared) =>
            {
                listing = (await world.CommitPersonalShopPurchaseAsync(session, seller!, prepared)).Listing;
                sold = true;
                Announce(buyer, seller!, prepared, purchase.Count);
            },
            prepared => new PersonalShopReply(prepared.Outcome, listing),
            failure => new PersonalShopReply(coinsShort ? PersonalShopOutcome.Failed(PersonalShopStatus.NotEnoughCoins, 0) : Failed(failure)),
            async (session, prepared) =>
            {
                CharacterRecord[] both = [session.Stamp(prepared.Snapshot!), seller!.Stamp(prepared.SellerSnapshot!)];
                if (prepared.Currency != PersonalShopCurrency.Coins)
                {
                    return await characters.UpdateAllAsync(both, CancellationToken.None) ? null : DurableFailure.StorageFailed;
                }

                if (coins is null)
                {
                    return DurableFailure.StorageFailed;
                }

                var transfer = new CoinTransfer(session.AccountId, seller.AccountId, prepared.Price, prepared.Price, $"personal shop: {prepared.ItemName}");
                var stored = await coins.TradeWithCharactersAsync(both, [transfer], CancellationToken.None);
                coinsShort = stored == CoinTradeStore.NotEnoughCoins;
                return stored == CoinTradeStore.Stored ? null : DurableFailure.StorageFailed;
            });
        if (sold)
        {
            Bought(buyer, reply.Outcome);
        }

        if (sold || reply.Outcome.Status is not (PersonalShopStatus.SoldOut or PersonalShopStatus.CountChanged or PersonalShopStatus.PriceChanged))
        {
            return reply;
        }

        // What the shop offers now, so the buyer's window shows the change it met.
        return marketNpc != 0 ? reply : reply with { Listing = (await world.ViewPersonalShopAsync(buyer, purchase.SellerId)).Listing };
    }

    private void Bought(PlayerSession buyer, PersonalShopOutcome outcome)
    {
        if (outcome.Ok)
        {
            _boughtAt[buyer.EntityId] = NowMs();
        }
    }

    private void Announce(PlayerSession buyer, PlayerSession seller, PreparedShopPurchase prepared, int count)
    {
        var line = new ChatLine(ChatKind.Trade, 0, "", PersonalShopMessages.Sold(prepared.Original.Name, count, prepared.ItemName, prepared.Price));
        foreach (var id in (ReadOnlySpan<ulong>)[buyer.EntityId, seller.EntityId])
        {
            if (chat is not null && chat.TryGet(id, out var member))
            {
                member.Link.Deliver(line);
            }
        }
    }

    private static PersonalShopOutcome Rejected() => PersonalShopOutcome.Failed(PersonalShopStatus.Rejected, 0);

    private static PersonalShopOutcome Failed(DurableFailure failure) => PersonalShopOutcome.Failed(failure switch
    {
        DurableFailure.Busy => PersonalShopStatus.Busy,
        DurableFailure.StorageFailed => PersonalShopStatus.StorageFailed,
        _ => PersonalShopStatus.Rejected
    }, 0);
}
