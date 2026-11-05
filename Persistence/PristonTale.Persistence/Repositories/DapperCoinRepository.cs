using System.Data.Common;
using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Coins;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperCoinRepository(IDbConnectionFactory connectionFactory) : ICoinRepository
{
    private const string LogSql = "INSERT INTO coin_log (account_id, change, balance, reason) VALUES (@AccountId, @Change, @Balance, @Reason)";

    public async Task<long> BalanceAsync(long accountId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT coins FROM accounts WHERE id = @accountId", new { accountId }, cancellationToken: ct)) ?? 0;
    }

    public async Task<long?> AddAsync(long accountId, long amount, string reason, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var balance = await ChangeAsync(connection, transaction, accountId, amount, reason, ct);
        if (balance is null)
        {
            await transaction.RollbackAsync(ct);
            return null;
        }

        await transaction.CommitAsync(ct);
        return balance;
    }

    public async Task<long?> BuyWithCharacterAsync(CharacterRecord character, long price, string reason, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var balance = await ChangeAsync(connection, transaction, character.AccountId, -price, reason, ct);
        if (balance is null
            || await connection.ExecuteAsync(new CommandDefinition(DapperCharacterRepository.UpdateSql, character, transaction, cancellationToken: ct)) == 0)
        {
            await transaction.RollbackAsync(ct);
            return null;
        }

        await transaction.CommitAsync(ct);
        return balance;
    }

    public async Task<CoinTradeStore> TradeWithCharactersAsync(IReadOnlyList<CharacterRecord> characters, IReadOnlyList<CoinTransfer> transfers,
        CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        foreach (var transfer in transfers)
        {
            if (await ChangeAsync(connection, transaction, transfer.FromAccountId, -transfer.Amount, transfer.Reason, ct) is null)
            {
                await transaction.RollbackAsync(ct);
                return CoinTradeStore.NotEnoughCoins;
            }

            if (await ChangeAsync(connection, transaction, transfer.ToAccountId, transfer.Received, transfer.Reason, ct) is null)
            {
                await transaction.RollbackAsync(ct);
                return CoinTradeStore.Failed;
            }
        }

        foreach (var character in characters.OrderBy(static record => record.Id))
        {
            if (await connection.ExecuteAsync(new CommandDefinition(DapperCharacterRepository.UpdateSql, character, transaction, cancellationToken: ct)) == 0)
            {
                await transaction.RollbackAsync(ct);
                return CoinTradeStore.Failed;
            }
        }

        await transaction.CommitAsync(ct);
        return CoinTradeStore.Stored;
    }

    // Changes the balance only when it stays at or above zero, and logs the change; null when it would not.
    private static async Task<long?> ChangeAsync(DbConnection connection, DbTransaction transaction, long accountId, long change, string reason,
        CancellationToken ct)
    {
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE accounts SET coins = coins + @change WHERE id = @accountId AND coins + @change >= 0",
            new { accountId, change }, transaction, cancellationToken: ct));
        if (rows == 0)
        {
            return null;
        }

        var balance = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT coins FROM accounts WHERE id = @accountId", new { accountId }, transaction, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition(LogSql, new { AccountId = accountId, Change = change, Balance = balance, Reason = reason },
            transaction, cancellationToken: ct));
        return balance;
    }
}
