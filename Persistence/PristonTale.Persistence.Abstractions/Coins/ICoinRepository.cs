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
    // holds too few coins.
    Task<long?> BuySkinAsync(long accountId, string itemKey, long price, string reason, CancellationToken ct = default);

    // A trade: writes the characters and moves each transfer's coins (the giver loses Amount, the receiver gains Received)
    // in one transaction, with log lines. NotEnoughCoins when a giver holds too few, Failed when a character row was not
    // written; nothing changes then.
    Task<CoinTradeStore> TradeWithCharactersAsync(IReadOnlyList<CharacterRecord> characters, IReadOnlyList<CoinTransfer> transfers,
        CancellationToken ct = default);
}

public readonly record struct CoinTransfer(long FromAccountId, long ToAccountId, long Amount, long Received, string Reason);

public enum CoinTradeStore
{
    Stored,
    NotEnoughCoins,
    Failed
}
