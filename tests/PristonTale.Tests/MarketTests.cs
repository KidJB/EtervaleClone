using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Markets;
using PristonTale.Application.Services;
using PristonTale.Application.Settings;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Contracts.World;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Clans;
using PristonTale.Persistence.Abstractions.Coins;
using PristonTale.Persistence.Abstractions.Markets;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Markets;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

// The Character Market on real storage (listing rules, sale, tax, rollback, a race) and the Marketplace over live shops.
internal static class MarketTests
{
    private const long Now = 1_000_000;
    private const int Level = 60;
    private static readonly CharacterMarketOptions Rules = new() { MinLevel = 50, MaxListingsPerAccount = 2, MinPrice = 100, MaxPrice = 10_000, ListingFee = 10, ExpiryDays = 7, TaxPercent = 5 };

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("character market: listing refuses a low level, a clan member, a stall owner, a stranger, a repeat and past the account's limit, and takes the fee", ListingAsync);
        yield return ("character market: browsing searches, filters, sorts and pages, a cancelled listing leaves it, and an expired one closes", BrowseCancelExpireAsync);
        yield return ("character market: a sale moves the character to the buyer's first free slot with its data intact, pays the seller less the tax and logs it once", SaleAsync);
        yield return ("character market: a failed sale changes nothing", RollbackAsync);
        yield return ("character market: two buyers at once, only one gets the character and pays", RaceAsync);
        yield return ("character market: a poor buyer, a full account, the seller's own listing and a changed price are refused", RefusalsAsync);
        yield return ("character market: a character in play cannot be listed and a disabled market refuses", ServiceAsync);
        yield return ("marketplace: it shows live shops, filters them, buys from afar, and a closed shop or a removed item leaves it", MarketplaceAsync);
        yield return ("marketplace: no gold, a full bag and an offer already sold are refused, and nothing is duplicated", MarketplaceRefusalsAsync);
        yield return ("market messages round-trip on the wire, and the settings table reaches the options", WireAndSettingsAsync);
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"priston-market-{Guid.NewGuid():N}");
        private ServiceProvider _provider = null!;

        public ICharacterMarketRepository Market => _provider.GetRequiredService<ICharacterMarketRepository>();
        public ICharacterRepository Characters => _provider.GetRequiredService<ICharacterRepository>();
        public ICoinRepository Coins => _provider.GetRequiredService<ICoinRepository>();
        public IDbConnectionFactory Connections => _provider.GetRequiredService<IDbConnectionFactory>();

        public static async Task<Store> OpenAsync()
        {
            var store = new Store();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPristonSqlite(Path.Combine(store._directory, "world.db"));
            store._provider = services.BuildServiceProvider();
            await store._provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            await using var connection = await store.Connections.OpenAsync();
            await connection.ExecuteAsync("INSERT INTO accounts (user_id, password_hash, coins) VALUES ('ann', 'x', 1000), ('bob', 'x', 5000), ('cara', 'x', 5000)");
            return store;
        }

        public async Task<CharacterRecord> CharacterAsync(long account, string name, short slot, int level = Level)
        {
            var created = await Characters.CreateAsync(CharacterRecordFactory.NewStarter(new CharacterClassRegistry(), account, name, slot, PlayerClass.Fighter, 3));
            await using var connection = await Connections.OpenAsync();
            await connection.ExecuteAsync("UPDATE characters SET level = @level, money = 777 WHERE id = @Id", new { level, created.Id });
            return (await Characters.FindByNameForAccountAsync(account, name))!;
        }

        public async Task<long> SqlAsync(string sql, object? values = null)
        {
            await using var connection = await Connections.OpenAsync();
            return await connection.ExecuteScalarAsync<long>(sql, values);
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static CharacterListingDraft Draft(CharacterRecord record, long price = 1000, long now = Now) =>
        new(Guid.NewGuid(), record.Id, record.AccountId, "Seller", false, record.Name, record.JobCode, record.Level, record.Tier, 0, record.FaceIndex, "", "", "", price, now);

    private static CharacterMarketPurchase Purchase(long listing, long buyer, long price = 1000) => new(Guid.NewGuid(), listing, buyer, price, Rules.TaxPercent, CharacterSlots.Maximum, Now + 1);

    private static async Task ListingAsync()
    {
        await using var store = await Store.OpenAsync();
        var low = await store.CharacterAsync(1, "Lowly", 0, 10);
        var clan = await store.CharacterAsync(1, "Clanned", 1);
        var stall = await store.CharacterAsync(1, "Staller", 2);
        var first = await store.CharacterAsync(1, "First", 3);
        var second = await store.CharacterAsync(1, "Second", 4);
        var third = await store.CharacterAsync(1, "Third", 5);
        var foreign = await store.CharacterAsync(2, "Foreign", 0);
        await store.SqlAsync("INSERT INTO clans (name, name_key, state, created_at, deadline_at, intro, mark_version) VALUES ('C', 'c', 0, 0, 0, '', 0); SELECT 1");
        await store.SqlAsync("INSERT INTO clan_members (character_id, clan_id, rank, joined_at) VALUES (@Id, 1, 0, 0); SELECT 1", new { clan.Id });
        await store.SqlAsync("INSERT INTO stalls (account_id, character_id, field_id, x, y, z, facing, look, items) VALUES (1, @Id, 3, 0, 0, 0, 0, '', ''); SELECT 1", new { stall.Id });

        Assert.True((await store.Market.ListAsync(Draft(low), Rules)).Status == CharacterMarketStatus.LevelTooLow, "a low level listed");
        Assert.True((await store.Market.ListAsync(Draft(clan), Rules)).Status == CharacterMarketStatus.InClan, "a clan member listed");
        Assert.True((await store.Market.ListAsync(Draft(stall), Rules)).Status == CharacterMarketStatus.HasStall, "a stall owner listed");
        Assert.True((await store.Market.ListAsync(Draft(foreign with { AccountId = 1 }), Rules)).Status == CharacterMarketStatus.NotOwner, "another account's character listed");

        var listed = await store.Market.ListAsync(Draft(first), Rules);
        Assert.True(listed.Status == CharacterMarketStatus.Ok && listed.Balance == 990, "the fee was not taken");
        Assert.True((await store.Market.ListAsync(Draft(first), Rules)).Status == CharacterMarketStatus.AlreadyListed, "a character listed twice");
        Assert.True((await store.Market.ListAsync(Draft(second), Rules)).Status == CharacterMarketStatus.Ok, "the second listing was refused");
        Assert.True((await store.Market.ListAsync(Draft(third), Rules)).Status == CharacterMarketStatus.TooManyListings, "the account's limit was passed");
        Assert.True(await store.Market.IsListedAsync(first.Id, Now) && !await store.Market.IsListedAsync(third.Id, Now), "the listed state is wrong");

        var poor = Rules with { ListingFee = 100_000, MaxListingsPerAccount = 6 };
        Assert.True((await store.Market.ListAsync(Draft(third), poor)).Status == CharacterMarketStatus.NotEnoughCoins && await store.Coins.BalanceAsync(1) == 980,
            "a fee beyond the balance listed or moved coins");
    }

    private static async Task BrowseCancelExpireAsync()
    {
        await using var store = await Store.OpenAsync();
        var options = Rules with { MaxListingsPerAccount = 6, ListingFee = 0 };
        var alpha = await store.CharacterAsync(1, "Alpha", 0);
        var beta = await store.CharacterAsync(1, "Beta", 1, 90);
        var alphaId = (await store.Market.ListAsync(Draft(alpha, 500), options)).ListingId;
        var betaId = (await store.Market.ListAsync(Draft(beta, 900, Now + 5), options)).ListingId;

        var newest = await store.Market.BrowseAsync(new CharacterListingQuery(0, 0, CharacterMarketSort.Newest, "", Now + 10));
        Assert.True(newest.Total == 2 && newest.Listings[0].Id == betaId, "newest first failed");
        var cheap = await store.Market.BrowseAsync(new CharacterListingQuery(0, 0, CharacterMarketSort.PriceLow, "", Now + 10));
        Assert.True(cheap.Listings[0].Id == alphaId, "price order failed");
        var level = await store.Market.BrowseAsync(new CharacterListingQuery(0, 0, CharacterMarketSort.LevelHigh, "", Now + 10));
        Assert.True(level.Listings[0].Id == betaId, "level order failed");
        Assert.True((await store.Market.BrowseAsync(new CharacterListingQuery(0, 0, CharacterMarketSort.Newest, "LPH", Now + 10))) is { Total: 1 } found && found.Listings[0].Id == alphaId, "the search failed");
        Assert.True((await store.Market.BrowseAsync(new CharacterListingQuery(0, 0, CharacterMarketSort.Newest, "%", Now + 10))).Total == 0, "a wildcard matched everything");
        Assert.True((await store.Market.BrowseAsync(new CharacterListingQuery(0, (int)PlayerClass.Knight, CharacterMarketSort.Newest, "", Now + 10))).Total == 0, "the class filter failed");
        Assert.True((await store.Market.BrowseAsync(new CharacterListingQuery(1, 0, CharacterMarketSort.Newest, "", Now + 10))).Listings.Count == 0, "the second page was not empty");

        Assert.True((await store.Market.CancelAsync(alphaId, 2, Now + 11)).Status == CharacterMarketStatus.NotFound, "another account cancelled");
        Assert.True((await store.Market.CancelAsync(alphaId, 1, Now + 11)).Status == CharacterMarketStatus.Ok && !await store.Market.IsListedAsync(alpha.Id, Now + 11), "the cancel did not free the character");
        Assert.True((await store.Market.BrowseAsync(new CharacterListingQuery(0, 0, CharacterMarketSort.Newest, "", Now + 12))).Total == 1, "a cancelled listing still shows");

        var past = Now + 8 * CharacterMarketRules.SecondsPerDay;
        Assert.True((await store.Market.BrowseAsync(new CharacterListingQuery(0, 0, CharacterMarketSort.Newest, "", past))).Total == 0 && !await store.Market.IsListedAsync(beta.Id, past), "an expired listing still counts");
        Assert.True(await store.Market.ExpireAsync(past) == 1 && (await store.Market.FindAsync(betaId))!.State == CharacterListingState.Expired, "the listing did not expire");
        Assert.True((await store.Market.BuyAsync(Purchase(betaId, 2, 900) with { Now = past })).Status == CharacterMarketStatus.NotFound, "an expired listing sold");
    }

    private static async Task SaleAsync()
    {
        await using var store = await Store.OpenAsync();
        var sold = await store.CharacterAsync(1, "Sold", 2);
        await store.CharacterAsync(2, "Kept", 0);
        var listing = (await store.Market.ListAsync(Draft(sold), Rules)).ListingId;
        var purchase = Purchase(listing, 2);
        var bought = await store.Market.BuyAsync(purchase);
        Assert.True(bought.Status == CharacterMarketStatus.Ok && bought.Balance == 4000, "the buyer was not charged 1000");
        Assert.True(await store.Coins.BalanceAsync(1) == 990 + 950, "the seller did not receive the price less a 5% tax");

        var moved = (await store.Characters.FindByNameForAccountAsync(2, "Sold"))!;
        Assert.True(moved.AccountId == 2 && moved.Slot == 1 && await store.Characters.FindByNameForAccountAsync(1, "Sold") is null, "the owner or slot is wrong");
        Assert.True(moved with { AccountId = sold.AccountId, Slot = sold.Slot, LastPlayedAt = sold.LastPlayedAt } == sold, "the character's data changed");
        Assert.True((await store.Market.FindAsync(listing))!.State == CharacterListingState.Sold, "the listing is not marked sold");
        Assert.True((await store.Market.RecentSalesAsync(5)) is [{ Price: 1000, Tax: 50, SellerReceived: 950, BuyerAccountId: 2 }], "the sale was not recorded");
        Assert.True(await store.SqlAsync("SELECT COUNT(*) FROM character_market_audit WHERE action = 'sold'") == 1, "the sale was not audited");

        var replay = await store.Market.BuyAsync(purchase);
        Assert.True(replay.Status == CharacterMarketStatus.Ok && replay.Balance == 4000 && await store.Coins.BalanceAsync(2) == 4000, "a retried purchase paid twice");
        Assert.True((await store.Market.BuyAsync(Purchase(listing, 3))).Status == CharacterMarketStatus.NotFound, "a sold listing sold again");
    }

    private static async Task RollbackAsync()
    {
        await using var store = await Store.OpenAsync();
        var character = await store.CharacterAsync(1, "Safe", 0);
        var listing = (await store.Market.ListAsync(Draft(character), Rules)).ListingId;
        await store.SqlAsync("CREATE TRIGGER fail_sale BEFORE INSERT ON character_market_sales BEGIN SELECT RAISE(ABORT, 'refused'); END; SELECT 1");
        await Assert.ThrowsAsync<System.Data.Common.DbException>(() => store.Market.BuyAsync(Purchase(listing, 2)), "the sale did not fail");
        Assert.True((await store.Market.FindAsync(listing))!.State == CharacterListingState.Active, "the listing stayed sold");
        Assert.True(await store.Coins.BalanceAsync(2) == 5000 && await store.Coins.BalanceAsync(1) == 990, "coins moved");
        Assert.True((await store.Characters.FindByNameForAccountAsync(1, "Safe")) is { Slot: 0 }, "the character moved");
    }

    private static async Task RaceAsync()
    {
        await using var store = await Store.OpenAsync();
        var character = await store.CharacterAsync(1, "Wanted", 0);
        var listing = (await store.Market.ListAsync(Draft(character), Rules)).ListingId;
        var results = await Task.WhenAll(Task.Run(() => store.Market.BuyAsync(Purchase(listing, 2))), Task.Run(() => store.Market.BuyAsync(Purchase(listing, 3))));
        Assert.True(results.Count(static result => result.Status == CharacterMarketStatus.Ok) == 1, $"not exactly one buyer won: {results[0].Status}, {results[1].Status}");
        Assert.True(await store.Coins.BalanceAsync(2) + await store.Coins.BalanceAsync(3) == 9000, "both buyers paid, or neither");
        Assert.True(await store.SqlAsync("SELECT COUNT(*) FROM character_market_sales") == 1, "two sales were recorded");
    }

    private static async Task RefusalsAsync()
    {
        await using var store = await Store.OpenAsync();
        var character = await store.CharacterAsync(1, "Dear", 0);
        var listing = (await store.Market.ListAsync(Draft(character, 9000), Rules)).ListingId;
        Assert.True((await store.Market.BuyAsync(Purchase(listing, 2, 9000))).Status == CharacterMarketStatus.NotEnoughCoins, "a poor buyer bought");
        Assert.True((await store.Market.FindAsync(listing))!.State == CharacterListingState.Active, "the listing did not reopen");
        Assert.True((await store.Market.BuyAsync(Purchase(listing, 1, 9000))).Status == CharacterMarketStatus.OwnListing, "the seller bought its own");
        Assert.True((await store.Market.BuyAsync(Purchase(listing, 2, 100))).Status == CharacterMarketStatus.PriceChanged, "a stale price sold");

        var cheap = await store.CharacterAsync(1, "Cheap", 1);
        var cheapListing = (await store.Market.ListAsync(Draft(cheap, 100), Rules)).ListingId;
        for (short slot = 0; slot < CharacterSlots.Maximum; slot++)
        {
            await store.CharacterAsync(3, $"Cara{slot}", slot);
        }

        Assert.True((await store.Market.BuyAsync(Purchase(cheapListing, 3, 100))).Status == CharacterMarketStatus.NoFreeSlot && await store.Coins.BalanceAsync(3) == 5000, "a full account bought");
    }

    private static async Task<(ChatFixture Fixture, ulong NpcId)> MarketFixtureAsync()
    {
        var npc = new NpcDefinition(1, "Trader", "trader.inx", [new NpcService(NpcServiceKind.CharacterMarket), new NpcService(NpcServiceKind.Marketplace)]) { DisplayName = "Trader", MoveRange = 0 };
        var fixture = new ChatFixture(state: FieldState.Village, npcs: new NpcCatalog([npc]), fixedNpcs: [new FixedNpc(1, 100 * 256, 100 * 256, 0)]);
        var scout = await fixture.JoinAsync("Scout", 5000, 5000);
        fixture.Tick(2);
        return (fixture, ChatFixture.Latest(scout)!.Value.Entities.First(static entity => entity.Kind == EntityKind.Npc).EntityId);
    }

    private static async Task ServiceAsync()
    {
        await using var store = await Store.OpenAsync();
        var (fixture, npc) = await MarketFixtureAsync();
        var seller = await fixture.JoinAsync("Merchant", 100, 100);
        Assert.True(seller.Session.AccountId == 2, "the fixture's account numbers moved");
        var free = await store.CharacterAsync(2, "Free", 0);
        var busy = await store.CharacterAsync(2, "Busy", 1);
        await store.CharacterAsync(2, "Top", 2, Simulation.Progression.ExperienceTable.MaximumLevel);
        var options = new MarketOptionsHolder { Character = Rules with { ListingFee = 0 } };
        var classes = new CharacterClassRegistry();
        var summaries = new CharacterAppService(fixture.Sessions, store.Characters, classes, fixture.Data, fixture.Catalog);
        var market = new CharacterMarketAppService(fixture.Sessions, store.Market, store.Characters, summaries, fixture.World, fixture.Data, options);
        var token = seller.Session.SessionToken;

        var holder = await fixture.JoinAsync("Holder", 5000, 200);
        Assert.True(fixture.Sessions.TryClaimCharacter(busy.Id, holder.Session.SessionToken), "fixture claim");
        var inPlay = await fixture.RunAsync(market.ListAsync(token, npc, Guid.NewGuid(), "Busy", 500, false));
        Assert.True(inPlay.Status == CharacterMarketStatus.InPlay, "a character in play was listed");
        var listed = await fixture.RunAsync(market.ListAsync(token, npc, Guid.NewGuid(), "Free", 500, true));
        Assert.True(listed.Status == CharacterMarketStatus.Ok, $"the listing was refused: {listed.Status}");
        Assert.True(fixture.Sessions.TryClaimCharacter(free.Id, "next"), "the listing kept the character's claim");
        fixture.Sessions.ReleaseCharacter(free.Id, "next");

        var (_, forSale) = await fixture.RunAsync(market.CharactersForSaleAsync(token, npc));
        Assert.True(forSale.Single(static row => row.Name == "Busy").Blocked == CharacterMarketStatus.InPlay
            && forSale.Single(static row => row.Name == "Free").Blocked == CharacterMarketStatus.AlreadyListed
            && forSale.Single(static row => row.Name == "Top").ExpBasisPoints == 0, "the sale window's reasons are wrong");
        var away = await fixture.JoinAsync("Away", 5000, 100);
        Assert.True((await fixture.RunAsync(market.BrowseAsync(away.Session.SessionToken, npc, 0, 0, CharacterMarketSort.Newest, ""))).Status == CharacterMarketStatus.OutOfReach, "the market opened from afar");
        options.Character = options.Character with { Enabled = false };
        Assert.True((await fixture.RunAsync(market.BrowseAsync(token, npc, 0, 0, CharacterMarketSort.Newest, ""))).Status == CharacterMarketStatus.Disabled, "a disabled market answered");
    }

    private static MarketplaceQuery Any(string item = "", string seller = "") => new(0, MarketplaceCategory.All, 0, null, ItemRarity.None, 0, 0, MarketplaceSort.Newest, item, seller);

    private static PersonalShopPurchase Buy(ChatFixture.Player buyer, MarketplaceOffer offer, int count) =>
        new(Guid.NewGuid(), offer.SellerId, offer.Item.InstanceId, offer.UnitPrice, count, buyer.Session.Character!.InventoryRevision);

    private static async Task<(ChatFixture Fixture, ulong Npc, MarketplaceAppService Market, PersonalShopAppService Shops, ChatFixture.Player Seller, ChatFixture.Player Buyer, ItemInstance Axe)> ShopFixtureAsync()
    {
        var (fixture, npc) = await MarketFixtureAsync();
        var shops = new PersonalShopAppService(fixture.Sessions, new ImmediateSaves(fixture.Characters), fixture.Characters, fixture.World, fixture.Directory);
        var market = new MarketplaceAppService(fixture.Sessions, fixture.World, shops, new MarketOptionsHolder());
        var seller = await fixture.JoinAsync("Seller", 4000, 4000);
        var buyer = await fixture.JoinAsync("Buyer", 100, 100);
        var axe = new ItemInstance(ChatFixture.Axe);
        var potions = new ItemInstance(ChatFixture.Potion, 10);
        Assert.True(seller.Session.Character!.Inventory.TryPlace(axe, 0) && seller.Session.Character.Inventory.TryPlace(potions, 5), "the seller's goods");
        seller.Session.Character.TryChangeMoney(-seller.Session.Character.Money);
        buyer.Session.Character!.TryChangeMoney(5000 - buyer.Session.Character.Money);
        Assert.True((await fixture.RunAsync(shops.OpenAsync(seller.Session.SessionToken, "Goods", [new PersonalShopEntry(axe.InstanceId, 1000, 0), new PersonalShopEntry(potions.InstanceId, 50, 2)]))).Ok, "the shop opened");
        return (fixture, npc, market, shops, seller, buyer, axe);
    }

    private static async Task MarketplaceAsync()
    {
        var (fixture, npc, market, shops, seller, buyer, axe) = await ShopFixtureAsync();
        var token = buyer.Session.SessionToken;
        var page = await fixture.RunAsync(market.BrowseAsync(token, npc, Any()));
        Assert.True(page is { Status: MarketplacePageStatus.Ok, Total: 2 } && page.Offers.All(static offer => offer is { Seller: "Seller", Own: false, Location: "Fixture Field" }), "the shop was not listed");
        Assert.True((await fixture.RunAsync(market.BrowseAsync(token, npc, Any("axe")))).Total == 1, "the item search failed");
        Assert.True((await fixture.RunAsync(market.BrowseAsync(token, npc, Any(seller: "nobody")))).Total == 0, "the seller search failed");
        Assert.True((await fixture.RunAsync(market.BrowseAsync(token, npc, Any() with { Category = MarketplaceCategory.Weapons }))).Total == 1, "the category filter failed");
        Assert.True((await fixture.RunAsync(market.BrowseAsync(token, npc, Any() with { MinPrice = 500 }))).Total == 1, "the price filter failed");
        Assert.True((await fixture.RunAsync(market.BrowseAsync(token, npc, Any() with { Rarity = ItemRarity.Legendary }))).Total == 0, "the rarity filter failed");
        var cheapest = await fixture.RunAsync(market.BrowseAsync(token, npc, Any() with { Sort = MarketplaceSort.PriceLow }));
        Assert.True(cheapest.Offers[0].UnitPrice == 50, "the price order failed");
        Assert.True((await fixture.RunAsync(market.BrowseAsync(seller.Session.SessionToken, npc, Any()))).Status == MarketplacePageStatus.OutOfReach, "the market opened away from its NPC");

        var offer = page.Offers.Single(entry => entry.Item.InstanceId == axe.InstanceId);
        var direct = await fixture.RunAsync(shops.BuyAsync(token, Buy(buyer, offer, 1)));
        Assert.True(direct.Outcome.Status == PersonalShopStatus.OutOfReach, "a direct purchase from afar went through");
        var bought = await fixture.RunAsync(market.BuyAsync(token, npc, Buy(buyer, offer, 1)));
        Assert.True(bought.Outcome.Ok, $"the Marketplace purchase failed: {bought.Outcome.Status}");
        Assert.True(buyer.Session.Character!.Inventory.Find(axe.InstanceId) >= 0 && seller.Session.Character!.Inventory.Find(axe.InstanceId) < 0, "the axe did not move");
        Assert.True(buyer.Session.Character!.Money == 4000 && seller.Session.Character!.Money == 1000, "the gold did not move");
        Assert.True((await fixture.RunAsync(market.BrowseAsync(token, npc, Any()))).Total == 1, "a sold offer still shows");

        var potionId = seller.Session.Character!.Inventory.Items().Single(static item => item.Definition == ChatFixture.Potion).InstanceId;
        seller.Session.Character.Inventory.TakeAt(seller.Session.Character.Inventory.Find(potionId));
        Assert.True((await fixture.RunAsync(market.BrowseAsync(token, npc, Any()))).Total == 0, "an item that left the bag still shows");
    }

    private static async Task MarketplaceRefusalsAsync()
    {
        var (fixture, npc, market, shops, seller, buyer, axe) = await ShopFixtureAsync();
        var token = buyer.Session.SessionToken;
        var page = await fixture.RunAsync(market.BrowseAsync(token, npc, Any("axe")));
        var offer = page.Offers.Single();

        buyer.Session.Character!.TryChangeMoney(-buyer.Session.Character.Money);
        Assert.True((await fixture.RunAsync(market.BuyAsync(token, npc, Buy(buyer, offer, 1)))).Outcome.Status == PersonalShopStatus.NotEnoughGold, "a poor buyer bought");
        buyer.Session.Character.TryChangeMoney(5000);

        var filler = new ItemDefinition(0x0101_0200, "Filler", ItemKind.Misc, GridWidth: 2, GridHeight: 3);
        while (buyer.Session.Character.Inventory.TryPlaceFirstFree(new ItemInstance(filler)))
        {
        }

        Assert.True((await fixture.RunAsync(market.BuyAsync(token, npc, Buy(buyer, offer, 1)))).Outcome.Status == PersonalShopStatus.NoSpace, "a full bag took the axe");
        Assert.True(seller.Session.Character!.Inventory.Find(axe.InstanceId) >= 0, "the axe left the seller on a refused purchase");
        foreach (var item in buyer.Session.Character.Inventory.Items().ToArray())
        {
            buyer.Session.Character.Inventory.TakeAt(buyer.Session.Character.Inventory.Find(item.InstanceId));
        }

        var other = await fixture.JoinAsync("Other", 100, 100);
        other.Session.Character!.TryChangeMoney(5000 - other.Session.Character.Money);
        Assert.True((await fixture.RunAsync(market.BuyAsync(token, npc, Buy(buyer, offer, 1)))).Outcome.Ok, "the first buyer was refused");
        var late = await fixture.RunAsync(market.BuyAsync(other.Session.SessionToken, npc, Buy(other, offer, 1)));
        Assert.True(late.Outcome.Status == PersonalShopStatus.SoldOut && other.Session.Character.Inventory.Find(axe.InstanceId) < 0, "the axe sold twice");
        Assert.True(other.Session.Character.Money == 5000, "a stale buyer paid");

        Assert.True((await fixture.RunAsync(shops.CloseAsync(seller.Session.SessionToken))).Ok, "the shop did not close");
        Assert.True((await fixture.RunAsync(market.BrowseAsync(token, npc, Any()))).Total == 0, "a closed shop still shows");
        var stale = await fixture.RunAsync(market.BuyAsync(other.Session.SessionToken, npc, Buy(other, offer, 1)));
        Assert.True(stale.Outcome.Status == PersonalShopStatus.Closed, "a closed shop sold");
    }

    private static Task WireAndSettingsAsync()
    {
        var buffer = new byte[8192];
        var request = new CharacterMarketRequestPacket(Guid.NewGuid(), MarketWire.ActionBuy, 7, 9, 100, 2, 3, 1, true, "name");
        Assert.True(CharacterMarketRequestPacket.Read(buffer.AsSpan(0, request.Write(buffer))) == request, "request round trip");
        CharacterMarketRow[] rows = [new(1, "A", "B", 1, 60, 1, 625, 100, 3, 4, 0, 0)];
        var page = new CharacterMarketPagePacket(request.OperationId, 0, 1, 50, 0, 1, 10, 5, 50, 100, 1000, rows);
        Assert.True(CharacterMarketPagePacket.Read(buffer.AsSpan(0, page.Write(buffer))).Rows.Single() == rows[0], "page round trip");
        var query = new MarketplaceQueryPacket(Guid.NewGuid(), 7, 1, 2, 3, 4, 5, 1, 10, 20, "axe", "bob");
        Assert.True(MarketplaceQueryPacket.Read(buffer.AsSpan(0, query.Write(buffer))) == query, "query round trip");
        var buy = new MarketplaceBuyPacket(Guid.NewGuid(), 1, 2, Guid.NewGuid(), 3, 4, 5);
        Assert.True(MarketplaceBuyPacket.Read(buffer.AsSpan(0, buy.Write(buffer))) == buy, "buy round trip");
        Assert.True(CharacterMarketPagePacket.MaxSize < 8192 && MarketplacePagePacket.MaxSize < 8192, "a market page outgrew the frame");

        var problems = new List<string>();
        var settings = ServerSettingCatalog.Read(new Dictionary<string, string> { ["market.character.tax_percent"] = "12", ["market.character.enabled"] = "false", ["market.shops.enabled"] = "false" }, null, problems);
        Assert.True(problems.Count == 0 && settings.CharacterMarket is { TaxPercent: 12, Enabled: false } && !settings.MarketplaceEnabled, "the market settings were not read");
        Assert.True(CharacterMarketRules.Tax(101, 5) == 6 && CharacterMarketRules.Tax(100, 0) == 0, "the tax rounds up");
        return Task.CompletedTask;
    }
}
