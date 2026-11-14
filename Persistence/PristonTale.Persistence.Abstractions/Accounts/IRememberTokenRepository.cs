namespace PristonTale.Persistence.Abstractions.Accounts;

// Stored "remember me" tokens by their hash; times are Unix milliseconds.
public interface IRememberTokenRepository
{
    // Stores a token and drops the account's oldest beyond keep and every expired one.
    Task AddAsync(long accountId, string tokenHash, long createdAt, long expiresAt, int keep, CancellationToken ct = default);

    // The account an unexpired token belongs to, or null.
    Task<AccountRecord?> FindAccountAsync(string tokenHash, long now, CancellationToken ct = default);

    Task RemoveAsync(string tokenHash, CancellationToken ct = default);
}
