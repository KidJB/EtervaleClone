using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Cabinet;
using PristonTale.Persistence.Abstractions.Coins;
using PristonTale.Simulation.Characters;

namespace PristonTale.Application.Services;

// A deal as a player sees it: the look, the leading bid, the least next bid, whether the player leads and the latest bids.
public sealed record CabinetDealView(CabinetDealRecord Deal, CabinetLook? Look, bool Leading, IReadOnlyList<CabinetBidRecord> History);

// The Cabinet's bidding deals: a game master offers a look for a time, players bid coins, and the highest bid when the time
// ends wins the look. Every rule that money depends on is the repository's, in one transaction (a bid holds its coins and
// gives the beaten bid its coins back; a bid that lost a race changes nothing); here are the player's own checks: the look
// not owned already and the deal's level reached.
public sealed class CabinetDealAppService(ISessionRegistry sessions, ICabinetDealRepository deals, ICoinRepository coins, WorldServer world, WorldDataHolder worldData)
{
    public const int HistoryShown = 5;
    public const int MaxDurationHours = 24 * 14;

    public async Task<(IReadOnlyList<CabinetDealView> Deals, long Balance)> ListAsync(string sessionToken, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return ([], 0);
        }

        var views = new List<CabinetDealView>();
        var now = world.UtcNow.ToUnixTimeSeconds();
        foreach (var deal in await deals.OpenAsync(ct))
        {
            if (deal.StartsAt <= now && deal.EndsAt > now)
            {
                views.Add(new CabinetDealView(deal, CabinetLooks.Find(deal.ItemKey, worldData.Current.Items, world.Cabinet), deal.TopAccountId == session.AccountId,
                    await deals.BidsAsync(deal.Id, HistoryShown, ct)));
            }
        }

        return (views, await coins.BalanceAsync(session.AccountId, ct));
    }

    public async Task<(CabinetBidStatus Status, long Balance)> BidAsync(string sessionToken, long dealId, long amount, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session) || session.Loaded is not { } character)
        {
            return (CabinetBidStatus.Rejected, 0);
        }

        var balance = await coins.BalanceAsync(session.AccountId, ct);
        if (await deals.GetAsync(dealId, ct) is not { } deal)
        {
            return (CabinetBidStatus.Unknown, balance);
        }

        if ((await coins.OwnedSkinsAsync(session.AccountId, ct)).Contains(deal.ItemKey, StringComparer.OrdinalIgnoreCase))
        {
            return (CabinetBidStatus.AlreadyOwned, balance);
        }

        if (character.Level < deal.MinLevel)
        {
            return (CabinetBidStatus.LevelTooLow, balance);
        }

        var (stored, left) = await deals.BidAsync(dealId, session.AccountId, character.Name, amount, world.UtcNow.ToUnixTimeSeconds(), ct);
        return (stored switch
        {
            CabinetBidStore.Placed => CabinetBidStatus.Placed,
            CabinetBidStore.Ended => CabinetBidStatus.Ended,
            CabinetBidStore.TooLow => CabinetBidStatus.TooLow,
            CabinetBidStore.Leading => CabinetBidStatus.Leading,
            CabinetBidStore.NotEnoughCoins => CabinetBidStatus.NotEnoughCoins,
            _ => CabinetBidStatus.Unknown
        }, left);
    }

    // A game master's new deal; the reason it was refused, or null with its id.
    public async Task<(long Id, string? Problem)> CreateAsync(string key, long startBid, long increment, double hours, int minLevel, string createdBy,
        CancellationToken ct = default)
    {
        if (CabinetLooks.Find(key, worldData.Current.Items, world.Cabinet) is not { } look)
        {
            return (0, $"'{key}' is no look of The Cabinet");
        }

        if (startBid < 1 || increment < 1 || hours <= 0 || hours > MaxDurationHours || minLevel < 0)
        {
            return (0, $"a deal needs a start bid and step of at least 1 coin and a time of up to {MaxDurationHours} hours");
        }

        var now = world.UtcNow.ToUnixTimeSeconds();
        var id = await deals.CreateAsync(new CabinetDealDraft(look.Key, now, now + (long)(hours * 3600), startBid, increment, minLevel, createdBy), ct);
        return (id, null);
    }

    public Task<CabinetDealClose?> EndAsync(long dealId, CancellationToken ct = default) => deals.EndAsync(dealId, world.UtcNow.ToUnixTimeSeconds(), ct);

    public Task<IReadOnlyList<CabinetDealClose>> CloseDueAsync(CancellationToken ct = default) => deals.CloseDueAsync(world.UtcNow.ToUnixTimeSeconds(), ct);

    public Task<IReadOnlyList<CabinetDealRecord>> OpenAsync(CancellationToken ct = default) => deals.OpenAsync(ct);
}
