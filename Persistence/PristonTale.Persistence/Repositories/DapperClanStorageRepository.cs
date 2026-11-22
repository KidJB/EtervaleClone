using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Clans;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperClanStorageRepository(IDbConnectionFactory connectionFactory) : IClanStorageRepository
{
    internal const string LogInsertSql = """
        INSERT INTO clan_warehouse_log (clan_id, at, character_name, action, item_key, item_name, quantity)
        VALUES (@ClanId, @At, @CharacterName, @Action, @ItemKey, @ItemName, @Quantity)
        """;

    public async Task<ClanWarehouseRecord?> FindWarehouseAsync(long clanId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<(long ClanId, long? Revision, string? Items, long SlotLevel, long WeightLevel)>(new CommandDefinition(
            """
            SELECT c.id, w.revision, w.items, c.warehouse_slot_level, c.warehouse_weight_level
            FROM clans c LEFT JOIN clan_warehouses w ON w.clan_id = c.id WHERE c.id = @clanId
            """, new { clanId }, cancellationToken: ct));
        return rows.FirstOrDefault() is { ClanId: > 0 } found
            ? new ClanWarehouseRecord(found.ClanId, found.Revision ?? 0, new JsonText(found.Items ?? "[]"), (int)found.SlotLevel, (int)found.WeightLevel)
            : null;
    }

    public async Task<bool> SaveWarehouseWithCharacterAsync(CharacterRecord character, ClanWarehouseRecord warehouse, long expectedRevision,
        IReadOnlyList<ClanWarehouseLogRecord> log, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var characterRows = await connection.ExecuteAsync(new CommandDefinition(
            DapperCharacterRepository.UpdateSql, character, transaction, cancellationToken: ct));
        if (characterRows == 0)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        var parameters = new { warehouse.ClanId, warehouse.Revision, warehouse.Items, Expected = expectedRevision };

        // No row counts as revision 0, so only that expectation may insert.
        var sql = expectedRevision == 0
            ? """
              INSERT INTO clan_warehouses (clan_id, revision, items)
              VALUES (@ClanId, @Revision, @Items)
              ON CONFLICT (clan_id) DO UPDATE SET revision = excluded.revision, items = excluded.items
              WHERE clan_warehouses.revision = 0
              """
            : """
              UPDATE clan_warehouses SET revision = @Revision, items = @Items
              WHERE clan_id = @ClanId AND revision = @Expected
              """;
        if (await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: ct)) == 0)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        if (log.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(LogInsertSql, log, transaction, cancellationToken: ct));
        }

        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<ClanWarehouseLogRecord>> WarehouseLogAsync(long clanId, int limit, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<(long ClanId, long At, string CharacterName, long Action, string ItemKey, string ItemName, long Quantity)>(
            new CommandDefinition(
                """
                SELECT clan_id, at, character_name, action, item_key, item_name, quantity FROM clan_warehouse_log
                WHERE clan_id = @clanId ORDER BY id DESC LIMIT @limit
                """, new { clanId, limit }, cancellationToken: ct));
        return rows.Select(static row => new ClanWarehouseLogRecord(row.ClanId, row.At, row.CharacterName, (short)row.Action, row.ItemKey, row.ItemName,
            (int)row.Quantity)).ToArray();
    }

    public async Task<IReadOnlyList<ClanBuffRecord>> LoadBuffsAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<(long ClanId, long Kind, long EndsAt)>(new CommandDefinition(
            "SELECT clan_id, kind, ends_at FROM clan_buffs ORDER BY clan_id, kind", cancellationToken: ct));
        return rows.Select(static row => new ClanBuffRecord(row.ClanId, (short)row.Kind, row.EndsAt)).ToArray();
    }

    public async Task ReplaceBuffsAsync(IReadOnlyList<ClanBuffRecord> buffs, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM clan_buffs", transaction: transaction, cancellationToken: ct));
        if (buffs.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO clan_buffs (clan_id, kind, ends_at)
                SELECT @ClanId, @Kind, @EndsAt WHERE EXISTS (SELECT 1 FROM clans WHERE id = @ClanId)
                """, buffs, transaction, cancellationToken: ct));
        }

        await transaction.CommitAsync(ct);
    }
}
