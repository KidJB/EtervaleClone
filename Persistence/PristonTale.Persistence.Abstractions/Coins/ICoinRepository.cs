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
}
