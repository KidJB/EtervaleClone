namespace PristonTale.Persistence.Abstractions.Cabinet;

// A bidding deal of The Cabinet: one look offered for a time to the highest coin bid. TopAccountId 0: no bid yet. State 0
// open, 1 closed.
public sealed record CabinetDealRecord(long Id, string ItemKey, long StartsAt, long EndsAt, long StartBid, long Increment, int MinLevel, long TopBid,
    long TopAccountId, string TopName, int Bids, short State, string CreatedBy, long ClosedAt)
{
    public const short Open = 0;
    public const short Closed = 1;

    // The least the next bid may be.
    public long NextBid => TopAccountId == 0 ? StartBid : TopBid + Increment;
}

public sealed record CabinetBidRecord(long DealId, long AccountId, string Name, long Amount, long PlacedAt);

public sealed record CabinetDealDraft(string ItemKey, long StartsAt, long EndsAt, long StartBid, long Increment, int MinLevel, string CreatedBy);

public enum CabinetBidStore
{
    Placed,
    Unknown,
    Ended,
    TooLow,
    Leading,
    NotEnoughCoins
}

// A closed deal: Awarded when its winner received the look, Refunded when the winner owned it by then and got the coins back.
public sealed record CabinetDealClose(CabinetDealRecord Deal, bool Awarded, bool Refunded);

// The deals and their bids. A bid holds the bidder's coins at once and gives the bid it beat its coins back, in one
// transaction; closing a deal gives its look to the leader (CosmeticSource.Deal), so its coins are already paid.
public interface ICabinetDealRepository
{
    Task<long> CreateAsync(CabinetDealDraft draft, CancellationToken ct = default);

    Task<IReadOnlyList<CabinetDealRecord>> OpenAsync(CancellationToken ct = default);

    Task<CabinetDealRecord?> GetAsync(long dealId, CancellationToken ct = default);

    // The deal's latest bids, highest first.
    Task<IReadOnlyList<CabinetBidRecord>> BidsAsync(long dealId, int limit, CancellationToken ct = default);

    // The status and the bidder's balance after it.
    Task<(CabinetBidStore Status, long Balance)> BidAsync(long dealId, long accountId, string name, long amount, long now, CancellationToken ct = default);

    // Closes every open deal whose time is over.
    Task<IReadOnlyList<CabinetDealClose>> CloseDueAsync(long now, CancellationToken ct = default);

    // Closes an open deal at once (a game master's end); null when it is not open.
    Task<CabinetDealClose?> EndAsync(long dealId, long now, CancellationToken ct = default);
}
