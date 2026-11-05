using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Services;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Coins;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Shops;

namespace PristonTale.Tests;

// The coin shop: its prices and bulk discount, the catalog file, its messages, and the coins taken with the bought item
// in one transaction (or neither).
internal static class CoinShopTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("coin shop: a discount replaces the price and bulk takes its percent per five bought, capped", PricingAsync);
        yield return ("coin shop: the catalog file is read with its tabs and articles, and a missing file leaves an empty shop", CatalogAsync);
        yield return ("coin shop: tabs, catalog, purchase and result travel on the wire", WireAsync);
        yield return ("coin shop: coins are granted and logged, never go below zero, and a purchase writes coins and character together", StorageAsync);
    }

    private static Task PricingAsync()
    {
        var potion = new CoinShopItem(1, "Potion", "", "PL103", 10) { Bulk = 5, MaxBulk = 20, Quantity = true };
        Assert.True(CoinShopPricing.Total(potion, 4) == 40 && CoinShopPricing.Total(potion, 5) == 48 && CoinShopPricing.Total(potion, 10) == 90,
            "the bulk discount is not five percent per five bought");
        Assert.True(CoinShopPricing.BulkPercent(potion, 100) == 20, "the bulk discount passed its cap");
        Assert.True(CoinShopPricing.Quantity(potion, 500, 50) == 50 && CoinShopPricing.Quantity(potion, 0, 50) == 1,
            "a quantity outside the stack or below one was accepted");

        var orb = new CoinShopItem(2, "Orb", "", "FO106", 30) { Discount = 25 };
        Assert.True(CoinShopPricing.Total(orb, CoinShopPricing.Quantity(orb, 7, 1)) == 25, "a single article was not one at its discount");
        return Task.CompletedTask;
    }

    private static Task CatalogAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-coinshop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "coinshop.json");
            var store = new CoinShopCatalogStore(path);
            Assert.True(store.Reload().StartsWith("no coin shop file", StringComparison.Ordinal) && store.Current.Tabs.Count == 0,
                "a missing file did not leave an empty shop");

            File.WriteAllText(path, """
                {
                  // comments are allowed
                  "Name": "Item Shop", "Message": "Hello",
                  "Tabs": [ { "Id": 3, "Name": "Rings", "Icon": "rings",
                    "Items": [ { "Id": 7, "Name": "Ring", "Description": "A ring.", "ItemKey": "OR106", "Price": 40, "Spec": true } ] } ]
                }
                """);
            store.Reload();
            var ring = store.Current.Find(3, 7);
            Assert.True(ring is { ItemKey: "OR106", Price: 40, Spec: true, Quantity: false } && store.Current.Find(3, 8) is null,
                "the catalog file was not read");

            File.WriteAllText(path, "{ broken");
            Assert.True(store.Reload().StartsWith("coin shop file unreadable", StringComparison.Ordinal) && store.Current.Tabs.Count == 0,
                "a broken file did not leave an empty shop");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        var buffer = new byte[FrameCodec.MaxFrameSize];
        var operation = Guid.NewGuid();
        var articles = Enumerable.Range(1, CoinShopWire.MaxItemsPerTab)
            .Select(index => new CoinShopArticle(index, new string('N', 40), new string('D', 200), "PL103", 10, 8, 5, 25, CoinShopWire.FlagQuantity))
            .ToArray();
        var tab = new CoinShopTabPacket(operation, 2, "Potions", "misc", articles);
        var length = tab.Write(buffer);
        var readTab = CoinShopTabPacket.Read(buffer.AsSpan(0, length));
        Assert.True(length <= CoinShopTabPacket.MaxSize && length <= FrameCodec.MaxFrameSize && readTab.Articles.Count == articles.Length
            && readTab.Articles[0] is { Discount: 8, Bulk: 5, MaxBulk: 25, Flags: CoinShopWire.FlagQuantity } && readTab.Icon == "misc",
            "a full coin shop tab did not fit or survive the wire");

        var catalog = new CoinShopCatalogPacket(operation, 1234, 1, "Item Shop", "Hello");
        Assert.True(CoinShopCatalogPacket.Read(buffer.AsSpan(0, catalog.Write(buffer))) == catalog, "the catalog end did not survive the wire");
        var buy = new CoinShopBuyPacket(operation, 2, 3, 10, (int)PlayerClass.Knight);
        Assert.True(CoinShopBuyPacket.Read(buffer.AsSpan(0, buy.Write(buffer))) == buy, "the purchase did not survive the wire");
        var result = new CoinShopBuyResultPacket(operation, (byte)CoinShopStatus.NoRoom, 77);
        Assert.True(CoinShopBuyResultPacket.Read(buffer.AsSpan(0, result.Write(buffer))) == result, "the result did not survive the wire");
        return Task.CompletedTask;
    }

    private static async Task StorageAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-coins-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            await using (var connection = await provider.GetRequiredService<IDbConnectionFactory>().OpenAsync())
            {
                await connection.ExecuteAsync("INSERT INTO accounts (user_id, password_hash) VALUES ('buyer', 'x')");
            }

            var characters = provider.GetRequiredService<ICharacterRepository>();
            var created = await characters.CreateAsync(CharacterRecordFactory.NewStarter(new CharacterClassRegistry(), 1, "Buyer", 0, PlayerClass.Fighter, 3));
            var coins = provider.GetRequiredService<ICoinRepository>();
            Assert.True(await coins.BalanceAsync(1) == 0 && await coins.AddAsync(1, 100, "grant") == 100, "a grant did not add coins");
            Assert.True(await coins.AddAsync(1, -500, "take") is null && await coins.BalanceAsync(1) == 100, "coins went below zero");

            Assert.True(await coins.BuyWithCharacterAsync(created with { Money = 9 }, 150, "too dear") is null
                && (await characters.FindByNameForAccountAsync(1, "Buyer"))!.Money != 9, "a purchase above the balance wrote the character");
            Assert.True(await coins.BuyWithCharacterAsync(created with { Money = 9 }, 60, "bought") == 40
                && (await characters.FindByNameForAccountAsync(1, "Buyer"))!.Money == 9, "a purchase did not write coins and character together");

            await using var check = await provider.GetRequiredService<IDbConnectionFactory>().OpenAsync();
            var logged = (await check.QueryAsync<long>("SELECT change FROM coin_log WHERE account_id = 1 ORDER BY id")).ToArray();
            Assert.True(logged.SequenceEqual([100L, -60L]), "the coin log does not hold the grant and the purchase alone");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
