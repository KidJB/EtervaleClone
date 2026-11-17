using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Persistence.Abstractions.Coins;

// The coins an account holds (accounts.coins) and the log of every change (coin_log).
public interface ICoinRepository
{
    Task<long> BalanceAsync(long accountId, CancellationToken ct = default);

    // Adds (or with a negative amount takes, never below zero) coins with a log line; the new balance, or null when the
    // account is unknown or holds too few.
    Task<long?> AddAsync(long accountId, long amount, string reason, CancellationToken ct = default);

    // A purchase: takes the price and writes the character (holding what was bought) in one transaction, with a log line.
    // The new balance, or null when the account holds too few coins or the character row was not written.
    Task<long?> BuyWithCharacterAsync(CharacterRecord character, long price, string reason, CancellationToken ct = default);

    // The skins (item keys) an account owns.
    Task<IReadOnlyList<string>> OwnedSkinsAsync(long accountId, CancellationToken ct = default);

    // A skin purchase: takes the price and records the skin in one transaction; the new balance, or null when the account
    // holds too few coins or owns the skin already.
    Task<long?> BuySkinAsync(long accountId, string itemKey, long price, string reason, long unlockedAt = 0, CancellationToken ct = default);

    // The skins an account owns with how each was unlocked.
    Task<IReadOnlyList<OwnedSkinRecord>> SkinRecordsAsync(long accountId, CancellationToken ct = default);

    // A skin given to the account (a quest, an event, a deal, a game master); false when the account owns it already.
    Task<bool> UnlockSkinAsync(OwnedSkinRecord skin, CancellationToken ct = default);

    // A skin unlocked from an item the character gave up (a brush and its gear, a token): records the skin and writes the
    // character in one transaction. Owned when the account holds the skin already, Failed when the character row was not
    // written; nothing changes then.
    Task<SkinUnlockStore> UnlockSkinWithCharacterAsync(CharacterRecord character, OwnedSkinRecord skin, CancellationToken ct = default);

    // Takes every skin from the account (a game master's reset); the number taken.
    Task<int> ResetSkinsAsync(long accountId, CancellationToken ct = default);

    // A trade: writes the characters and moves each transfer's coins (the giver loses Amount, the receiver gains Received)
    // in one transaction, with log lines. NotEnoughCoins when a giver holds too few, Failed when a character row was not
    // written; nothing changes then.
    Task<CoinTradeStore> TradeWithCharactersAsync(IReadOnlyList<CharacterRecord> characters, IReadOnlyList<CoinTransfer> transfers,
        CancellationToken ct = default);
}

// Source is a Simulation.Characters.CosmeticSource; CharacterId the character that unlocked it (0: none); Bound: kept by the
// account, never traded.
public sealed record OwnedSkinRecord(long AccountId, string ItemKey, long UnlockedAt, short Source, long CharacterId, bool Bound = true);

public enum SkinUnlockStore
{
    Stored,
    Owned,
    Failed
}

public readonly record struct CoinTransfer(long FromAccountId, long ToAccountId, long Amount, long Received, string Reason);

public enum CoinTradeStore
{
    Stored,
    NotEnoughCoins,
    Failed
}
