namespace PristonTale.Simulation.Markets;

// What a Character Market request came to; carried on the wire, so values are never renumbered.
public enum CharacterMarketStatus : byte
{
    Ok = 0,
    Disabled = 1,
    OutOfReach = 2,
    NotFound = 3,
    NotOwner = 4,
    LevelTooLow = 5,
    PriceOutOfRange = 6,
    TooManyListings = 7,
    AlreadyListed = 8,
    InPlay = 9,
    InClan = 10,
    HasStall = 11,
    NotEnoughCoins = 12,
    NoFreeSlot = 13,
    OwnListing = 14,
    PriceChanged = 15,
    Busy = 16,
    StorageFailed = 17,
    Rejected = 18
}

public enum CharacterMarketSort : byte
{
    Newest = 0,
    PriceLow = 1,
    PriceHigh = 2,
    LevelHigh = 3,
    Name = 4
}

// The market's rules, read from the server settings table.
public sealed record CharacterMarketOptions
{
    public static readonly CharacterMarketOptions Default = new();

    public bool Enabled { get; init; } = true;
    public int MinLevel { get; init; } = 50;
    public int MaxListingsPerAccount { get; init; } = 3;
    public long MinPrice { get; init; } = 100;
    public long MaxPrice { get; init; } = 1_000_000;
    public long ListingFee { get; init; }
    public int ExpiryDays { get; init; } = 14;
    public int TaxPercent { get; init; } = 5;
}

public static class CharacterMarketRules
{
    public const int PageSize = 8;
    public const int MaxSearchLength = 16;
    public const int SecondsPerDay = 86_400;
    private const int PercentScale = 100;
    private const ulong BasisPoints = 10_000;

    public static bool PriceAllowed(CharacterMarketOptions options, long price) => price >= options.MinPrice && price <= options.MaxPrice;

    // The tax rounds up, as the coin trade fee does.
    public static long Tax(long price, int percent) => (price * percent + PercentScale - 1) / PercentScale;

    public static long ExpiresAt(long now, int days) => now + days * (long)SecondsPerDay;

    // The share of the level's experience earned, in hundredths of a percent.
    public static int ExperienceBasisPoints(ulong experience, ulong levelStart, ulong nextLevel)
    {
        if (nextLevel <= levelStart || experience <= levelStart)
        {
            return 0;
        }

        return (int)Math.Min(BasisPoints, (experience - levelStart) / Math.Max(1, (nextLevel - levelStart) / BasisPoints));
    }
}
