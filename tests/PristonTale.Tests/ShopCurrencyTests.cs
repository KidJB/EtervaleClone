using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Markets;
using PristonTale.Application.Services;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Coins;
using PristonTale.Persistence.Abstractions.Stalls;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Simulation.World;

namespace PristonTale.Tests;

// Offers priced in gold, coins or shown only; the wait between purchases; the seller hearing of a visit.
internal static class ShopCurrencyTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("shop currency: a view-only item lists at 0 and never sells, and coin offers bring no gold to the listing or the sale", RulesAsync);
        yield return ("shop currency: a coin sale moves the item and the coins between accounts together, a poor buyer changes nothing", CoinSaleAsync);
        yield return ("shop currency: a buyer waits the cooldown after a purchase, and a seller hears once who visited", CooldownAndVisitAsync);
        yield return ("shop currency: a stall's coin sale pays the owning account and keeps the currency in storage", StallCoinsAsync);
        yield return ("shop currency: the currency rides on the open request, the listing and the Marketplace page", WireAsync);
    }

    private static Task RulesAsync()
    {
        var seller = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Seller");
        var axe = new ItemInstance(ChatFixture.Axe);
        var potions = new ItemInstance(ChatFixture.Potion, 10);
        Assert.True(seller.Inventory.TryPlaceFirstFree(axe) && seller.Inventory.TryPlaceFirstFree(potions), "fixture items");
        PersonalShopEntry shown = new(axe.InstanceId, 0, 0, PersonalShopCurrency.ViewOnly);
        Assert.True(PersonalShopRules.TryOpen(seller, true, "Look", [shown with { UnitPrice = 5 }], out _) == PersonalShopStatus.Rejected, "a priced view-only item opened");
        Assert.True(PersonalShopRules.TryOpen(seller, true, "Look", [shown with { Currency = PersonalShopCurrency.Gold }], out _) == PersonalShopStatus.Rejected,
            "a free gold item opened");
        PersonalShopEntry coins = new(potions.InstanceId, 50_000_000, 2, PersonalShopCurrency.Coins);
        Assert.True(PersonalShopRules.TryOpen(seller, true, "Look", [shown, coins], out var shop) == PersonalShopStatus.Ok,
            "a view-only item and coin potions past the gold limit did not open");
        Assert.True(PersonalShopRules.Total(seller.Inventory, shop.Entries) == 0, "coin and view-only offers counted as gold");

        var buyer = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Buyer");
        var look = new PersonalShopPurchase(Guid.NewGuid(), 1, axe.InstanceId, 0, 1, 0);
        Assert.True(PersonalShopRules.TrySell(shop, seller.CloneForInventoryOperation(), buyer.CloneForInventoryOperation(), look, out _) == PersonalShopStatus.ViewOnly,
            "a view-only item sold");
        var sellerCopy = seller.CloneForInventoryOperation();
        var buyerCopy = buyer.CloneForInventoryOperation();
        var sale = new PersonalShopPurchase(Guid.NewGuid(), 1, potions.InstanceId, 50_000_000, 1, 0);
        Assert.True(PersonalShopRules.TrySell(shop, sellerCopy, buyerCopy, sale, out var price) == PersonalShopStatus.Ok && price == 50_000_000
            && buyerCopy.Money == buyer.Money && sellerCopy.Money == seller.Money && buyerCopy.Inventory.Items().Any(item => item.Definition == ChatFixture.Potion),
            "a coin sale moved gold, or not the potion");
        return Task.CompletedTask;
    }

    // Coins held per account; a trade writes the characters through the fixture's store, as the real repository does in its transaction.
    private sealed class MemoryCoins(ICharacterRepository characters) : ICoinRepository
    {
        public Dictionary<long, long> Balances { get; } = [];

        public Task<long> BalanceAsync(long accountId, CancellationToken ct = default) => Task.FromResult(Balances.GetValueOrDefault(accountId));

        public Task<long?> AddAsync(long accountId, long amount, string reason, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<long?> BuyWithCharacterAsync(CharacterRecord character, long price, string reason, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> OwnedSkinsAsync(long accountId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<long?> BuySkinAsync(long accountId, string itemKey, long price, string reason, long unlockedAt = 0, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<OwnedSkinRecord>> SkinRecordsAsync(long accountId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> UnlockSkinAsync(OwnedSkinRecord skin, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SkinUnlockStore> UnlockSkinWithCharacterAsync(CharacterRecord character, OwnedSkinRecord skin, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> ResetSkinsAsync(long accountId, CancellationToken ct = default) => throw new NotSupportedException();

        public async Task<CoinTradeStore> TradeWithCharactersAsync(IReadOnlyList<CharacterRecord> records, IReadOnlyList<CoinTransfer> transfers, CancellationToken ct = default)
        {
            if (transfers.Any(transfer => Balances.GetValueOrDefault(transfer.FromAccountId) < transfer.Amount))
            {
                return CoinTradeStore.NotEnoughCoins;
            }

            if (!await characters.UpdateAllAsync(records, ct))
            {
                return CoinTradeStore.Failed;
            }

            foreach (var transfer in transfers)
            {
                Balances[transfer.FromAccountId] -= transfer.Amount;
                Balances[transfer.ToAccountId] = Balances.GetValueOrDefault(transfer.ToAccountId) + transfer.Received;
            }

            return CoinTradeStore.Stored;
        }
    }

    private static async Task<(ChatFixture Fixture, PersonalShopAppService Shops, MemoryCoins Coins, ChatFixture.Player Seller, ChatFixture.Player Buyer, ItemInstance Axe, ItemInstance Potions)>
        OpenAsync(MarketOptionsHolder? options = null, Func<long>? nowMs = null)
    {
        var fixture = new ChatFixture(state: FieldState.Village);
        var coins = new MemoryCoins(fixture.Characters);
        var shops = new PersonalShopAppService(fixture.Sessions, new ImmediateSaves(fixture.Characters), fixture.Characters, fixture.World, fixture.Directory, coins: coins,
            options: options) { NowMs = nowMs ?? (static () => 0) };
        var seller = await fixture.JoinAsync("Seller", 100, 100);
        var buyer = await fixture.JoinAsync("Buyer", 140, 100);
        var axe = new ItemInstance(ChatFixture.Axe);
        var potions = new ItemInstance(ChatFixture.Potion, 10);
        Assert.True(seller.Session.Character!.Inventory.TryPlace(axe, 0) && seller.Session.Character.Inventory.TryPlace(potions, 5), "the seller's goods");
        buyer.Session.Character!.TryChangeMoney(5000 - buyer.Session.Character.Money);
        var opened = await fixture.RunAsync(shops.OpenAsync(seller.Session.SessionToken, "Goods",
            [new PersonalShopEntry(axe.InstanceId, 300, 0, PersonalShopCurrency.Coins), new PersonalShopEntry(potions.InstanceId, 50, 2)]));
        Assert.True(opened.Ok, $"the shop opened: {opened.Message}");
        return (fixture, shops, coins, seller, buyer, axe, potions);
    }

    private static PersonalShopPurchase Buy(ChatFixture.Player buyer, ChatFixture.Player seller, Guid item, long price, int count = 1) =>
        new(Guid.NewGuid(), seller.Session.EntityId, item, price, count, buyer.Session.Character!.InventoryRevision);

    private static async Task CoinSaleAsync()
    {
        var (fixture, shops, coins, seller, buyer, axe, _) = await OpenAsync();
        var (sellerAccount, buyerAccount) = (seller.Session.AccountId, buyer.Session.AccountId);
        coins.Balances[buyerAccount] = 100;
        var poor = await fixture.RunAsync(shops.BuyAsync(buyer.Session.SessionToken, Buy(buyer, seller, axe.InstanceId, 300)));
        Assert.True(poor.Outcome.Status == PersonalShopStatus.NotEnoughCoins && seller.Session.Character!.Inventory.Find(axe.InstanceId) >= 0
            && coins.Balances[buyerAccount] == 100, $"a poor buyer changed something: {poor.Outcome.Status}");

        coins.Balances[buyerAccount] = 1000;
        var bought = await fixture.RunAsync(shops.BuyAsync(buyer.Session.SessionToken, Buy(buyer, seller, axe.InstanceId, 300)));
        Assert.True(bought.Outcome.Ok, $"the coin purchase failed: {bought.Outcome.Status}");
        Assert.True(buyer.Session.Character!.Inventory.Find(axe.InstanceId) >= 0 && seller.Session.Character!.Inventory.Find(axe.InstanceId) < 0, "the axe did not move");
        Assert.True(coins.Balances[buyerAccount] == 700 && coins.Balances[sellerAccount] == 300 && buyer.Session.Character.Money == 5000,
            "the coins did not move, or gold moved instead");
    }

    private static async Task CooldownAndVisitAsync()
    {
        var now = 0L;
        var (fixture, shops, _, seller, buyer, _, potions) = await OpenAsync(new MarketOptionsHolder { PurchaseCooldownMs = 2000 }, () => now);
        var view = await fixture.RunAsync(shops.ViewAsync(buyer.Session.SessionToken, seller.Session.EntityId));
        Assert.True(view.Listing is not null && seller.Link.Last == PersonalShopMessages.Visited("Buyer"), $"the seller did not hear of the visit: {seller.Link.Last}");
        var heard = seller.Link.Lines.Count;
        await fixture.RunAsync(shops.ViewAsync(buyer.Session.SessionToken, seller.Session.EntityId));
        Assert.True(seller.Link.Lines.Count == heard, "the same visitor was announced twice within the minute");

        Assert.True((await fixture.RunAsync(shops.BuyAsync(buyer.Session.SessionToken, Buy(buyer, seller, potions.InstanceId, 50)))).Outcome.Ok, "the first purchase failed");
        now = 1000;
        var soon = await fixture.RunAsync(shops.BuyAsync(buyer.Session.SessionToken, Buy(buyer, seller, potions.InstanceId, 50)));
        Assert.True(soon.Outcome.Status == PersonalShopStatus.TooSoon, $"a purchase within the cooldown went through: {soon.Outcome.Status}");
        now = 2500;
        Assert.True((await fixture.RunAsync(shops.BuyAsync(buyer.Session.SessionToken, Buy(buyer, seller, potions.InstanceId, 50)))).Outcome.Ok, "the purchase after the cooldown failed");
    }

    private static async Task StallCoinsAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-stall-coins-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            await using (var connection = await provider.GetRequiredService<IDbConnectionFactory>().OpenAsync())
            {
                await connection.ExecuteAsync("INSERT INTO accounts (user_id, password_hash, coins) VALUES ('ann', 'x', 0), ('bob', 'x', 500)");
            }

            var characters = provider.GetRequiredService<ICharacterRepository>();
            var registry = new CharacterClassRegistry();
            var ann = await characters.CreateAsync(CharacterRecordFactory.NewStarter(registry, 1, "Ann", 0, PlayerClass.Fighter, 3));
            var bob = await characters.CreateAsync(CharacterRecordFactory.NewStarter(registry, 2, "Bob", 0, PlayerClass.Fighter, 3));
            var stock = StoreGrid.Create();
            var potion = new ItemInstance(ChatFixture.Potion, 3);
            stock.TryPlace(potion, 0);
            var shop = StallRules.Listing("Sign", [new PersonalShopEntry(potion.InstanceId, 100, 0, PersonalShopCurrency.Coins)]);
            var items = StallCodec.CaptureItems(shop, stock);
            Assert.True(StallCodec.RestoreItems(items, new ItemCatalog([ChatFixture.Potion])).Entries is [{ Currency: PersonalShopCurrency.Coins }], "the currency was not stored");

            var stalls = provider.GetRequiredService<IStallRepository>();
            var look = StallCodec.CaptureLook(new StallLook("Ann", 1, 0, 0, 0, "", "", "", "", "", "Sign", 1));
            var id = await stalls.CreateWithCharacterAsync(ann, new StallRecord(0, 1, ann.Id, 3, 0, 0, 0, 0, look, items));
            var sale = new StallSale(bob, id!.Value, items, false, ann.Id, 300, 0, null, Coins: true, OwnerAccountId: 1);
            Assert.True(await stalls.SellAsync(sale with { Price = 600 }) == StallSaleStore.NotEnoughCoins, "a poor buyer paid in coins");
            var coins = provider.GetRequiredService<ICoinRepository>();
            Assert.True(await stalls.SellAsync(sale) == StallSaleStore.Stored && await coins.BalanceAsync(1) == 300 && await coins.BalanceAsync(2) == 200,
                "the stall's coins did not reach the owning account");
            Assert.True((await characters.FindByNameForAccountAsync(1, "Ann"))!.Money == ann.Money, "a coin sale paid the owner gold");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task WireAsync()
    {
        var buffer = new byte[8192];
        var item = Guid.NewGuid();
        PersonalShopEntryWire[] entries = [new(item, 0, 0, (byte)PersonalShopCurrency.ViewOnly), new(Guid.NewGuid(), 50, 4, (byte)PersonalShopCurrency.Coins)];
        var open = PersonalShopOpenPacket.Read(buffer.AsSpan(0, new PersonalShopOpenPacket(Guid.NewGuid(), "Sign", entries).Write(buffer)));
        Assert.True(open.Entries.SequenceEqual(entries), "the open request lost the currency");
        var entry = new StoreItemEntry(item, 0x0101_0100, (byte)ItemKind.Axe, 1, 0, 2, 3, "Stone Axe");
        PersonalShopOfferWire[] offers = [new(entry, 300, (byte)PersonalShopCurrency.Coins)];
        var listing = PersonalShopListingPacket.Read(buffer.AsSpan(0, new PersonalShopListingPacket(Guid.NewGuid(), 9, offers).Write(buffer)));
        Assert.True(listing.Offers.SequenceEqual(offers), "the listing lost the currency");
        var page = MarketplacePagePacket.Read(buffer.AsSpan(0, new MarketplacePagePacket(Guid.NewGuid(), 0, 0, 1,
            [new MarketplaceOfferWire(9, "Ann", "Ricarten", false, entry, 0, (byte)PersonalShopCurrency.ViewOnly)]).Write(buffer)));
        Assert.True(page.Offers is [{ Currency: (byte)PersonalShopCurrency.ViewOnly }], "the Marketplace page lost the currency");
        return Task.CompletedTask;
    }
}
