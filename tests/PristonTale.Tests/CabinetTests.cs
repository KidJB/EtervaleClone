using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Services;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Cabinet;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Coins;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Trades;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;
using System.Numerics;

namespace PristonTale.Tests;

// The Cabinet: account-wide looks, the class and grip rules, the two brushes and tokens (nothing used up for a look owned
// already), coin purchases refused for exclusive looks, drops, the stats the looks never touch, the saved records, the
// bidding deals on real storage (minimum, step, outbid refund, expiry, a race) and the wire.
internal static class CabinetTests
{
    private const string Armor = "DA120";
    private const string Robe = "DA256";
    private const string Costume = "CA166";
    private const string Token = "CT101";
    private const string BoundToken = "CU101";
    private const string Exclusive = "DS306";

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("cabinet: categories, brush kinds, default rarity and sources, exclusive looks, bound tokens and the settings file", RulesAsync);
        yield return ("cabinet: a brush turns bag gear of its kind into the account's look and is used up once; a wrong brush, no brush or an owned look takes nothing", BrushAsync);
        yield return ("cabinet: a token unlocks its look once and a second token of an owned look is kept", TokenAsync);
        yield return ("cabinet: looks are shared by the account's characters, a class shows only what it wears, and the worn items' stats never change", SharingAsync);
        yield return ("cabinet: coins buy a look for sale once, an exclusive look is never sold, and the applied looks survive the save", PurchaseAsync);
        yield return ("cabinet: a monster's and every boss's Cabinet drops fall", DropsAsync);
        yield return ("cabinet: unlocks are stored once per account with their source and character, and a reset takes them", StorageAsync);
        yield return ("cabinet: deals hold bids, refund the beaten bid, refuse low, repeated and late bids, and award the look when they close", DealsAsync);
        yield return ("cabinet: two bids at once, only one leads and only its coins are held", DealRaceAsync);
        yield return ("cabinet: the deal checks of the account (owned look, level) and the Cabinet messages on the wire", DealServiceAndWireAsync);
    }

    private static CabinetConfig Config() => new(
        [
            new CosmeticRule(Costume, ItemRarity.Legendary, CosmeticSource.BossDrop | CosmeticSource.Deal, Token: Token),
            new CosmeticRule("CA547", Sources: CosmeticSource.MonsterDrop, Token: BoundToken),
            new CosmeticRule(Exclusive, ItemRarity.Legendary, CosmeticSource.Deal)
        ],
        [new CosmeticDrop(Token, Bosses: true, PerTenThousand: 10_000), new CosmeticDrop(CabinetRules.ArmorBrushKey, Monster: 7, PerTenThousand: 10_000)]);

    private static ItemDefinition Gear(uint code, string key, string name, ItemKind kind, EquipmentSlot slot, int level = 40) =>
        new(code, name, kind, slot, GridWidth: 2, GridHeight: 3) { CatalogKey = key, RequiredLevel = level, Template = new ItemTemplate { Defence = new ItemRange(10, 10) } };

    private static readonly ItemDefinition ArmorItem = Gear(ItemCodes.Parse(Armor), Armor, "Battle Suit", ItemKind.Armor, EquipmentSlot.Armor);
    private static readonly ItemDefinition RobeItem = Gear(ItemCodes.Parse(Robe), Robe, "Mystic Robe", ItemKind.Armor, EquipmentSlot.Armor);

    private static ItemCatalog Items(CabinetConfig config) =>
        new([ChatFixture.Axe, ChatFixture.Potion, ArmorItem, RobeItem, .. CabinetRules.ItemDefinitions(config, static key => key)]);

    // A fixture whose world holds the test items and Cabinet settings.
    private static (ChatFixture Fixture, SkinAppService Skins, MemoryCoins Coins, string Path) Open()
    {
        var fixture = new ChatFixture();
        var config = Config();
        fixture.Data.Current = WithItems(fixture.Data.Current, Items(config));
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"priston-cabinet-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, CabinetConfigStore.Write(config));
        var store = new CabinetConfigStore(path);
        Assert.True(store.Reload().StartsWith("Cabinet: 3 look rules", StringComparison.Ordinal), "the test's settings did not load");
        fixture.World.CabinetStore = store;
        var coins = new MemoryCoins(fixture.Characters);
        return (fixture, new SkinAppService(fixture.Sessions, new ImmediateSaves(fixture.Characters), coins, fixture.World, fixture.Data), coins, path);
    }

    private static WorldData WithItems(WorldData data, ItemCatalog items) => new(items, data.Monsters, data.Npcs, data.FieldSpawns, data.Motions);

    private static Task RulesAsync()
    {
        Assert.True(CabinetRules.Category(Armor, SkinSlot.Armor) == CosmeticCategory.Armor && CabinetRules.Category(Robe, SkinSlot.Armor) == CosmeticCategory.Robe
            && CabinetRules.Category(Costume, SkinSlot.Armor) == CosmeticCategory.Costume && CabinetRules.Category("DS101", SkinSlot.OffHand) == CosmeticCategory.Shield
            && CabinetRules.Category("WA101", SkinSlot.Weapon) == CosmeticCategory.Weapon && CabinetRules.Category("BI117", SkinSlot.Hair) == CosmeticCategory.Hair,
            "a look sits in the wrong category");
        Assert.True(CabinetRules.BrushFor(SkinSlot.Armor) == CabinetBrush.ArmorRobe && CabinetRules.BrushFor(SkinSlot.Weapon) == CabinetBrush.WeaponShield
            && CabinetRules.BrushFor(SkinSlot.OffHand) == CabinetBrush.WeaponShield && CabinetRules.BrushFor(SkinSlot.Hair) == CabinetBrush.None, "a brush kind");
        Assert.True(CabinetRules.DefaultRarity(CosmeticCategory.Armor, 10, false) == ItemRarity.Common
            && CabinetRules.DefaultRarity(CosmeticCategory.Weapon, 100, false) == ItemRarity.Epic
            && CabinetRules.DefaultRarity(CosmeticCategory.Costume, 0, false) == ItemRarity.Epic, "default rarity");
        Assert.True(CabinetRules.DefaultSources(true) == (CosmeticSource.Coins | CosmeticSource.Brush) && CabinetRules.DefaultSources(false) == CosmeticSource.Coins
            && CabinetRules.Exclusive(CosmeticSource.BossDrop) && !CabinetRules.Exclusive(CosmeticSource.Coins | CosmeticSource.Event), "sources and exclusivity");
        Assert.True(CabinetRules.ParseSources("Coins, bossdrop") == (CosmeticSource.Coins | CosmeticSource.BossDrop), "sources text");
        Assert.True(TradeRules.IsTradable(ItemKind.Misc, Token) && !TradeRules.IsTradable(ItemKind.Misc, BoundToken)
            && TradeRules.IsTradable(ItemKind.Misc, CabinetRules.ArmorBrushKey), "a bound token trades, or a tradeable one or a brush does not");

        var items = Items(Config());
        var armor = CabinetLooks.Find(Armor, items, Config())!;
        var costume = CabinetLooks.Find(Costume, items, Config())!;
        var exclusive = CabinetLooks.Find(Exclusive, items, Config())!;
        Assert.True(armor is { Brushable: true, Purchasable: true, HasItem: true } && costume is { Purchasable: false, Rarity: ItemRarity.Legendary, Token: Token }
            && exclusive is { Purchasable: false, Category: CosmeticCategory.Shield }, "a look's rule did not apply");
        Assert.True(CabinetLooks.All(items, Config()).Exists(static look => look.Key == Robe && look.Category == CosmeticCategory.Robe), "the robe is not listed");

        var problems = new List<string>();
        var parsed = CabinetConfigStore.Parse(CabinetConfigStore.Write(new CabinetConfig(
            [new CosmeticRule("DA120"), new CosmeticRule("DA120"), new CosmeticRule(""), new CosmeticRule("DA121", Token: "WA101"), new CosmeticRule("DA122", Enabled: false)],
            [new CosmeticDrop("WA101", Bosses: true, PerTenThousand: 5), new CosmeticDrop(Token, PerTenThousand: 5), new CosmeticDrop(Token, Monster: 3, PerTenThousand: 5)])), problems);
        Assert.True(parsed.Cosmetics.Count == 2 && parsed.Rule("DA122") is { Enabled: false } && parsed.Drops.Count == 1 && problems.Count == 5,
            $"the settings file kept a bad entry or lost a good one: {string.Join("; ", problems)}");
        var shipped = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "cabinet.json");
        CabinetConfigStore.Parse(File.ReadAllText(shipped), problems = []);
        Assert.True(problems.Count == 0, $"the shipped Cabinet settings have problems: {string.Join("; ", problems)}");
        return Task.CompletedTask;
    }

    private static async Task BrushAsync()
    {
        var (fixture, skins, coins, path) = Open();
        try
        {
            var player = await fixture.JoinAsync("Brusher", 0, 0, level: 60);
            // A committed unlock gives the character a new bag.
            Inventory Bag() => player.Session.Character!.Inventory;
            int CountOf(ItemInstance item) => Bag().Find(item.InstanceId) is var at and >= 0 ? Bag()[at].Count : 0;
            var armor = new ItemInstance(ArmorItem);
            var axe = new ItemInstance(ChatFixture.Axe);
            var weaponBrush = new ItemInstance(fixture.Data.Current.Items.Get(ItemCodes.Parse(CabinetRules.WeaponBrushKey)), 1);
            Assert.True(Bag().TryPlaceFirstFree(armor) && Bag().TryPlaceFirstFree(axe) && Bag().TryPlaceFirstFree(weaponBrush), "the bag");
            var token = player.Session.SessionToken;

            var wrong = await fixture.RunAsync(skins.UnlockFromItemAsync(token, armor.InstanceId));
            Assert.True(wrong.Status == SkinStatus.NoBrush && Bag().Find(armor.InstanceId) >= 0 && Bag().Find(weaponBrush.InstanceId) >= 0,
                "a weapon brush took armor, or something left the bag");
            var potion = new ItemInstance(ChatFixture.Potion, 3);
            Assert.True(Bag().TryPlaceFirstFree(potion), "the potion");
            Assert.True((await fixture.RunAsync(skins.UnlockFromItemAsync(token, potion.InstanceId))).Status == SkinStatus.NotEligible, "a potion was brushed");

            var weapon = await fixture.RunAsync(skins.UnlockFromItemAsync(token, axe.InstanceId));
            Assert.True(weapon.Status == SkinStatus.Ok && weapon.Key == "WA101" && Bag().Find(axe.InstanceId) < 0 && Bag().Find(weaponBrush.InstanceId) < 0,
                $"the weapon brush did not turn the axe into its look: {weapon.Status}");
            Assert.True(coins.Skins[player.Session.AccountId] is [{ ItemKey: "WA101", Source: (short)CosmeticSource.Brush } record] && record.CharacterId == player.Session.Loaded!.Id,
                "the brushed look was not stored once with its source and character");

            var armorBrushes = new ItemInstance(fixture.Data.Current.Items.Get(ItemCodes.Parse(CabinetRules.ArmorBrushKey)), 3);
            Assert.True(Bag().TryPlaceFirstFree(armorBrushes), "the armor brushes");
            Assert.True((await fixture.RunAsync(skins.UnlockFromItemAsync(token, armor.InstanceId))).Status == SkinStatus.Ok && Bag().Find(armor.InstanceId) < 0
                && CountOf(armorBrushes) == 2, "the armor brush did not take the armor and exactly one brush");

            var second = new ItemInstance(ArmorItem);
            Assert.True(Bag().TryPlaceFirstFree(second), "the second armor");
            var owned = await fixture.RunAsync(skins.UnlockFromItemAsync(token, second.InstanceId));
            Assert.True(owned.Status == SkinStatus.AlreadyOwned && Bag().Find(second.InstanceId) >= 0 && CountOf(armorBrushes) == 2,
                "an owned look used up its item or a brush");
            Assert.True(coins.Skins[player.Session.AccountId].Count == 2, "a look was stored twice");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task TokenAsync()
    {
        var (fixture, skins, coins, path) = Open();
        try
        {
            var player = await fixture.JoinAsync("Collector", 0, 0);
            Inventory Bag() => player.Session.Character!.Inventory;
            var definition = fixture.Data.Current.Items.Get(ItemCodes.Parse(Token));
            var first = new ItemInstance(definition);
            var second = new ItemInstance(definition);
            Assert.True(Bag().TryPlaceFirstFree(first) && Bag().TryPlaceFirstFree(second), "the tokens");
            var used = await fixture.RunAsync(skins.UnlockFromItemAsync(player.Session.SessionToken, first.InstanceId));
            Assert.True(used.Status == SkinStatus.Ok && used.Key == Costume && Bag().Find(first.InstanceId) < 0, "the token did not unlock its costume");
            Assert.True(coins.Skins[player.Session.AccountId] is [{ Source: (short)CosmeticSource.BossDrop }], "the token's look was not recorded as a boss drop");
            var again = await fixture.RunAsync(skins.UnlockFromItemAsync(player.Session.SessionToken, second.InstanceId));
            Assert.True(again.Status == SkinStatus.AlreadyOwned && Bag().Find(second.InstanceId) >= 0, "a second token of an owned look was used up");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task SharingAsync()
    {
        var (fixture, skins, coins, path) = Open();
        try
        {
            var fighter = await fixture.JoinAsync("Fighter", 0, 0, level: 60);
            var priestess = await fixture.JoinAsync("Priestess", 40, 0, playerClass: PlayerClass.Priestess, level: 60, account: fighter.Session.AccountId);
            coins.Balances[fighter.Session.AccountId] = 100_000;
            Assert.True((await skins.BuyAsync(fighter.Session.SessionToken, Armor)).Status == SkinStatus.Ok
                && (await skins.BuyAsync(fighter.Session.SessionToken, Robe)).Status == SkinStatus.Ok, "the fighter's purchases");
            var shared = await fixture.RunAsync(skins.CatalogAsync(priestess.Session.SessionToken));
            Assert.True(shared.Articles.Any(static article => article.Key == Armor && article.Owned) && shared.Articles.Any(static article => article.Key == Robe && article.Owned),
                "the account's looks did not reach its other character");
            Assert.True((await fixture.RunAsync(skins.ApplyAsync(priestess.Session.SessionToken, new SkinSet(Armor: Robe)))).Status == SkinStatus.Ok,
                "the priestess could not show the robe her account owns");
            Assert.True((await fixture.RunAsync(skins.ApplyAsync(fighter.Session.SessionToken, new SkinSet(Armor: Robe)))).Status == SkinStatus.Rejected,
                "the fighter showed a robe its class cannot wear");
            Assert.True((await fixture.RunAsync(skins.ApplyAsync(fighter.Session.SessionToken, new SkinSet(Armor: Exclusive)))).Status == SkinStatus.NotOwned,
                "a look the account does not own was shown");
            Assert.True((await fixture.RunAsync(skins.ApplyAsync(fighter.Session.SessionToken, new SkinSet(Weapon: Armor)))).Status == SkinStatus.NotOwned,
                "an armor look went to the weapon slot");

            var character = fighter.Session.Character!;
            var worn = new ItemInstance(ArmorItem);
            character.Equip(EquipmentSlot.Armor, worn);
            character.Equip(EquipmentSlot.Weapon, new ItemInstance(ChatFixture.Axe));
            var before = character.Derived;
            var itemBefore = (worn.Roll, worn.Rarity, worn.UpgradeLevel, worn.DurabilityCurrent);
            Assert.True((await fixture.RunAsync(skins.ApplyAsync(fighter.Session.SessionToken, new SkinSet(Armor: Armor)))).Status == SkinStatus.Ok, "the fighter's look");
            Assert.True(character.Derived == before && (worn.Roll, worn.Rarity, worn.UpgradeLevel, worn.DurabilityCurrent) == itemBefore,
                "showing a look changed a stat");
            Assert.True((await fixture.RunAsync(skins.ApplyAsync(fighter.Session.SessionToken, SkinSet.None))).Status == SkinStatus.Ok && character.Derived == before,
                "removing a look changed a stat");
            Assert.True(SkinRules.Look(Armor, Robe) == Armor && SkinRules.Look("WA101", "DS101") == "WA101", "a look of another kind showed over gear");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task PurchaseAsync()
    {
        var (fixture, skins, coins, path) = Open();
        try
        {
            var player = await fixture.JoinAsync("Buyer", 0, 0);
            var token = player.Session.SessionToken;
            Assert.True((await skins.BuyAsync(token, Armor)).Status == SkinStatus.NotEnoughCoins, "a look was bought without coins");
            coins.Balances[player.Session.AccountId] = 10_000;
            var price = CabinetLooks.Find(Armor, fixture.Data.Current.Items, fixture.World.Cabinet)!.Price;
            Assert.True((await skins.BuyAsync(token, Armor)) == (SkinStatus.Ok, 10_000 - price), "the look for sale was not bought at its price");
            Assert.True((await skins.BuyAsync(token, Armor)).Status == SkinStatus.AlreadyOwned && coins.Balances[player.Session.AccountId] == 10_000 - price,
                "a look was bought twice");
            Assert.True((await skins.BuyAsync(token, Costume)).Status == SkinStatus.NotForSale && (await skins.BuyAsync(token, Exclusive)).Status == SkinStatus.NotForSale,
                "an exclusive look was sold");
            Assert.True((await skins.BuyAsync(token, "ZZ999")).Status == SkinStatus.UnknownSkin, "an unknown look was sold");

            var catalog = await fixture.RunAsync(skins.CatalogAsync(token));
            var exclusive = catalog.Articles.Single(static article => article.Key == Exclusive);
            Assert.True(exclusive.Sources == (byte)CosmeticSource.Deal && exclusive.Rarity == (byte)ItemRarity.Legendary && exclusive.Category == (byte)CosmeticCategory.Shield,
                "the catalog lost a look's rule");

            Assert.True((await fixture.RunAsync(skins.ApplyAsync(token, new SkinSet(Armor: Armor)))).Status == SkinStatus.Ok, "the look");
            var record = CharacterRecordFactory.Capture(player.Session.Loaded!, player.Session.Character!, ChatFixture.Field, Vector3.Zero);
            Assert.True(SkinCodec.Restore(record.Encode().Skins) == new SkinSet(Armor: Armor), "the shown look was not saved with the character");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Task DropsAsync()
    {
        var config = Config();
        var items = Items(config);
        var hopy = new MonsterDefinition(7, "Hopy", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
            new MonsterCombatStats(4, 1, new MonsterRange(1, 1), 6, 30, 0, 0, 0, 1, ResistanceBlock.Zero), "") { ExpReward = 10 };
        var boss = hopy with { Code = 8, Name = "Boss", Class = MonsterClass.Boss };
        foreach (var definition in (MonsterDefinition[])[hopy, boss])
        {
            var field = new FieldInstance(1, FieldSpawnData.Empty, new MonsterCatalog([definition]), new EntityIdAllocator(), new CombatRng(5), items: items)
            {
                Cabinet = () => config
            };
            var killer = new WorldPlayer(1, Vector3.Zero, new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Hunter", 80), false, default);
            field.Add(killer);
            var monster = ((ISpawnSink)field).Spawn(definition, new Vector3(24, 0, 0), -1, 0);
            Assert.True(field.SetAttackTarget(1, monster.EntityId, true), "fixture target rejected");
            for (var index = 0; index < 400 && monster.IsAlive; index++)
            {
                field.Step(0.05f);
            }

            var keys = field.GroundLoot.Snapshot().Select(static drop => drop.View.CatalogKey).ToList();
            Assert.True(!monster.IsAlive, $"the {definition.Name} did not die");
            Assert.True(definition.IsBoss ? keys.Contains(Token) && !keys.Contains(CabinetRules.ArmorBrushKey) : keys.Contains(CabinetRules.ArmorBrushKey) && !keys.Contains(Token),
                $"the {definition.Name}'s Cabinet drops: {string.Join(", ", keys)}");
        }

        return Task.CompletedTask;
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"priston-cabinet-{Guid.NewGuid():N}");
        private ServiceProvider _provider = null!;

        public ICoinRepository Coins => _provider.GetRequiredService<ICoinRepository>();
        public ICabinetDealRepository Deals => _provider.GetRequiredService<ICabinetDealRepository>();
        public ICharacterRepository Characters => _provider.GetRequiredService<ICharacterRepository>();

        public static async Task<Store> OpenAsync()
        {
            var store = new Store();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddPristonSqlite(System.IO.Path.Combine(store._directory, "world.db"));
            store._provider = services.BuildServiceProvider();
            await store._provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            await using var connection = await store._provider.GetRequiredService<IDbConnectionFactory>().OpenAsync();
            await connection.ExecuteAsync("INSERT INTO accounts (user_id, password_hash, coins) VALUES ('ann', 'x', 1000), ('bob', 'x', 5000), ('cara', 'x', 5000)");
            return store;
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static async Task StorageAsync()
    {
        await using var store = await Store.OpenAsync();
        var created = await store.Characters.CreateAsync(CharacterRecordFactory.NewStarter(new CharacterClassRegistry(), 1, "Ann", 0, PlayerClass.Fighter, 3));
        Assert.True(await store.Coins.UnlockSkinAsync(new OwnedSkinRecord(1, Costume, 50, (short)CosmeticSource.Event, created.Id)), "an event's look");
        Assert.True(!await store.Coins.UnlockSkinAsync(new OwnedSkinRecord(1, Costume, 60, (short)CosmeticSource.Quest, 0)), "a look was unlocked twice");
        Assert.True(await store.Coins.BuySkinAsync(1, Armor, 100, "test", 70) == 900 && await store.Coins.BuySkinAsync(1, Armor, 100, "test", 80) is null
            && await store.Coins.BalanceAsync(1) == 900, "a bought look was paid twice");
        var records = await store.Coins.SkinRecordsAsync(1);
        Assert.True(records.Count == 2 && records[0] == new OwnedSkinRecord(1, Costume, 50, (short)CosmeticSource.Event, created.Id)
            && records[1] == new OwnedSkinRecord(1, Armor, 70, (short)CosmeticSource.Coins, 0), "the stored unlocks");
        Assert.True((await store.Coins.OwnedSkinsAsync(2)).Count == 0, "another account received the looks");

        var moved = created with { Money = 1234 };
        Assert.True(await store.Coins.UnlockSkinWithCharacterAsync(moved, new OwnedSkinRecord(1, Costume, 90, (short)CosmeticSource.Brush, created.Id)) == SkinUnlockStore.Owned
            && (await store.Characters.FindByNameForAccountAsync(1, "Ann"))!.Money != 1234, "an owned look's unlock wrote the character");
        Assert.True(await store.Coins.UnlockSkinWithCharacterAsync(moved, new OwnedSkinRecord(1, Robe, 90, (short)CosmeticSource.Brush, created.Id)) == SkinUnlockStore.Stored
            && (await store.Characters.FindByNameForAccountAsync(1, "Ann"))!.Money == 1234, "the brushed look and its character were not stored together");
        Assert.True(await store.Coins.ResetSkinsAsync(1) == 3 && (await store.Coins.OwnedSkinsAsync(1)).Count == 0, "the reset");
    }

    private static async Task DealsAsync()
    {
        await using var store = await Store.OpenAsync();
        var deals = store.Deals;
        var id = await deals.CreateAsync(new CabinetDealDraft(Exclusive, 100, 1000, 300, 50, 0, "GM"));
        Assert.True((await deals.BidAsync(id, 2, "bob", 299, 200)).Status == CabinetBidStore.TooLow, "a bid under the start was taken");
        Assert.True((await deals.BidAsync(id, 2, "bob", 300, 50)).Status == CabinetBidStore.Ended, "a bid before the start was taken");
        Assert.True(await deals.BidAsync(id, 2, "bob", 300, 200) == (CabinetBidStore.Placed, 4700), "the first bid at the start");
        Assert.True((await deals.BidAsync(id, 2, "bob", 400, 210)).Status == CabinetBidStore.Leading, "the leader bid against itself");
        Assert.True((await deals.BidAsync(id, 3, "cara", 349, 220)).Status == CabinetBidStore.TooLow, "a bid under the step was taken");
        Assert.True((await deals.BidAsync(id, 1, "ann", 2000, 220)).Status == CabinetBidStore.NotEnoughCoins && await store.Coins.BalanceAsync(1) == 1000,
            "a poor bid was taken");
        Assert.True(await deals.BidAsync(id, 3, "cara", 350, 230) == (CabinetBidStore.Placed, 4650) && await store.Coins.BalanceAsync(2) == 5000,
            "the beaten bid was not given back");
        var deal = (await deals.GetAsync(id))!;
        Assert.True(deal is { TopAccountId: 3, TopBid: 350, TopName: "cara", Bids: 2, NextBid: 400 }, "the deal's leader");
        var history = await deals.BidsAsync(id, 5);
        Assert.True(history.Count == 2 && history[0].Amount == 350 && history[1].Name == "bob", "the bid history");
        Assert.True((await deals.BidAsync(id, 2, "bob", 400, 1000)).Status == CabinetBidStore.Ended, "a bid after the end was taken");

        Assert.True((await deals.CloseDueAsync(999)).Count == 0, "a deal closed before its end");
        var closed = await deals.CloseDueAsync(1000);
        Assert.True(closed is [{ Awarded: true, Refunded: false }] && (await store.Coins.OwnedSkinsAsync(3)).Contains(Exclusive)
            && await store.Coins.BalanceAsync(3) == 4650, "the winner did not receive the look for its held coins");
        Assert.True((await deals.CloseDueAsync(2000)).Count == 0 && await deals.EndAsync(id, 2000) is null, "a deal closed twice");
        Assert.True((await store.Coins.SkinRecordsAsync(3)) is [{ Source: (short)CosmeticSource.Deal }], "the won look's source");

        var owned = await deals.CreateAsync(new CabinetDealDraft(Exclusive, 100, 5000, 100, 10, 0, "GM"));
        Assert.True((await deals.BidAsync(owned, 3, "cara", 100, 200)).Status == CabinetBidStore.Placed, "the second deal's bid");
        var ended = await deals.EndAsync(owned, 300);
        Assert.True(ended is { Awarded: false, Refunded: true } && await store.Coins.BalanceAsync(3) == 4650, "a winner owning the look was not refunded");
        var empty = await deals.CreateAsync(new CabinetDealDraft(Armor, 100, 5000, 100, 10, 0, "GM"));
        Assert.True(await deals.EndAsync(empty, 300) is { Awarded: false, Refunded: false } && (await deals.OpenAsync()).Count == 0, "a deal without bids");
    }

    private static async Task DealRaceAsync()
    {
        await using var store = await Store.OpenAsync();
        var id = await store.Deals.CreateAsync(new CabinetDealDraft(Exclusive, 0, 1000, 500, 100, 0, "GM"));
        var results = await Task.WhenAll(Task.Run(() => store.Deals.BidAsync(id, 2, "bob", 500, 10)), Task.Run(() => store.Deals.BidAsync(id, 3, "cara", 500, 10)));
        Assert.True(results.Count(static result => result.Status == CabinetBidStore.Placed) == 1, $"not exactly one bid led: {results[0].Status}, {results[1].Status}");
        Assert.True(await store.Coins.BalanceAsync(2) + await store.Coins.BalanceAsync(3) == 9500, "both bids held coins, or neither");
        Assert.True((await store.Deals.GetAsync(id))!.Bids == 1, "two bids were counted");
    }

    private static async Task DealServiceAndWireAsync()
    {
        await using var store = await Store.OpenAsync();
        var fixture = new ChatFixture();
        var holder = new ChatFixture().Data;
        holder.Current = WithItems(holder.Current, Items(Config()));
        var service = new CabinetDealAppService(fixture.Sessions, store.Deals, store.Coins, fixture.World, holder);
        var low = await fixture.JoinAsync("Low", 0, 0, level: 10);
        var owner = await fixture.JoinAsync("Owner", 10, 0, level: 80);
        var (id, problem) = await service.CreateAsync("DA120", 100, 10, 2, 50, "GM");
        Assert.True(problem is null && (await service.CreateAsync("ZZ999", 100, 10, 2, 0, "GM")).Problem is not null
            && (await service.CreateAsync("DA120", 0, 10, 2, 0, "GM")).Problem is not null, "a deal's checks");
        Assert.True((await service.BidAsync(low.Session.SessionToken, id, 100)).Status == CabinetBidStatus.LevelTooLow, "a bid under the deal's level");
        Assert.True(await store.Coins.UnlockSkinAsync(new OwnedSkinRecord(owner.Session.AccountId, "DA120", 0, (short)CosmeticSource.Event, 0)), "the owner's look");
        Assert.True((await service.BidAsync(owner.Session.SessionToken, id, 100)).Status == CabinetBidStatus.AlreadyOwned, "a bid on an owned look");
        var listed = await service.ListAsync(owner.Session.SessionToken);
        Assert.True(listed.Deals is [{ Look.Key: "DA120", Leading: false }], "the open deal is not listed");

        Assert.True(SkinUnlockResultPacket.MaxSize <= FrameCodec.MaxFrameSize && CabinetDealsPacket.MaxSize <= FrameCodec.MaxFrameSize
            && SkinCatalogPacket.MaxSize <= FrameCodec.MaxFrameSize, "a Cabinet message does not fit one frame");
        var buffer = new byte[FrameCodec.MaxFrameSize];
        var article = new SkinArticle(Costume, "Cygnus", 1, 0, 4500, false) { Category = 2, Rarity = 5, Sources = 68, Description = "Swan" };
        var catalog = SkinCatalogPacket.Read(buffer.AsSpan(0, new SkinCatalogPacket(Guid.NewGuid(), 5, true, "", "", "", "", [article]).Write(buffer)));
        Assert.True(catalog.Articles[0] == article, "an article's rule changed on the wire");
        var unlock = new SkinUnlockPacket(Guid.NewGuid(), Guid.NewGuid());
        Assert.True(SkinUnlockPacket.Read(buffer.AsSpan(0, unlock.Write(buffer))) == unlock, "the unlock request");
        var unlocked = new SkinUnlockResultPacket(Guid.NewGuid(), 0, 77, Costume);
        Assert.True(SkinUnlockResultPacket.Read(buffer.AsSpan(0, unlocked.Write(buffer))) == unlocked, "the unlock result");
        var wireDeal = new CabinetWireDeal(9, Exclusive, "Torment Shield", 5, 1000, 400, 350, "cara", true, 50, 2, [new CabinetWireBid("cara", 350, 230), new CabinetWireBid("bob", 300, 200)]);
        var dealsPacket = new CabinetDealsPacket(Guid.NewGuid(), 4650, 500, [wireDeal]);
        var readDeals = CabinetDealsPacket.Read(buffer.AsSpan(0, dealsPacket.Write(buffer)));
        Assert.True(readDeals.Balance == 4650 && readDeals.Now == 500 && readDeals.Deals[0] with { History = [] } == wireDeal with { History = [] }
            && readDeals.Deals[0].History.SequenceEqual(wireDeal.History), "the deals message");
        var bid = new CabinetBidPacket(Guid.NewGuid(), 9, 400);
        Assert.True(CabinetBidPacket.Read(buffer.AsSpan(0, bid.Write(buffer))) == bid, "the bid");
        var result = new CabinetBidResultPacket(Guid.NewGuid(), 3, 12);
        Assert.True(CabinetBidResultPacket.Read(buffer.AsSpan(0, result.Write(buffer))) == result, "the bid result");
    }
}
