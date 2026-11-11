using System.Data.Common;
using Dapper;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Markets;
using PristonTale.Simulation.Markets;

namespace PristonTale.Persistence.Repositories;

// Every write opens its transaction with a write, so SQLite takes its lock first and never fails to upgrade a read.
public sealed class DapperCharacterMarketRepository(IDbConnectionFactory connectionFactory) : ICharacterMarketRepository
{
    private const short Active = (short)CharacterListingState.Active;
    private const string Fields =
        "id, character_id, seller_account_id, seller_name, seller_hidden, name, job_code, level, tier, exp_basis_points, face_index, armor_key, weapon_key, off_hand_key, price, fee, state, created_at, expires_at";

    private const string InsertAudit =
        "INSERT INTO character_market_audit (listing_id, action, actor_account_id, detail, created_at) VALUES (@ListingId, @Action, @Actor, @Detail, @Now)";

    public async Task<CharacterListingPage> BrowseAsync(CharacterListingQuery query, CancellationToken ct = default)
    {
        var where = "state = 0 AND expires_at > @Now";
        if (query.JobCode > 0)
        {
            where += " AND job_code = @JobCode";
        }

        var search = query.Search.Trim().ToLowerInvariant();
        if (search.Length > 0)
        {
            where += " AND name_key LIKE @Pattern ESCAPE '\\'";
        }

        var order = query.Sort switch
        {
            CharacterMarketSort.PriceLow => "price ASC, id DESC",
            CharacterMarketSort.PriceHigh => "price DESC, id DESC",
            CharacterMarketSort.LevelHigh => "level DESC, id DESC",
            CharacterMarketSort.Name => "name_key ASC, id DESC",
            _ => "created_at DESC, id DESC"
        };
        var values = new
        {
            query.Now,
            query.JobCode,
            Pattern = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%",
            Limit = CharacterMarketRules.PageSize,
            Offset = Math.Max(0, query.Page) * CharacterMarketRules.PageSize
        };
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<ListingRow>(new CommandDefinition(
            $"SELECT {Fields} FROM character_market_listings WHERE {where} ORDER BY {order} LIMIT @Limit OFFSET @Offset", values, cancellationToken: ct));
        var total = await connection.ExecuteScalarAsync<long>(new CommandDefinition($"SELECT COUNT(*) FROM character_market_listings WHERE {where}", values, cancellationToken: ct));
        return new CharacterListingPage(await WithHairAsync(connection, rows, ct), (int)total);
    }

    public async Task<CharacterListingPage> ListingsOfAsync(long accountId, int page, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var values = new { accountId, Limit = CharacterMarketRules.PageSize, Offset = Math.Max(0, page) * CharacterMarketRules.PageSize };
        var rows = await connection.QueryAsync<ListingRow>(new CommandDefinition(
            $"SELECT {Fields} FROM character_market_listings WHERE seller_account_id = @accountId ORDER BY state ASC, id DESC LIMIT @Limit OFFSET @Offset", values, cancellationToken: ct));
        var total = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM character_market_listings WHERE seller_account_id = @accountId", values, cancellationToken: ct));
        return new CharacterListingPage(await WithHairAsync(connection, rows, ct), (int)total);
    }

    public async Task<CharacterListing?> FindAsync(long listingId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<ListingRow>(new CommandDefinition(
            $"SELECT {Fields} FROM character_market_listings WHERE id = @listingId", new { listingId }, cancellationToken: ct));
        return row is null ? null : ToListing(row);
    }

    public async Task<CharacterRecord?> CharacterOfAsync(long characterId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<CharacterRecord>(new CommandDefinition(
            $"SELECT {DapperCharacterRepository.Columns} FROM characters WHERE id = @characterId", new { characterId }, cancellationToken: ct));
    }

    public async Task<CharacterListingBlocks> BlocksAsync(long accountId, long now, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var clan = await connection.QueryAsync<long>(new CommandDefinition(
            "SELECT m.character_id FROM clan_members m JOIN characters c ON c.id = m.character_id WHERE c.account_id = @accountId", new { accountId }, cancellationToken: ct));
        var stalls = await connection.QueryAsync<long>(new CommandDefinition(
            "SELECT character_id FROM stalls WHERE account_id = @accountId", new { accountId }, cancellationToken: ct));
        var listed = await connection.QueryAsync<long>(new CommandDefinition(
            "SELECT character_id FROM character_market_listings WHERE seller_account_id = @accountId AND state = 0 AND expires_at > @now AND character_id IS NOT NULL",
            new { accountId, now }, cancellationToken: ct));
        return new CharacterListingBlocks(clan.ToHashSet(), stalls.ToHashSet(), listed.ToHashSet());
    }

