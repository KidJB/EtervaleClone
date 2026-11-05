using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Stalls;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperStallRepository(IDbConnectionFactory connectionFactory) : IStallRepository
{
    private const string Columns = "id, account_id, character_id, field_id, x, y, z, facing, look, items";

    public async Task<IReadOnlyList<StallRecord>> ListAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return (await connection.QueryAsync<StallRow>(new CommandDefinition($"SELECT {Columns} FROM stalls ORDER BY id", cancellationToken: ct)))
            .Select(static row => row.ToRecord()).ToList();
    }

    public async Task<long?> CreateWithCharacterAsync(CharacterRecord seller, StallRecord stall, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var taken = await connection.ExecuteScalarAsync<long>(new CommandDefinition("SELECT COUNT(*) FROM stalls WHERE account_id = @AccountId",
            new { stall.AccountId }, transaction, cancellationToken: ct));
        if (taken > 0 || await connection.ExecuteAsync(new CommandDefinition(DapperCharacterRepository.UpdateSql, seller, transaction, cancellationToken: ct)) == 0)
        {
            await transaction.RollbackAsync(ct);
            return null;
        }

        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO stalls (account_id, character_id, field_id, x, y, z, facing, look, items)
            VALUES (@AccountId, @CharacterId, @FieldId, @X, @Y, @Z, @Facing, @Look, @Items) RETURNING id
            """, stall, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return id;
    }

    public async Task<StallSaleStore> SellAsync(StallSale sale, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var stallSql = sale.Empty ? "DELETE FROM stalls WHERE id = @StallId" : "UPDATE stalls SET items = @Items WHERE id = @StallId";
        if (await connection.ExecuteAsync(new CommandDefinition(stallSql, new { sale.StallId, sale.Items }, transaction, cancellationToken: ct)) == 0)
        {
            await transaction.RollbackAsync(ct);
            return StallSaleStore.Gone;
        }

        var paid = sale.Owner is { } owner
            ? await connection.ExecuteAsync(new CommandDefinition(DapperCharacterRepository.UpdateSql, owner, transaction, cancellationToken: ct))
            : await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE characters SET money = money + @Price WHERE id = @OwnerCharacterId AND money + @Price <= @OwnerMoneyLimit",
                new { sale.Price, sale.OwnerCharacterId, sale.OwnerMoneyLimit }, transaction, cancellationToken: ct));
        if (paid == 0)
        {
            await transaction.RollbackAsync(ct);
            return sale.Owner is null ? StallSaleStore.GoldLimit : StallSaleStore.Failed;
        }

        if (await connection.ExecuteAsync(new CommandDefinition(DapperCharacterRepository.UpdateSql, sale.Buyer, transaction, cancellationToken: ct)) == 0)
        {
            await transaction.RollbackAsync(ct);
            return StallSaleStore.Failed;
        }

        await transaction.CommitAsync(ct);
        return StallSaleStore.Stored;
    }

    public async Task<long?> CloseAsync(long accountId, string sender, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var stall = (await connection.QuerySingleOrDefaultAsync<StallRow>(new CommandDefinition(
            $"SELECT {Columns} FROM stalls WHERE account_id = @accountId", new { accountId }, transaction, cancellationToken: ct)))?.ToRecord();
        if (stall is null)
        {
            await transaction.RollbackAsync(ct);
            return null;
        }

        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM stalls WHERE id = @Id", new { stall.Id }, transaction, cancellationToken: ct));
        foreach (var item in StallCodec.ItemBoxItems(stall.Items))
        {
            await connection.ExecuteAsync(new CommandDefinition("INSERT INTO item_box (account_id, sender, item) VALUES (@accountId, @sender, @item)",
                new { accountId, sender, item }, transaction, cancellationToken: ct));
        }

        await transaction.CommitAsync(ct);
        return stall.Id;
    }

    // A stored stall as either provider returns it (whole numbers may come back as integers); Dapper converts each column.
    private sealed class StallRow
    {
        public long Id { get; set; }
        public long AccountId { get; set; }
        public long CharacterId { get; set; }
        public int FieldId { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Facing { get; set; }
        public string Look { get; set; } = "";
        public string Items { get; set; } = "";

        public StallRecord ToRecord() => new(Id, AccountId, CharacterId, FieldId, X, Y, Z, Facing, Look, Items);
    }
}
