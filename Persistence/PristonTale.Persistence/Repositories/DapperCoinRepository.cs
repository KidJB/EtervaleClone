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

    public async Task<IReadOnlyList<string>> OwnedSkinsAsync(long accountId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var keys = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT item_key FROM account_skins WHERE account_id = @AccountId", new { AccountId = accountId }, cancellationToken: ct));
        return keys.ToList();
    }

    public async Task<long?> BuySkinAsync(long accountId, string itemKey, long price, string reason, long unlockedAt = 0, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var balance = await ChangeAsync(connection, transaction, accountId, -price, reason, ct);
        if (balance is null || !await InsertSkinAsync(connection, transaction, new OwnedSkinRecord(accountId, itemKey, unlockedAt, (short)Simulation.Characters.CosmeticSource.Coins, 0), ct))
        {
            await transaction.RollbackAsync(ct);
            return null;
        }

        await transaction.CommitAsync(ct);
        return balance;
    }

    public async Task<IReadOnlyList<OwnedSkinRecord>> SkinRecordsAsync(long accountId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<SkinRow>(new CommandDefinition(
            "SELECT account_id AS AccountId, item_key AS ItemKey, unlocked_at AS UnlockedAt, source AS Source, character_id AS CharacterId, bound AS Bound "
            + "FROM account_skins WHERE account_id = @accountId ORDER BY unlocked_at, item_key", new { accountId }, cancellationToken: ct));
        return rows.Select(static row => new OwnedSkinRecord(row.AccountId, row.ItemKey, row.UnlockedAt, (short)row.Source, row.CharacterId, row.Bound != 0)).ToList();
    }

    public async Task<bool> UnlockSkinAsync(OwnedSkinRecord skin, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (!await InsertSkinAsync(connection, transaction, skin, ct))
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<SkinUnlockStore> UnlockSkinWithCharacterAsync(CharacterRecord character, OwnedSkinRecord skin, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (!await InsertSkinAsync(connection, transaction, skin, ct))
        {
            await transaction.RollbackAsync(ct);
            return SkinUnlockStore.Owned;
        }

        if (await connection.ExecuteAsync(new CommandDefinition(DapperCharacterRepository.UpdateSql, character, transaction, cancellationToken: ct)) == 0)
        {
            await transaction.RollbackAsync(ct);
            return SkinUnlockStore.Failed;
        }

        await transaction.CommitAsync(ct);
        return SkinUnlockStore.Stored;
    }

    public async Task<int> ResetSkinsAsync(long accountId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("DELETE FROM account_skins WHERE account_id = @accountId", new { accountId }, cancellationToken: ct));
    }

    // Records a skin unless the account owns it (one row per account and key, also kept by the primary key).
    internal static async Task<bool> InsertSkinAsync(DbConnection connection, DbTransaction transaction, OwnedSkinRecord skin, CancellationToken ct) =>
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO account_skins (account_id, item_key, unlocked_at, source, character_id, bound)
            SELECT @AccountId, @ItemKey, @UnlockedAt, @Source, @CharacterId, @Bound
            WHERE NOT EXISTS (SELECT 1 FROM account_skins WHERE account_id = @AccountId AND item_key = @ItemKey)
            """,
            new { skin.AccountId, skin.ItemKey, skin.UnlockedAt, skin.Source, skin.CharacterId, Bound = (short)(skin.Bound ? 1 : 0) }, transaction, cancellationToken: ct)) > 0;

    private sealed class SkinRow
    {
        public long AccountId { get; init; }
        public string ItemKey { get; init; } = "";
        public long UnlockedAt { get; init; }
        public long Source { get; init; }
        public long CharacterId { get; init; }
        public long Bound { get; init; }
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
    internal static async Task<long?> ChangeAsync(DbConnection connection, DbTransaction transaction, long accountId, long change, string reason,
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