    public async Task<bool> IsListedAsync(long characterId, long now, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM character_market_listings WHERE character_id = @characterId AND state = 0 AND expires_at > @now", new { characterId, now }, cancellationToken: ct)) > 0;
    }

    public async Task<CharacterMarketStore> ListAsync(CharacterListingDraft draft, CharacterMarketOptions options, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE character_market_listings SET state = @Expired, closed_at = @Now WHERE character_id = @CharacterId AND state = 0 AND expires_at <= @Now",
            new { Expired = (short)CharacterListingState.Expired, draft.Now, draft.CharacterId }, transaction, cancellationToken: ct));
        var status = await RefusalAsync(connection, transaction, draft, options, ct);
        if (status != CharacterMarketStatus.Ok)
        {
            await transaction.RollbackAsync(ct);
            return new CharacterMarketStore(status);
        }

        long? balance = null;
        if (options.ListingFee > 0)
        {
            balance = await DapperCoinRepository.ChangeAsync(connection, transaction, draft.SellerAccountId, -options.ListingFee, $"character market fee: {draft.Name}", ct);
            if (balance is null)
            {
                await transaction.RollbackAsync(ct);
                return new CharacterMarketStore(CharacterMarketStatus.NotEnoughCoins);
            }
        }

        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO character_market_listings (operation_id, character_id, seller_account_id, seller_name, seller_hidden, name, name_key, job_code, level, tier,
                exp_basis_points, face_index, armor_key, weapon_key, off_hand_key, price, fee, state, created_at, expires_at)
            VALUES (@Operation, @CharacterId, @SellerAccountId, @SellerName, @Hidden, @Name, @NameKey, @JobCode, @Level, @Tier,
                @ExpBasisPoints, @FaceIndex, @ArmorKey, @WeaponKey, @OffHandKey, @Price, @Fee, 0, @Now, @ExpiresAt)
            RETURNING id
            """,
            new
            {
                Operation = draft.OperationId.ToString("D"),
                draft.CharacterId,
                draft.SellerAccountId,
                draft.SellerName,
                Hidden = (short)(draft.SellerHidden ? 1 : 0),
                draft.Name,
                NameKey = draft.Name.ToLowerInvariant(),
                draft.JobCode,
                draft.Level,
                draft.Tier,
                draft.ExpBasisPoints,
                draft.FaceIndex,
                draft.ArmorKey,
                draft.WeaponKey,
                draft.OffHandKey,
                draft.Price,
                Fee = options.ListingFee,
                draft.Now,
                ExpiresAt = CharacterMarketRules.ExpiresAt(draft.Now, options.ExpiryDays)
            }, transaction, cancellationToken: ct));
        await AuditAsync(connection, transaction, id, "listed", draft.SellerAccountId, $"{draft.Name} for {draft.Price}", draft.Now, ct);
        await transaction.CommitAsync(ct);
        return new CharacterMarketStore(CharacterMarketStatus.Ok, balance ?? 0, id);
    }

    public async Task<CharacterMarketStore> CancelAsync(long listingId, long sellerAccountId, long now, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE character_market_listings SET state = @Cancelled, closed_at = @now WHERE id = @listingId AND seller_account_id = @sellerAccountId AND state = 0",
            new { Cancelled = (short)CharacterListingState.Cancelled, now, listingId, sellerAccountId }, transaction, cancellationToken: ct));
        if (rows == 0)
        {
            await transaction.RollbackAsync(ct);
            return new CharacterMarketStore(CharacterMarketStatus.NotFound);
        }

        await AuditAsync(connection, transaction, listingId, "cancelled", sellerAccountId, "", now, ct);
        await transaction.CommitAsync(ct);
        return new CharacterMarketStore(CharacterMarketStatus.Ok, 0, listingId);
    }

    public async Task<CharacterMarketStore> BuyAsync(CharacterMarketPurchase purchase, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var claimed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE character_market_listings SET state = @Sold, closed_at = @Now
            WHERE id = @ListingId AND state = 0 AND expires_at > @Now AND price = @ExpectedPrice AND seller_account_id <> @BuyerAccountId AND character_id IS NOT NULL
            """,
            new { Sold = (short)CharacterListingState.Sold, purchase.Now, purchase.ListingId, purchase.ExpectedPrice, purchase.BuyerAccountId }, transaction, cancellationToken: ct));
        if (claimed == 0)
        {
            var refused = await RefusedPurchaseAsync(connection, transaction, purchase, ct);
            await transaction.RollbackAsync(ct);
            return refused;
        }

        var listing = ToListing(await connection.QuerySingleAsync<ListingRow>(new CommandDefinition(
            $"SELECT {Fields} FROM character_market_listings WHERE id = @ListingId", new { purchase.ListingId }, transaction, cancellationToken: ct)));
        var character = listing.CharacterId!.Value;
        var slot = await FreeSlotAsync(connection, transaction, purchase, ct);
        var status = await SaleRefusalAsync(connection, transaction, listing, character, slot, ct);
        if (status != CharacterMarketStatus.Ok)
        {
            await transaction.RollbackAsync(ct);
            return new CharacterMarketStore(status);
        }

        var tax = CharacterMarketRules.Tax(listing.Price, purchase.TaxPercent);
        var received = listing.Price - tax;
        var balance = await MoveCoinsAsync(connection, transaction, purchase.BuyerAccountId, listing.SellerAccountId, listing.Price, received, listing.Name, ct);
        if (balance is null)
        {
            await transaction.RollbackAsync(ct);
            return new CharacterMarketStore(CharacterMarketStatus.NotEnoughCoins);
        }

        var moved = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE characters SET account_id = @Buyer, slot = @Slot WHERE id = @Character AND account_id = @Seller",
            new { Buyer = purchase.BuyerAccountId, Slot = (short)slot!.Value, Character = character, Seller = listing.SellerAccountId }, transaction, cancellationToken: ct));
        if (moved == 0)
        {
            await transaction.RollbackAsync(ct);
            return new CharacterMarketStore(CharacterMarketStatus.NotFound);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO character_market_sales (operation_id, listing_id, character_id, character_name, seller_account_id, buyer_account_id, price, tax, seller_received, created_at)
            VALUES (@Operation, @ListingId, @Character, @Name, @Seller, @Buyer, @Price, @Tax, @Received, @Now)
            """,
            new
            {
                Operation = purchase.OperationId.ToString("D"),
                purchase.ListingId,
                Character = character,
                listing.Name,
                Seller = listing.SellerAccountId,
                Buyer = purchase.BuyerAccountId,
                listing.Price,
                Tax = tax,
                Received = received,
                purchase.Now
            }, transaction, cancellationToken: ct));
        await AuditAsync(connection, transaction, purchase.ListingId, "sold", purchase.BuyerAccountId, $"{listing.Name} for {listing.Price}, tax {tax}", purchase.Now, ct);
        await transaction.CommitAsync(ct);
        return new CharacterMarketStore(CharacterMarketStatus.Ok, balance.Value, purchase.ListingId);
    }

    public async Task<bool> RemoveAsync(long listingId, long actorAccountId, string reason, long now, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE character_market_listings SET state = @Removed, closed_at = @now WHERE id = @listingId AND state = 0",
            new { Removed = (short)CharacterListingState.Removed, now, listingId }, transaction, cancellationToken: ct));
        if (rows == 0)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        await AuditAsync(connection, transaction, listingId, "removed", actorAccountId, reason, now, ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<int> ExpireAsync(long now, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO character_market_audit (listing_id, action, actor_account_id, detail, created_at)
            SELECT id, 'expired', NULL, '', @now FROM character_market_listings WHERE state = 0 AND expires_at <= @now
            """, new { now }, transaction, cancellationToken: ct));
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE character_market_listings SET state = @Expired, closed_at = @now WHERE state = 0 AND expires_at <= @now",
            new { Expired = (short)CharacterListingState.Expired, now }, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
        return rows;
    }

    public async Task<IReadOnlyList<CharacterMarketSale>> RecentSalesAsync(int limit, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<SaleRow>(new CommandDefinition(
            """
            SELECT id, operation_id, listing_id, character_id, character_name, seller_account_id, buyer_account_id, price, tax, seller_received, created_at
            FROM character_market_sales ORDER BY id DESC LIMIT @limit
            """, new { limit = Math.Max(1, limit) }, cancellationToken: ct));
        return rows.Select(static row => new CharacterMarketSale(row.Id, Guid.Parse(row.OperationId), row.ListingId, row.CharacterId, row.CharacterName, row.SellerAccountId,
            row.BuyerAccountId, row.Price, row.Tax, row.SellerReceived, row.CreatedAt)).ToList();
    }

    public async Task<IReadOnlyList<CharacterListing>> RecentListingsAsync(int limit, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<ListingRow>(new CommandDefinition(
            $"SELECT {Fields} FROM character_market_listings ORDER BY id DESC LIMIT @limit", new { limit = Math.Max(1, limit) }, cancellationToken: ct));
        return rows.Select(ToListing).ToList();
    }

    private static async Task<CharacterMarketStatus> RefusalAsync(DbConnection connection, DbTransaction transaction, CharacterListingDraft draft, CharacterMarketOptions options, CancellationToken ct)
    {
        var owner = await OwnerAsync(connection, transaction, draft.CharacterId, ct);
        if (owner is null || owner.AccountId != draft.SellerAccountId)
        {
            return CharacterMarketStatus.NotOwner;
        }

        if (owner.Level < options.MinLevel)
        {
            return CharacterMarketStatus.LevelTooLow;
        }

        var engaged = await EngagementAsync(connection, transaction, draft.CharacterId, ct);
        if (engaged != CharacterMarketStatus.Ok)
        {
            return engaged;
        }

        if (await CountAsync(connection, transaction, "SELECT COUNT(*) FROM character_market_listings WHERE character_id = @Id AND state = 0", draft.CharacterId, ct) > 0)
        {
            return CharacterMarketStatus.AlreadyListed;
        }

        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM character_market_listings WHERE seller_account_id = @Account AND state = 0 AND expires_at > @Now",
            new { Account = draft.SellerAccountId, draft.Now }, transaction, cancellationToken: ct)) >= options.MaxListingsPerAccount
            ? CharacterMarketStatus.TooManyListings
            : CharacterMarketStatus.Ok;
    }

    private static Task<CharacterOwnerRow?> OwnerAsync(DbConnection connection, DbTransaction transaction, long character, CancellationToken ct) =>
        connection.QuerySingleOrDefaultAsync<CharacterOwnerRow>(new CommandDefinition(
            "SELECT account_id, level FROM characters WHERE id = @Character", new { Character = character }, transaction, cancellationToken: ct));

    // A character in a clan, or standing as a stall, is tied to more than its owner.
    private static async Task<CharacterMarketStatus> EngagementAsync(DbConnection connection, DbTransaction transaction, long character, CancellationToken ct)
    {
        if (await CountAsync(connection, transaction, "SELECT COUNT(*) FROM clan_members WHERE character_id = @Id", character, ct) > 0)
        {
            return CharacterMarketStatus.InClan;
        }

        return await CountAsync(connection, transaction, "SELECT COUNT(*) FROM stalls WHERE character_id = @Id", character, ct) > 0
            ? CharacterMarketStatus.HasStall
            : CharacterMarketStatus.Ok;
    }

    private static Task<long> CountAsync(DbConnection connection, DbTransaction transaction, string sql, long id, CancellationToken ct) =>
        connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, new { Id = id }, transaction, cancellationToken: ct));

    private static async Task<CharacterMarketStore> RefusedPurchaseAsync(DbConnection connection, DbTransaction transaction, CharacterMarketPurchase purchase, CancellationToken ct)
    {
        var earlier = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT listing_id FROM character_market_sales WHERE operation_id = @Operation AND buyer_account_id = @Buyer",
            new { Operation = purchase.OperationId.ToString("D"), Buyer = purchase.BuyerAccountId }, transaction, cancellationToken: ct));
        if (earlier == purchase.ListingId)
        {
            var coins = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT coins FROM accounts WHERE id = @Buyer", new { Buyer = purchase.BuyerAccountId }, transaction, cancellationToken: ct));
            return new CharacterMarketStore(CharacterMarketStatus.Ok, coins, purchase.ListingId);
        }

        var row = await connection.QuerySingleOrDefaultAsync<ListingRow>(new CommandDefinition(
            $"SELECT {Fields} FROM character_market_listings WHERE id = @ListingId", new { purchase.ListingId }, transaction, cancellationToken: ct));
        if (row is null || row.State != Active || row.ExpiresAt <= purchase.Now || row.CharacterId is null)
        {
            return new CharacterMarketStore(CharacterMarketStatus.NotFound);
        }

        return new CharacterMarketStore(row.SellerAccountId == purchase.BuyerAccountId ? CharacterMarketStatus.OwnListing : CharacterMarketStatus.PriceChanged);
    }

    private static async Task<int?> FreeSlotAsync(DbConnection connection, DbTransaction transaction, CharacterMarketPurchase purchase, CancellationToken ct)
    {
        var used = await connection.QueryAsync<short>(new CommandDefinition(
            "SELECT slot FROM characters WHERE account_id = @Buyer", new { Buyer = purchase.BuyerAccountId }, transaction, cancellationToken: ct));
        return CharacterSlots.FirstFree(used) is { } slot && slot < purchase.MaxSlots ? slot : null;
    }

    private static async Task<CharacterMarketStatus> SaleRefusalAsync(DbConnection connection, DbTransaction transaction, CharacterListing listing, long character, int? slot, CancellationToken ct)
    {
        var owner = await OwnerAsync(connection, transaction, character, ct);
        if (owner is null || owner.AccountId != listing.SellerAccountId)
        {
            return CharacterMarketStatus.NotFound;
        }

        var engaged = await EngagementAsync(connection, transaction, character, ct);
        if (engaged != CharacterMarketStatus.Ok)
        {
            return engaged;
        }

        return slot is null ? CharacterMarketStatus.NoFreeSlot : CharacterMarketStatus.Ok;
    }

    // In account order, so two opposite purchases at once lock the same way round on PostgreSQL. The buyer's balance after, or null.
    private static async Task<long?> MoveCoinsAsync(DbConnection connection, DbTransaction transaction, long buyer, long seller, long price, long received, string name, CancellationToken ct)
    {
        long? balance = null;
        foreach (var account in new[] { Math.Min(buyer, seller), Math.Max(buyer, seller) })
        {
            if (account == buyer)
            {
                balance = await DapperCoinRepository.ChangeAsync(connection, transaction, buyer, -price, $"character market: bought {name}", ct);
                if (balance is null)
                {
                    return null;
                }
            }
            else if (await DapperCoinRepository.ChangeAsync(connection, transaction, seller, received, $"character market: sold {name}", ct) is null)
            {
                return null;
            }
        }

        return balance;
    }

    private static Task AuditAsync(DbConnection connection, DbTransaction transaction, long listingId, string action, long? actor, string detail, long now, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(InsertAudit, new { ListingId = listingId, Action = action, Actor = actor, Detail = detail, Now = now }, transaction, cancellationToken: ct));

    // One indexed read for the page's characters, so the cards draw them as they look now.
    private static async Task<IReadOnlyList<CharacterListing>> WithHairAsync(DbConnection connection, IEnumerable<ListingRow> rows, CancellationToken ct)
    {
        var listings = rows.Select(ToListing).ToList();
        var ids = listings.Where(static listing => listing.CharacterId is not null).Select(static listing => listing.CharacterId!.Value).ToArray();
        if (ids.Length == 0)
        {
            return listings;
        }

        var hair = (await connection.QueryAsync<HairRow>(new CommandDefinition("SELECT id, hair_index FROM characters WHERE id IN @ids", new { ids }, cancellationToken: ct)))
            .ToDictionary(static row => row.Id, static row => (int)row.HairIndex);
        return listings.Select(listing => listing.CharacterId is { } id && hair.TryGetValue(id, out var index) ? listing with { HairIndex = index } : listing).ToList();
    }

    private static CharacterListing ToListing(ListingRow row) => new(row.Id, row.CharacterId, row.SellerAccountId, row.SellerName, row.SellerHidden != 0, row.Name, (int)row.JobCode, (int)row.Level,
        (int)row.Tier, (int)row.ExpBasisPoints, (int)row.FaceIndex, row.ArmorKey, row.WeaponKey, row.OffHandKey, row.Price, row.Fee, (CharacterListingState)row.State, row.CreatedAt, row.ExpiresAt);

    private sealed class HairRow
    {
        public long Id { get; set; }
        public long HairIndex { get; set; }
    }

    private sealed class CharacterOwnerRow
    {
        public long AccountId { get; set; }
        public long Level { get; set; }
    }

    private sealed class ListingRow
    {
        public long Id { get; set; }
        public long? CharacterId { get; set; }
        public long SellerAccountId { get; set; }
        public string SellerName { get; set; } = "";
        public long SellerHidden { get; set; }
        public string Name { get; set; } = "";
        public long JobCode { get; set; }
        public long Level { get; set; }
        public long Tier { get; set; }
        public long ExpBasisPoints { get; set; }
        public long FaceIndex { get; set; }
        public string ArmorKey { get; set; } = "";
        public string WeaponKey { get; set; } = "";
        public string OffHandKey { get; set; } = "";
        public long Price { get; set; }
        public long Fee { get; set; }
        public short State { get; set; }
        public long CreatedAt { get; set; }
        public long ExpiresAt { get; set; }
    }

    private sealed class SaleRow
    {
        public long Id { get; set; }
        public string OperationId { get; set; } = "";
        public long ListingId { get; set; }
        public long? CharacterId { get; set; }
        public string CharacterName { get; set; } = "";
        public long SellerAccountId { get; set; }
        public long BuyerAccountId { get; set; }
        public long Price { get; set; }
        public long Tax { get; set; }
        public long SellerReceived { get; set; }
        public long CreatedAt { get; set; }
    }
}
