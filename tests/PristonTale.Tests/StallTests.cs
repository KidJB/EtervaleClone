using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.ItemBoxes;
using PristonTale.Persistence.Abstractions.Stalls;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

// Offline stalls: the listed items leave the bag for the stall's stock, a sale moves units to the buyer and the price to the
// owner within its gold limit, and storage writes the sale and the close (into the Item Distributor) in one transaction.
internal static class StallTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("stall: the listing leaves the bag for the stock, and a sale moves units and gold at the price shown", RulesAsync);
        yield return ("stall: storage keeps one stall per account, pays an offline owner within its limit, and a close fills the Item Distributor", StorageAsync);
    }

    private static Task RulesAsync()
    {
        var registry = new CharacterClassRegistry();
        var seller = registry.CreatePlayer(PlayerClass.Fighter, "Seller");
        var potions = new ItemInstance(ChatFixture.Potion, 10);
        Assert.True(seller.Inventory.TryAdd(potions), "fixture potions");
        var shop = StallRules.Listing("Potions", [new PersonalShopEntry(potions.InstanceId, 5, 0)]);
        var stock = StallRules.TakeStock(seller, shop);
        Assert.True(stock is not null && seller.Inventory.Find(potions.InstanceId) < 0 && stock.Find(potions.InstanceId) >= 0, "the stock did not take the listed item");

        var buyer = registry.CreatePlayer(PlayerClass.Fighter, "Buyer");
        Assert.True(buyer.TryChangeMoney(100), "fixture gold");
        var purchase = new PersonalShopPurchase(Guid.NewGuid(), 1, potions.InstanceId, 5, 4, 0);
        Assert.True(StallRules.TrySell(shop, stock!, buyer, purchase with { UnitPrice = 6 }, 1, 0, 0, out _) == PersonalShopStatus.PriceChanged,
            "a changed price sold");
        Assert.True(StallRules.TrySell(shop, stock!, buyer, purchase, 1, 0, MoneyLimits.CarryLimit(1, 0), out _) == PersonalShopStatus.GoldLimit,
            "an owner at its gold limit was paid");
        Assert.True(StallRules.TrySell(shop, stock!, buyer, purchase, 1, 0, 0, out var price) == PersonalShopStatus.Ok && price == 20 && buyer.Money == 80
            && stock![stock.Find(potions.InstanceId)].Count == 6, "the sale did not move four units for 20 gold");
        Assert.True(StallRules.Remaining(shop, stock!).Entries.Count == 1, "an entry with units left was dropped");
        return Task.CompletedTask;
    }

    private static async Task StorageAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-stalls-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            await using (var connection = await provider.GetRequiredService<IDbConnectionFactory>().OpenAsync())
            {
                await connection.ExecuteAsync("INSERT INTO accounts (user_id, password_hash) VALUES ('ann', 'x'), ('bob', 'x')");
            }

            var characters = provider.GetRequiredService<ICharacterRepository>();
            var registry = new CharacterClassRegistry();
            var ann = await characters.CreateAsync(CharacterRecordFactory.NewStarter(registry, 1, "Ann", 0, PlayerClass.Fighter, 3));
            var bob = await characters.CreateAsync(CharacterRecordFactory.NewStarter(registry, 2, "Bob", 0, PlayerClass.Fighter, 3));
            var stalls = provider.GetRequiredService<IStallRepository>();
            var shop = StallRules.Listing("Sign", []);
            var stock = StoreGrid.Create();
            var potion = new ItemInstance(ChatFixture.Potion, 3);
            stock.TryPlace(potion, 0);
            shop = StallRules.Listing("Sign", [new PersonalShopEntry(potion.InstanceId, 10, 0)]);
            var look = StallCodec.CaptureLook(new StallLook("Ann", 1, 0, 0, 0, "", "", "", "", "", "Sign", 1));
            var record = new StallRecord(0, 1, ann.Id, 3, 1, 2, 3, 0, look, StallCodec.CaptureItems(shop, stock));
            var id = await stalls.CreateWithCharacterAsync(ann, record);
            Assert.True(id is not null && await stalls.CreateWithCharacterAsync(ann, record) is null && (await stalls.ListAsync()).Count == 1,
                "an account kept two stalls or none");

            var sale = new StallSale(bob with { Money = 1 }, id!.Value, StallCodec.CaptureItems(shop, stock), false, ann.Id, 30, 1_000, null);
            Assert.True(await stalls.SellAsync(sale) == StallSaleStore.Stored && (await characters.FindByNameForAccountAsync(1, "Ann"))!.Money == ann.Money + 30
                && (await characters.FindByNameForAccountAsync(2, "Bob"))!.Money == 1, "the sale did not pay the offline owner with the buyer stored");
            Assert.True(await stalls.SellAsync(sale with { OwnerMoneyLimit = 0 }) == StallSaleStore.GoldLimit, "an owner past its limit was paid");

            Assert.True(await stalls.CloseAsync(1, "Your stall") == id && await stalls.CloseAsync(1, "Your stall") is null,
                "the stall did not close once");
            var box = provider.GetRequiredService<IItemBoxRepository>();
            Assert.True((await box.ListAsync(1)) is [{ Sender: "Your stall" }] && await stalls.SellAsync(sale) == StallSaleStore.Gone,
                "the unsold item did not reach the Item Distributor, or a closed stall sold");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
