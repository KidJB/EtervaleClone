using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.ItemBoxes;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperItemBoxRepository(IDbConnectionFactory connectionFactory) : IItemBoxRepository
{
    private const string InsertSql = "INSERT INTO item_box (account_id, sender, item) VALUES (@accountId, @sender, @item)";

    public async Task<IReadOnlyList<ItemBoxRecord>> ListAsync(long accountId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<ItemBoxRecord>(new CommandDefinition(
            "SELECT id, sender, item, created_at FROM item_box WHERE account_id = @accountId ORDER BY id LIMIT @limit",
            new { accountId, limit = ItemBoxLimits.MaxListed }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<ItemBoxRecord?> FindAsync(long accountId, long id, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<ItemBoxRecord>(new CommandDefinition(
            "SELECT id, sender, item, created_at FROM item_box WHERE account_id = @accountId AND id = @id", new { accountId, id }, cancellationToken: ct));
    }

    public async Task<long> AddAsync(long accountId, string sender, string item, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(InsertSql + " RETURNING id", new { accountId, sender, item }, cancellationToken: ct));
    }

    public async Task<bool> TakeWithCharacterAsync(long id, CharacterRecord character, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var taken = await connection.ExecuteAsync(new CommandDefinition("DELETE FROM item_box WHERE id = @id AND account_id = @AccountId",
            new { id, character.AccountId }, transaction, cancellationToken: ct));
        if (taken == 0 || await connection.ExecuteAsync(new CommandDefinition(DapperCharacterRepository.UpdateSql, character, transaction, cancellationToken: ct)) == 0)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<bool> DiscardAsync(long accountId, long id, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("DELETE FROM item_box WHERE id = @id AND account_id = @accountId",
            new { id, accountId }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> SendWithCharacterAsync(CharacterRecord sender, long toAccountId, string senderName, string item, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        if (await connection.ExecuteAsync(new CommandDefinition(DapperCharacterRepository.UpdateSql, sender, transaction, cancellationToken: ct)) == 0)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(InsertSql, new { accountId = toAccountId, sender = senderName, item }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<long?> AccountOfCharacterAsync(string characterName, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT account_id FROM characters WHERE lower(name) = lower(@characterName)", new { characterName }, cancellationToken: ct));
    }
}
