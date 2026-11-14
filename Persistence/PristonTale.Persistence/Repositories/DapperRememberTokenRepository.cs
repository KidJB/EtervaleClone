using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperRememberTokenRepository(IDbConnectionFactory connectionFactory) : IRememberTokenRepository
{
    public async Task AddAsync(long accountId, string tokenHash, long createdAt, long expiresAt, int keep, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO remember_tokens (token_hash, account_id, created_at, expires_at) VALUES (@tokenHash, @accountId, @createdAt, @expiresAt)",
            new { tokenHash, accountId, createdAt, expiresAt }, transaction, cancellationToken: ct));
        var newestFirst = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT token_hash FROM remember_tokens WHERE account_id = @accountId ORDER BY created_at DESC, token_hash",
            new { accountId }, transaction, cancellationToken: ct));
        var surplus = newestFirst.Skip(keep).ToArray();
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM remember_tokens WHERE expires_at <= @createdAt",
            new { createdAt }, transaction, cancellationToken: ct));
        foreach (var hash in surplus)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM remember_tokens WHERE token_hash = @hash", new { hash }, transaction, cancellationToken: ct));
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<AccountRecord?> FindAccountAsync(string tokenHash, long now, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<AccountRecord>(new CommandDefinition(
            """
            SELECT a.id, a.user_id, a.password_hash, a.is_blocked, a.is_tester, a.gm_level
            FROM remember_tokens t JOIN accounts a ON a.id = t.account_id
            WHERE t.token_hash = @tokenHash AND t.expires_at > @now
            """,
            new { tokenHash, now }, cancellationToken: ct));
    }

    public async Task RemoveAsync(string tokenHash, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM remember_tokens WHERE token_hash = @tokenHash", new { tokenHash }, cancellationToken: ct));
    }
}
