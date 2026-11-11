using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Markets;

namespace PristonTale.Persistence.Abstractions.Markets;

// Stored as character_market_listings.state; never renumbered.
public enum CharacterListingState : short
{
    Active = 0,
    Sold = 1,
    Cancelled = 2,
    Expired = 3,
    Removed = 4
}

// A listing with the character's facts as they were when it was made, so browsing never reads the characters table.
public sealed record CharacterListing(long Id, long? CharacterId, long SellerAccountId, string SellerName, bool SellerHidden, string Name, int JobCode, int Level, int Tier,
    int ExpBasisPoints, int FaceIndex, string ArmorKey, string WeaponKey, string OffHandKey, long Price, long Fee, CharacterListingState State, long CreatedAt, long ExpiresAt)
{
    // The character's hair now (pages read it from the characters table); 0 when it was deleted.
    public int HairIndex { get; init; }
}

public sealed record CharacterListingQuery(int Page, int JobCode, CharacterMarketSort Sort, string Search, long Now);

public sealed record CharacterListingPage(IReadOnlyList<CharacterListing> Listings, int Total);

// A new listing: the character by its id and the facts shown, all checked again in its transaction.
public sealed record CharacterListingDraft(Guid OperationId, long CharacterId, long SellerAccountId, string SellerName, bool SellerHidden, string Name, int JobCode, int Level,
    int Tier, int ExpBasisPoints, int FaceIndex, string ArmorKey, string WeaponKey, string OffHandKey, long Price, long Now);

public sealed record CharacterMarketPurchase(Guid OperationId, long ListingId, long BuyerAccountId, long ExpectedPrice, int TaxPercent, int MaxSlots, long Now);

// The status, and on success the coins the acting account holds and the listing touched.
public readonly record struct CharacterMarketStore(CharacterMarketStatus Status, long Balance = 0, long ListingId = 0);

public sealed record CharacterMarketSale(long Id, Guid OperationId, long ListingId, long? CharacterId, string CharacterName, long SellerAccountId, long BuyerAccountId, long Price,
    long Tax, long SellerReceived, long CreatedAt);

// Which of an account's characters cannot be listed now, by why.
public sealed record CharacterListingBlocks(IReadOnlySet<long> InClan, IReadOnlySet<long> WithStall, IReadOnlySet<long> Listed);

public interface ICharacterMarketRepository
{
    Task<CharacterListingPage> BrowseAsync(CharacterListingQuery query, CancellationToken ct = default);

    Task<CharacterListingPage> ListingsOfAsync(long accountId, int page, CancellationToken ct = default);

    Task<CharacterListing?> FindAsync(long listingId, CancellationToken ct = default);

    Task<CharacterRecord?> CharacterOfAsync(long characterId, CancellationToken ct = default);

    Task<CharacterListingBlocks> BlocksAsync(long accountId, long now, CancellationToken ct = default);

    // Whether the character has an unexpired listing: such a character cannot enter the world, be deleted or be moved.
    Task<bool> IsListedAsync(long characterId, long now, CancellationToken ct = default);

    // Checks ownership, level, clan, stall and the account's limit, takes the fee and stores the listing in one transaction.
    Task<CharacterMarketStore> ListAsync(CharacterListingDraft draft, CharacterMarketOptions options, CancellationToken ct = default);

    Task<CharacterMarketStore> CancelAsync(long listingId, long sellerAccountId, long now, CancellationToken ct = default);

    // In one transaction: the listing marked sold, the buyer debited, the seller credited less the tax, the character moved to the
    // buyer's first free slot, the sale and audit rows written. A repeated OperationId answers Ok without a second sale.
    Task<CharacterMarketStore> BuyAsync(CharacterMarketPurchase purchase, CancellationToken ct = default);

    // A game master takes a listing down; the fee stays spent.
    Task<bool> RemoveAsync(long listingId, long actorAccountId, string reason, long now, CancellationToken ct = default);

    // Closes every listing past its date; the number closed.
    Task<int> ExpireAsync(long now, CancellationToken ct = default);

    Task<IReadOnlyList<CharacterMarketSale>> RecentSalesAsync(int limit, CancellationToken ct = default);

    Task<IReadOnlyList<CharacterListing>> RecentListingsAsync(int limit, CancellationToken ct = default);
}
