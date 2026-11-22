using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Caravans;
using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperCaravanStorageRepository(IDbConnectionFactory connectionFactory) : ICaravanStorageRepository
{
    public async Task<CaravanStorageRecord?> FindAsync(long characterId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<CaravanStorageRecord>(new CommandDefinition(
            "SELECT character_id, revision, items FROM caravan_storage WHERE character_id = @characterId",
            new { characterId }, cancellationToken: ct));
    }

    public async Task<bool> SaveWithCharacterAsync(CharacterRecord character, CaravanStorageRecord storage, long expectedRevision,
        CancellationToken ct = default)
    {
        if (character.Id != storage.CharacterId)
        {
            throw new ArgumentException("The caravan storage belongs to another character.", nameof(storage));
        }

        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var characterRows = await connection.ExecuteAsync(new CommandDefinition(
            DapperCharacterRepository.UpdateSql, character, transaction, cancellationToken: ct));
        if (characterRows == 0)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        var parameters = new { storage.CharacterId, storage.Revision, storage.Items, Expected = expectedRevision };

        // No row counts as revision 0, so only that expectation may insert.
        var sql = expectedRevision == 0
            ? """
              INSERT INTO caravan_storage (character_id, revision, items)
              VALUES (@CharacterId, @Revision, @Items)
              ON CONFLICT (character_id) DO UPDATE SET revision = excluded.revision, items = excluded.items
              WHERE caravan_storage.revision = 0
              """
            : """
              UPDATE caravan_storage SET revision = @Revision, items = @Items
              WHERE character_id = @CharacterId AND revision = @Expected
              """;
        if (await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: ct)) == 0)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        await transaction.CommitAsync(ct);
        return true;
    }
}
