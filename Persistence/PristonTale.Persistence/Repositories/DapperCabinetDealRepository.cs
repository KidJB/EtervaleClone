using System.Data.Common;
using System.Globalization;
using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Cabinet;
using PristonTale.Persistence.Abstractions.Coins;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperCabinetDealRepository(IDbConnectionFactory connectionFactory) : ICabinetDealRepository
{
    private const string Columns =
        "id AS Id, item_key AS ItemKey, starts_at AS StartsAt, ends_at AS EndsAt, start_bid AS StartBid, increment AS Increment, min_level AS MinLevel, "
        + "top_bid AS TopBid, top_account_id AS TopAccountId, top_name AS TopName, bids AS Bids, state AS State, created_by AS CreatedBy, closed_at AS ClosedAt";

    public async Task<long> CreateAsync(CabinetDealDraft draft, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO cabinet_deals (item_key, starts_at, ends_at, start_bid, increment, min_level, top_bid, top_account_id, top_name, bids, state, created_by, closed_at)
            VALUES (@ItemKey, @StartsAt, @EndsAt, @StartBid, @Increment, @MinLevel, 0, 0, '', 0, 0, @CreatedBy, 0)
            RETURNING id
            """, draft, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<CabinetDealRecord>> OpenAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<DealRow>(new CommandDefinition(
            $"SELECT {Columns} FROM cabinet_deals WHERE state = 0 ORDER BY ends_at, id", cancellationToken: ct));
        return rows.Select(static row => row.ToRecord()).ToList();
    }

    public async Task<CabinetDealRecord?> GetAsync(long dealId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await GetAsync(connection, null, dealId, ct);
    }

    public async Task<IReadOnlyList<CabinetBidRecord>> BidsAsync(long dealId, int limit, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<BidRow>(new CommandDefinition(
            "SELECT deal_id AS DealId, account_id AS AccountId, name AS Name, amount AS Amount, placed_at AS PlacedAt FROM cabinet_bids "
            + "WHERE deal_id = @dealId ORDER BY amount DESC, id DESC LIMIT @limit", new { dealId, limit }, cancellationToken: ct));
        return rows.Select(static row => new CabinetBidRecord(row.DealId, row.AccountId, row.Name, row.Amount, row.PlacedAt)).ToList();
    }

    public async Task<(CabinetBidStore Status, long Balance)> BidAsync(long dealId, long accountId, string name, long amount, long now, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var deal = await GetAsync(connection, transaction, dealId, ct);
        var refusal = deal is null ? CabinetBidStore.Unknown
            : deal.State != CabinetDealRecord.Open || now >= deal.EndsAt || now < deal.StartsAt ? CabinetBidStore.Ended
            : deal.TopAccountId == accountId ? CabinetBidStore.Leading
            : amount < deal.NextBid ? CabinetBidStore.TooLow
            : (CabinetBidStore?)null;
        if (refusal is { } refused)
        {
            await transaction.RollbackAsync(ct);
            return (refused, await BalanceAsync(connection, accountId, ct));
        }

        var balance = await DapperCoinRepository.ChangeAsync(connection, transaction, accountId, -amount,
            string.Create(CultureInfo.InvariantCulture, $"cabinet deal {dealId} bid"), ct);
        if (balance is null)
        {
            await transaction.RollbackAsync(ct);
            return (CabinetBidStore.NotEnoughCoins, await BalanceAsync(connection, accountId, ct));
        }

        // Leads only if no other bid landed since the deal was read.
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE cabinet_deals SET top_bid = @amount, top_account_id = @accountId, top_name = @name, bids = bids + 1
            WHERE id = @dealId AND state = 0 AND top_bid = @TopBid AND top_account_id = @TopAccountId
            """, new { amount, accountId, name, dealId, deal!.TopBid, deal.TopAccountId }, transaction, cancellationToken: ct));
        if (rows == 0)
        {
            await transaction.RollbackAsync(ct);
            return (CabinetBidStore.TooLow, await BalanceAsync(connection, accountId, ct));
        }

        if (deal.TopAccountId != 0
            && await DapperCoinRepository.ChangeAsync(connection, transaction, deal.TopAccountId, deal.TopBid,
                string.Create(CultureInfo.InvariantCulture, $"cabinet deal {dealId} outbid refund"), ct) is null)
        {
            await transaction.RollbackAsync(ct);
            return (CabinetBidStore.Unknown, await BalanceAsync(connection, accountId, ct));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO cabinet_bids (deal_id, account_id, name, amount, placed_at) VALUES (@dealId, @accountId, @name, @amount, @now)",
            new { dealId, accountId, name, amount, now }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return (CabinetBidStore.Placed, balance.Value);
    }

    public async Task<IReadOnlyList<CabinetDealClose>> CloseDueAsync(long now, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var due = await connection.QueryAsync<long>(new CommandDefinition(
            "SELECT id FROM cabinet_deals WHERE state = 0 AND ends_at <= @now ORDER BY ends_at", new { now }, cancellationToken: ct));
        var closed = new List<CabinetDealClose>();
        foreach (var id in due.ToList())
        {
            if (await CloseAsync(connection, id, now, ct) is { } close)
            {
                closed.Add(close);
            }
        }

        return closed;
    }

    public async Task<CabinetDealClose?> EndAsync(long dealId, long now, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await CloseAsync(connection, dealId, now, ct);
    }

    // Closes one open deal in its own transaction: the leader receives the look, or its coins back when it owns the look by now.
    private static async Task<CabinetDealClose?> CloseAsync(DbConnection connection, long dealId, long now, CancellationToken ct)
    {
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var deal = await GetAsync(connection, transaction, dealId, ct);
        if (deal is null || await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE cabinet_deals SET state = 1, closed_at = @now WHERE id = @dealId AND state = 0", new { now, dealId }, transaction, cancellationToken: ct)) == 0)
        {
            await transaction.RollbackAsync(ct);
            return null;
        }

        var awarded = false;
        var refunded = false;
        if (deal.TopAccountId != 0)
        {
            awarded = await DapperCoinRepository.InsertSkinAsync(connection, transaction,
                new OwnedSkinRecord(deal.TopAccountId, deal.ItemKey, now, (short)Simulation.Characters.CosmeticSource.Deal, 0), ct);
            refunded = !awarded && await DapperCoinRepository.ChangeAsync(connection, transaction, deal.TopAccountId, deal.TopBid,
                string.Create(CultureInfo.InvariantCulture, $"cabinet deal {dealId} refund: look already owned"), ct) is not null;
        }

        await transaction.CommitAsync(ct);
        return new CabinetDealClose(deal with { State = CabinetDealRecord.Closed, ClosedAt = now }, awarded, refunded);
    }

    private static async Task<CabinetDealRecord?> GetAsync(DbConnection connection, DbTransaction? transaction, long dealId, CancellationToken ct) =>
        (await connection.QuerySingleOrDefaultAsync<DealRow>(new CommandDefinition(
            $"SELECT {Columns} FROM cabinet_deals WHERE id = @dealId", new { dealId }, transaction, cancellationToken: ct)))?.ToRecord();

    private static async Task<long> BalanceAsync(DbConnection connection, long accountId, CancellationToken ct) =>
        await connection.ExecuteScalarAsync<long?>(new CommandDefinition("SELECT coins FROM accounts WHERE id = @accountId", new { accountId }, cancellationToken: ct)) ?? 0;

    private sealed class DealRow
    {
        public long Id { get; init; }
        public string ItemKey { get; init; } = "";
        public long StartsAt { get; init; }
        public long EndsAt { get; init; }
        public long StartBid { get; init; }
        public long Increment { get; init; }
        public long MinLevel { get; init; }
        public long TopBid { get; init; }
        public long TopAccountId { get; init; }
        public string TopName { get; init; } = "";
        public long Bids { get; init; }
        public long State { get; init; }
        public string CreatedBy { get; init; } = "";
        public long ClosedAt { get; init; }

        public CabinetDealRecord ToRecord() =>
            new(Id, ItemKey, StartsAt, EndsAt, StartBid, Increment, (int)MinLevel, TopBid, TopAccountId, TopName, (int)Bids, (short)State, CreatedBy, ClosedAt);
    }

    private sealed class BidRow
    {
        public long DealId { get; init; }
        public long AccountId { get; init; }
        public string Name { get; init; } = "";
        public long Amount { get; init; }
        public long PlacedAt { get; init; }
    }
}
