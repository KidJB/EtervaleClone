using System.Threading.Channels;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Clans;
using PristonTale.Persistence.Abstractions.Warehouses;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Social.Clans;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Bellatra;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

internal static class ClanStorageTests
{
    private const long Wolves = 7;
    private const long Bears = 8;
    private const string Sword = "WA101";
    private const string Shield = "DS101";

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("clan rights: the chief has all, the vice and members what the chief sets (defaults), packed as stored", PermissionsAsync);
        yield return ("clan buffs: a buff running for the world and the clan counts once; the clan's mask ends on time", BuffRulesAsync);
        yield return ("clan buffs: a member with the right starts one for its clan only; other clans, the world and the item rules stay apart",
            BuffWorldAsync);
        yield return ("clan buffs: leaving drops them at once, joining or entering gives the time left, and they come back after a restart",
            BuffMembershipAsync);
        yield return ("clan warehouse: two pages of the store grid hold 140 items, deposits spill to the next page and items move between pages",
            WarehouseRulesAsync);
        yield return ("clan warehouse: shared by the clan's members by their rights, never by another clan, synced to every open view and logged",
            WarehouseWorldAsync);
        yield return ("clan warehouse: two members taking the same item at once get it once, and a reopened warehouse keeps the items' metadata",
            WarehouseConcurrencyAsync);
        yield return ("clan storage on SQLite: the warehouse under its revision with the character and the log, the buffs, and dissolution", SqliteAsync);
        yield return ("clan warehouse window: Next turns the page, an item carried across is set down on the other page, and the tabs follow the rank",
            WindowAsync);
        yield return ("clan warehouse keeper: its own NPC drawn as a warehouse keeper, offering only the clan warehouse, made once; the clan buffs are items of their own", KeepersAsync);
        yield return ("clan upgrades: five doubling levels each, slots to 240 on three pages and weight to 3,200; a resized warehouse keeps its items",
            UpgradeRulesAsync);
        yield return ("clan coins: donations in range, the vice and chief spend the coin treasury on a buff at the world buff items' prices",
            CoinRulesAsync);
        yield return ("clan storage messages round-trip: the clan's rights, the log, the clan's buffs and a warehouse in parts", WireAsync);
    }

    private static Task PermissionsAsync()
    {
        var defaults = ClanRankPermissions.Default;
        Assert.True(defaults.For(ClanRank.Chief) == ClanPermission.All && defaults.For(ClanRank.Vice) == ClanPermission.All
            && defaults.For(ClanRank.Member) == (ClanPermission.Deposit | ClanPermission.Withdraw), "the default rights differ");
        Assert.True(defaults.Packed == 0x1F03 && ClanRankPermissions.Unpack(defaults.Packed) == defaults
            && defaults.Vice.HasFlag(ClanPermission.SpendCoins) && !defaults.Member.HasFlag(ClanPermission.SpendCoins), "the default rights do not pack as the migration's default");
        var custom = new ClanRankPermissions(ClanPermission.Deposit, ClanPermission.Deposit | ClanPermission.ViewLog);
        Assert.True(ClanRankPermissions.Unpack(custom.Packed) == custom && custom.For(ClanRank.Chief) == ClanPermission.All
            && ClanRankPermissions.Unpack(0xFFFF) == new ClanRankPermissions(ClanPermission.All, ClanPermission.All), "rights do not round-trip or are not bounded");
        Assert.True(ClanWarehouse.Allows(ClanPermission.Deposit, NpcServiceAction.Deposit) && !ClanWarehouse.Allows(ClanPermission.Deposit, NpcServiceAction.Withdraw)
            && ClanWarehouse.Allows(ClanPermission.Withdraw, NpcServiceAction.Move) && !ClanWarehouse.Allows(ClanPermission.ViewLog, NpcServiceAction.Move)
            && ClanWarehouse.Allows(ClanPermission.None, NpcServiceAction.Open) && !ClanWarehouse.Allows(ClanPermission.All, NpcServiceAction.DepositGold),
            "the warehouse's rights per action differ");
        var clan = new Clan(1, "Wolves", ClanState.Registered, 0, 0, permissions: custom);
        clan.Add(new ClanMember(1, 1, "Chief", PlayerClass.Fighter, 50, ClanRank.Chief, 0));
        clan.Add(new ClanMember(2, 2, "Vice", PlayerClass.Fighter, 50, ClanRank.Vice, 0));
        Assert.True(clan.RightsOf(1) == ClanPermission.All && clan.RightsOf(2) == custom.Vice && clan.RightsOf(3) == ClanPermission.None, "a member's rights");
        return Task.CompletedTask;
    }

    private static Task BuffRulesAsync()
    {
        var experience = WorldBuffs.Get((int)WorldBuffKind.Experience).Bit;
        var both = new PremiumTimers { WorldBuffMask = experience, ClanBuffMask = experience };
        Assert.True(PremiumRules.ExperiencePercent(both) == WorldBuffs.ExperiencePercent, "a buff running for the world and the clan stacked");
        var clanOnly = new PremiumTimers { ClanBuffMask = WorldBuffs.Get((int)WorldBuffKind.SpDown).Bit };
        Assert.True(PremiumRules.StaminaCost(100, clanOnly) == 90 && clanOnly.BuffMask == clanOnly.ClanBuffMask, "a clan buff did not work as the world's");
        var ends = new long[WorldBuffs.All.Count + 1];
        ends[(int)WorldBuffKind.Drop] = 100;
        Assert.True(ClanBuffs.Mask(ends, 99) == WorldBuffs.Get((int)WorldBuffKind.Drop).Bit && ClanBuffs.Mask(ends, 100) == 0, "the clan's mask past its end");
        Assert.True(WorldBuffs.Extend(50, 100, 3600) == 3700 && WorldBuffs.Extend(5000, 100, 3600) == 8600, "an item's time is not added to what is left");
        return Task.CompletedTask;
    }

    private static async Task BuffWorldAsync()
    {
        await using var world = await ClanWorld.CreateAsync();
        var chief = await world.JoinAsync("Chief", Wolves, ClanPermission.All, "CL109", "CL209", "WB109");
        var member = await world.JoinAsync("Member", Wolves, ClanPermission.Deposit, "CL109");
        var rival = await world.JoinAsync("Rival", Bears, ClanPermission.All);
        var loner = await world.JoinAsync("Loner", 0, ClanPermission.None, "CL109", "WB109");
        var exp = WorldBuffs.Get((int)WorldBuffKind.Experience)!;

        Assert.True((await world.UseAsync(loner, "CL109", 0)).Outcome.Status == ItemUseStatus.ClanOnly && loner.Item("CL109") is not null,
            "a player without a clan started a clan buff");
        Assert.True((await world.UseAsync(member, "CL109", 0)).Outcome.Status == ItemUseStatus.ClanRankTooLow && member.Item("CL109") is not null,
            "a rank without the right started a clan buff or lost its item");
        Assert.True((await world.UseAsync(chief, "CL109", 0)).Outcome.Ok, "the chief's clan buff was refused");
        world.Pass();
        var first = world.Server.ClanBuffState.Single();
        Assert.True(first is { ClanId: Wolves, Kind: (int)WorldBuffKind.Experience } && first.EndSeconds == world.Now + WorldBuffs.DurationSeconds,
            "the clan buff did not run its hour");
        Assert.True(chief.Premium.ClanBuffMask == exp.Bit && member.Premium.ClanBuffMask == exp.Bit, "a member of the clan has not the buff");
        Assert.True(rival.Premium.ClanBuffMask == 0 && loner.Premium.ClanBuffMask == 0, "another clan or a player without one has the buff");
        Assert.True(world.Server.WorldBuffState.Length == 0 && chief.Premium.WorldBuffMask == 0 && loner.Premium.WorldBuffMask == 0, "the world's buffs changed");

        Assert.True((await world.UseAsync(chief, "CL209", 0)).Outcome.Ok, "the 24-hour item was refused");
        world.Pass();
        Assert.True(world.Server.ClanBuffState.Single().EndSeconds == first.EndSeconds + WorldBuffs.DayDurationSeconds,
            "the 24-hour item did not add its time to what was left");

        Assert.True((await world.UseAsync(loner, "WB109", 0)).Outcome.Ok, "the world buff was refused");
        world.Pass();
        Assert.True(world.Server.WorldBuffState.Length == 1 && rival.Premium.WorldBuffMask == exp.Bit && chief.Premium.BuffMask == exp.Bit
            && PremiumRules.ExperiencePercent(chief.Premium) == WorldBuffs.ExperiencePercent, "the world buff did not reach all or stacked with the clan's");
        Assert.True(world.Server.ClanBuffState.Length == 1, "the world buff changed the clan's");
        Assert.True((await world.UseAsync(chief, "WB109", 0)).Outcome.Ok && world.Server.ClanBuffState.Length == 1, "a world item started a clan buff");
    }

    private static async Task BuffMembershipAsync()
    {
        ClanBuffEnd[] saved;
        await using (var world = await ClanWorld.CreateAsync())
        {
            var chief = await world.JoinAsync("Chief", Wolves, ClanPermission.All, "CL110");
            var member = await world.JoinAsync("Member", Wolves, ClanPermission.Deposit);
            var recruit = await world.JoinAsync("Recruit", 0, ClanPermission.None);
            var drop = WorldBuffs.Get((int)WorldBuffKind.Drop)!.Bit;
            Assert.True((await world.UseAsync(chief, "CL110", 0)).Outcome.Ok, "the clan buff was refused");
            world.Pass();
            Assert.True(member.Premium.ClanBuffMask == drop && recruit.Premium.ClanBuffMask == 0, "the buff did not reach only the clan");

            await world.TagAsync(member, 0, ClanPermission.None);
            world.Pass();
            Assert.True(member.Premium.ClanBuffMask == 0 && PremiumRules.ExtraDropChance(member.Premium) == 0, "a member who left kept the clan buff");
            await world.TagAsync(recruit, Wolves, ClanPermission.Deposit);
            world.Pass();
            Assert.True(recruit.Premium.ClanBuffMask == drop && PremiumRules.ExtraDropChance(recruit.Premium) == WorldBuffs.DropPercent, "a member who joined lacks it");
            saved = world.Server.ClanBuffState;
        }

        await using (var restarted = await ClanWorld.CreateAsync())
        {
            restarted.Clock.Now = restarted.Clock.Now.AddMinutes(30);
            await restarted.RunAsync(restarted.Server.RestoreClanBuffsAsync(saved));
            var back = await restarted.JoinAsync("Chief", Wolves, ClanPermission.All);
            restarted.Pass();
            Assert.True(restarted.Server.ClanBuffState.SequenceEqual(saved) && back.Premium.ClanBuffMask == WorldBuffs.Get((int)WorldBuffKind.Drop)!.Bit,
                "the clan buff did not come back after a restart for the time left");
            var snapshot = restarted.Latest(back);
            Assert.True(snapshot?.ClanBuffs is [{ Kind: (int)WorldBuffKind.Drop }], "a member entering the world was not sent its clan's buffs");

            restarted.Clock.Now = DateTimeOffset.FromUnixTimeSeconds(saved[0].EndSeconds);
            restarted.Pass();
            Assert.True(restarted.Server.ClanBuffState.Length == 0 && back.Premium.ClanBuffMask == 0, "the clan buff outlived its end");
        }
    }

    private static Task WarehouseRulesAsync()
    {
        var storage = ClanWarehouse.Create();
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Keeper", 80);
        var sword = new ItemDefinition(ItemCodes.Parse(Sword), "Sword", ItemKind.Misc, MaxStack: 1) { CatalogKey = Sword };
        var big = new ItemDefinition(ItemCodes.Parse(Shield), "Shield", ItemKind.Misc, GridWidth: 3, GridHeight: 3, MaxStack: 1) { CatalogKey = Shield };
        Assert.True(storage.Pages == ClanWarehouse.Pages && storage.ItemLimit == 140 && storage.View(character).Rows == StoreGrid.Rows * 2
            && storage.ItemLimit * 100 / WarehouseStorage.MaxItems == 140, "the clan warehouse is not 40% over the account's");

        var item = new ItemInstance(sword);
        character.Inventory.TryAdd(item);
        Assert.True(storage.TryDeposit(character, item.InstanceId, StoreGrid.Cells + 5) == NpcServiceStatus.Ok && storage.Page(1).Find(item.InstanceId) == 5,
            "a deposit on the second page");
        Assert.True(storage.TryMove(item.InstanceId, 2) == NpcServiceStatus.Ok && storage.Page(0).Find(item.InstanceId) == 2 && storage.Page(1).Find(item.InstanceId) < 0,
            "an item did not move to the first page");
        Assert.True(storage.TryMove(item.InstanceId, StoreGrid.Cells * 2) == NpcServiceStatus.Rejected, "an item moved past the last page");

        // A 3x3 item never spans two pages: the last rows of page one hold none.
        var shield = new ItemInstance(big);
        character.Inventory.TryAdd(shield);
        Assert.True(storage.TryDeposit(character, shield.InstanceId, 8 * StoreGrid.Columns) != NpcServiceStatus.Ok && storage.Find(shield.InstanceId) is null,
            "a large item spanned two pages");

        var full = ClanWarehouse.Create();
        for (var cell = 0; cell < StoreGrid.Cells; cell++)
        {
            Assert.True(full.Restore(new ItemInstance(sword), cell), "page one did not fill");
        }

        var spill = new ItemInstance(sword);
        character.Inventory.TryAdd(spill);
        Assert.True(full.TryDeposit(character, spill.InstanceId, -1) == NpcServiceStatus.Ok && full.Page(1).Find(spill.InstanceId) == 0, "a deposit did not spill to page two");
        while (full.Count < ClanWarehouse.MaxItems)
        {
            Assert.True(full.Restore(new ItemInstance(sword), StoreGrid.Cells + full.Count - StoreGrid.Cells + 1), "page two did not fill");
        }

        var extra = new ItemInstance(sword);
        character.Inventory.TryAdd(extra);
        Assert.True(full.TryDeposit(character, extra.InstanceId, -1) == NpcServiceStatus.NoSpace && !full.Restore(new ItemInstance(sword), 2 * StoreGrid.Cells - 1),
            "the clan warehouse took a 141st item");

        var catalog = new ItemCatalog([sword, big]);
        var record = WarehouseStateCodec.CaptureItems(storage);
        var restored = WarehouseStateCodec.RestoreItems(ClanWarehouse.Create(storage.Revision), record, catalog);
        Assert.True(restored.Occupied().Select(static cell => (cell.Anchor, cell.Item.InstanceId)).SequenceEqual(storage.Occupied().Select(static cell => (cell.Anchor, cell.Item.InstanceId))),
            "the pages did not round-trip");
        Assert.Throws<InvalidDataException>(() => WarehouseStateCodec.RestoreItems(new WarehouseStorage(), WarehouseStateCodec.CaptureItems(full), catalog),
            "the account warehouse took the clan's 140 items");
        return Task.CompletedTask;
    }

    private static async Task WarehouseWorldAsync()
    {
        await using var world = await ClanWorld.CreateAsync();
        var chief = await world.JoinAsync("Chief", Wolves, ClanPermission.All, Sword);
        var depositor = await world.JoinAsync("Depositor", Wolves, ClanPermission.Deposit, Shield);
        var rival = await world.JoinAsync("Rival", Bears, ClanPermission.All);
        var loner = await world.JoinAsync("Loner", 0, ClanPermission.None);
        Assert.True(chief.Player.InVillage && world.KeeperId != 0, "the fixture's field is not a town or has no keeper");
        Assert.True((await world.WarehouseAsync(chief, NpcServiceAction.Open, npc: 0)).Outcome.Status == NpcServiceStatus.OutOfReach,
            "the clan warehouse opened without its keeper");

        Assert.True((await world.WarehouseAsync(loner, NpcServiceAction.Open)).Outcome.Status == NpcServiceStatus.NotOpen, "a player without a clan opened one");
        var opened = await world.WarehouseAsync(chief, NpcServiceAction.Open);
        Assert.True(opened.Outcome.Ok && opened.Warehouse is { Items.Count: 0, Rows: 18 }, "the chief did not open the clan's empty warehouse");
        var sword = chief.Item(Sword)!;
        Assert.True((await world.WarehouseAsync(depositor, NpcServiceAction.Deposit, depositor.Item(Shield)!.InstanceId, 3)).Outcome.Status == NpcServiceStatus.NotOpen,
            "a change was taken before the warehouse was opened");
        var stored = await world.WarehouseAsync(chief, NpcServiceAction.Deposit, sword.InstanceId, StoreGrid.Cells + 4);
        Assert.True(stored.Outcome.Ok && chief.Item(Sword) is null && stored.Warehouse?.Items is [{ Anchor: StoreGrid.Cells + 4 } shown] && shown.InstanceId == sword.InstanceId,
            "the deposit on page two");

        var seen = await world.WarehouseAsync(depositor, NpcServiceAction.Open);
        Assert.True(seen.Warehouse?.Items.Single().InstanceId == sword.InstanceId, "another member does not see the clan's item");
        var depositorShield = depositor.Item(Shield)!;
        Assert.True((await world.WarehouseAsync(depositor, NpcServiceAction.Deposit, depositorShield.InstanceId, 0)).Outcome.Ok, "a member could not deposit");
        Assert.True((await world.WarehouseAsync(depositor, NpcServiceAction.Withdraw, sword.InstanceId, -1)).Outcome.Status == NpcServiceStatus.NotPermitted
            && depositor.Item(Sword) is null, "a rank without the right took an item");
        world.Pass();
        Assert.True(world.Latest(chief)?.ClanWarehouse is { } pushed && pushed.Items.Any(item => item.InstanceId == depositorShield.InstanceId),
            "the chief's open view was not sent the member's change");

        var rivalView = await world.WarehouseAsync(rival, NpcServiceAction.Open);
        Assert.True(rivalView.Outcome.Ok && rivalView.Warehouse?.Items.Count == 0, "another clan sees this clan's items");
        Assert.True((await world.WarehouseAsync(rival, NpcServiceAction.Withdraw, sword.InstanceId, -1)).Outcome.Status == NpcServiceStatus.Rejected,
            "another clan took this clan's item");

        await world.TagAsync(depositor, 0, ClanPermission.None);
        Assert.True((await world.WarehouseAsync(depositor, NpcServiceAction.Refresh)).Outcome.Status == NpcServiceStatus.NotOpen, "a member who left kept the warehouse");
        Assert.True((await world.WarehouseAsync(chief, NpcServiceAction.Withdraw, depositorShield.InstanceId, -1)).Outcome.Ok && chief.Item(Shield) is not null,
            "the chief could not take an item out");

        var log = world.Storage.Log;
        Assert.True(log.Count == 3 && log[0] is { CharacterName: "Chief", Action: (short)ClanWarehouseLogAction.Deposit, ItemKey: Sword, Quantity: 1 }
            && log[1] is { CharacterName: "Depositor", ItemKey: Shield } && log[2] is { CharacterName: "Chief", Action: (short)ClanWarehouseLogAction.Withdraw },
            "the log lines differ");
        Assert.True(log.All(static line => line.ClanId == Wolves && line.At > 0 && line.ItemName.Length > 0), "a log line lacks its clan, time or name");
    }

    private static async Task WarehouseConcurrencyAsync()
    {
        var storage = new MemoryClanStorage();
        Guid swordId;
        await using (var world = await ClanWorld.CreateAsync(storage))
        {
            var chief = await world.JoinAsync("Chief", Wolves, ClanPermission.All, Sword);
            var vice = await world.JoinAsync("Vice", Wolves, ClanPermission.All);
            var sword = chief.Item(Sword)!;
            sword.UpgradeLevel = 7;
            sword.DurabilityMax = 160;
            sword.DurabilityCurrent = 99;
            sword.Rarity = ItemRarity.Epic;
            swordId = sword.InstanceId;
            await world.WarehouseAsync(chief, NpcServiceAction.Open);
            await world.WarehouseAsync(vice, NpcServiceAction.Open);
            Assert.True((await world.WarehouseAsync(chief, NpcServiceAction.Deposit, swordId, 0)).Outcome.Ok, "the deposit");
            await world.WarehouseAsync(vice, NpcServiceAction.Refresh);

            var both = await world.RunAsync(Task.WhenAll(world.WarehouseRequest(chief, NpcServiceAction.Withdraw, swordId, -1),
                world.WarehouseRequest(vice, NpcServiceAction.Withdraw, swordId, -1)));
            Assert.True(both.Count(static reply => reply.Outcome.Ok) == 1, "two members took the same item, or neither did");
            Assert.True((chief.Item(Sword) is null) != (vice.Item(Sword) is null), "the item is not in exactly one bag");
            var winner = chief.Item(Sword) is not null ? chief : vice;
            Assert.True(storage.Revision == 2 && !storage.Items.Value.Contains(swordId.ToString(), StringComparison.OrdinalIgnoreCase),
                "the stored warehouse still holds the taken item");
            Assert.True((await world.WarehouseAsync(winner, NpcServiceAction.Deposit, swordId, StoreGrid.Cells)).Outcome.Ok, "the item went back");
            Assert.True(storage.Log.Count(static line => line.Action == (short)ClanWarehouseLogAction.Withdraw) == 1, "the log counts two withdrawals");
        }

        await using (var restarted = await ClanWorld.CreateAsync(storage))
        {
            var chief = await restarted.JoinAsync("Chief", Wolves, ClanPermission.All);
            var reopened = await restarted.WarehouseAsync(chief, NpcServiceAction.Open);
            Assert.True(reopened.Warehouse?.Items.Single() is { Anchor: StoreGrid.Cells } back && back.InstanceId == swordId, "the item did not come back on page two");
            Assert.True((await restarted.WarehouseAsync(chief, NpcServiceAction.Withdraw, swordId, -1)).Outcome.Ok
                && chief.Item(Sword) is { UpgradeLevel: 7, DurabilityMax: 160, DurabilityCurrent: 99, Rarity: ItemRarity.Epic }, "the item lost its metadata");
        }
    }

    private static async Task SqliteAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "etervale-clan-storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            await using (var connection = await provider.GetRequiredService<IDbConnectionFactory>().OpenAsync())
            {
                await connection.ExecuteAsync("INSERT INTO accounts (user_id, password_hash) VALUES ('ann', 'x')");
            }

            var characters = provider.GetRequiredService<ICharacterRepository>();
            var ann = await characters.CreateAsync(CharacterRecordFactory.NewStarter(new CharacterClassRegistry(), 1, "Ann", 0, PlayerClass.Fighter, 3));
            var clans = provider.GetRequiredService<IClanRepository>();
            var clanId = await clans.FoundAsync(new ClanRecord(0, "Wolves", 1, 10, 0, "", 0), new ClanMemberRecord(0, ann.Id, (short)ClanRank.Chief, 10),
                ann with { SaveSeq = ann.SaveSeq + 1 });
            ann = (await characters.FindByNameForAccountAsync(1, "Ann"))!;
            var coins = provider.GetRequiredService<Persistence.Abstractions.Coins.ICoinRepository>();
            await coins.AddAsync(1, 5_000, "test");
            Assert.True(await clans.DonateCoinsAsync(clanId, 1, 4_500, ClanBuffs.MaxCoinTreasury, Gold(5, 4_500))
                && !await clans.DonateCoinsAsync(clanId, 1, 1_000, ClanBuffs.MaxCoinTreasury, Gold(5, 1_000)) && await coins.BalanceAsync(1) == 500,
                "the coins did not move once, or moved without the account holding them");
            Assert.True(await clans.SpendCoinsAsync(clanId, 4_000, Gold(6, 4_000)) && !await clans.SpendCoinsAsync(clanId, 4_000, Gold(6, 4_000))
                && (await clans.LoadAsync()).Clans.Single().CoinTreasury == 500, "the coin treasury paid twice or kept the wrong balance");
            Assert.True((await clans.LoadAsync()).Clans.Single().RankPermissions == ClanRankPermissions.Default.Packed, "a new clan has not the default rights");
            Assert.True(await clans.SetRankPermissionsAsync(clanId, 0x0103) && (await clans.LoadAsync()).Clans.Single().RankPermissions == 0x0103, "the rights were not stored");
            var donor = ann with { Money = 1, SaveSeq = ann.SaveSeq + 1 };
            ClanWarehouseLogRecord Gold(short action, int quantity) => new(clanId, 50, "Ann", action, "", "Gold", quantity);
            Assert.True(await clans.DonateAsync(clanId, 9_000_000, ClanUpgrades.MaxTreasury, donor, Gold(3, 9_000_000))
                && !await clans.DonateAsync(clanId, 2, 9_000_001, donor with { SaveSeq = donor.SaveSeq + 1 }, Gold(3, 2)), "the donation or the treasury cap");
            Assert.True(await clans.UpgradeAsync(clanId, ClanUpgradeKind.WarehouseSlots, 0, 5_000_000, Gold(4, 1))
                && !await clans.UpgradeAsync(clanId, ClanUpgradeKind.WarehouseSlots, 0, 1, Gold(4, 1))
                && !await clans.UpgradeAsync(clanId, ClanUpgradeKind.WarehouseWeight, 0, 4_000_001, Gold(4, 1)), "an upgrade's level or cost check");
            Assert.True((await clans.LoadAsync()).Clans.Single() is { Treasury: 4_000_000, SlotLevel: 1, WeightLevel: 0 }
                && (await characters.FindByNameForAccountAsync(1, "Ann"))!.Money == 1, "the treasury, levels or the donor's gold were not stored");
            ann = (await characters.FindByNameForAccountAsync(1, "Ann"))!;

            var storage = provider.GetRequiredService<IClanStorageRepository>();
            Assert.True(await storage.FindWarehouseAsync(clanId) is { Revision: 0, SlotLevel: 1 } && await storage.FindWarehouseAsync(clanId + 99) is null,
                "a new clan's warehouse does not come with its levels, or a missing clan has one");
            ClanWarehouseLogRecord Line(string item, short action) => new(clanId, 100, "Ann", action, item, item + " name", 1);
            var first = ann with { Money = 5, SaveSeq = ann.SaveSeq + 1 };
            Assert.True(await storage.SaveWarehouseWithCharacterAsync(first, new ClanWarehouseRecord(clanId, 1, new JsonText("[]")), 0, [Line("WA101", 1)]),
                "the first warehouse write");
            Assert.True(!await storage.SaveWarehouseWithCharacterAsync(first with { SaveSeq = first.SaveSeq + 1 }, new ClanWarehouseRecord(clanId, 2, new JsonText("[]")), 0,
                [Line("WA102", 2)]), "a stale warehouse write was stored");
            var second = first with { Money = 6, SaveSeq = first.SaveSeq + 1 };
            Assert.True(await storage.SaveWarehouseWithCharacterAsync(second, new ClanWarehouseRecord(clanId, 2, new JsonText("[]")), 1, [Line("WA103", 2)]),
                "the next warehouse write");
            Assert.True(await storage.FindWarehouseAsync(clanId) is { Revision: 2 } && (await characters.FindByNameForAccountAsync(1, "Ann"))!.Money == 6,
                "the warehouse and the character were not stored together");
            Assert.True(await storage.WarehouseLogAsync(clanId, 2) is [{ ItemKey: "WA103", Action: 2 }, { ItemKey: "WA101", Action: 1, ItemName: "WA101 name" }],
                "the log is not newest first, or kept a refused write's line");

            await storage.ReplaceBuffsAsync([new ClanBuffRecord(clanId, 9, 5000), new ClanBuffRecord(clanId + 99, 9, 5000)]);
            Assert.True(await storage.LoadBuffsAsync() is [{ Kind: 9, EndsAt: 5000 } buff] && buff.ClanId == clanId, "the buffs, or a buff of no stored clan");
            await storage.ReplaceBuffsAsync([new ClanBuffRecord(clanId, 10, 6000)]);
            Assert.True(await storage.LoadBuffsAsync() is [{ Kind: 10 }], "the buffs were not replaced");

            Assert.True(await clans.DisbandAsync(clanId, [ann.Id], 999), "the clan was not dissolved");
            Assert.True(await storage.FindWarehouseAsync(clanId) is null && (await storage.LoadBuffsAsync()).Count == 0 && (await storage.WarehouseLogAsync(clanId, 10)).Count == 0,
                "a dissolved clan kept its warehouse, log or buffs");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    private static async Task UpgradeRulesAsync()
    {
        Assert.True(ClanUpgrades.ItemLimit(0) == ClanWarehouse.MaxItems && ClanUpgrades.WeightLimit(0) == ClanWarehouse.MaxWeight
            && ClanUpgrades.Pages(0) == ClanWarehouse.Pages && ClanUpgrades.ItemLimit(ClanUpgrades.MaxLevel) == 240 && ClanUpgrades.Pages(5) == 3
            && ClanUpgrades.WeightLimit(5) == 3_200 && ClanUpgrades.ItemLimit(9) == 240, "the upgrade tables differ");
        Assert.True(ClanUpgrades.Cost(ClanUpgradeKind.WarehouseSlots, 0) == 5_000_000 && ClanUpgrades.Cost(ClanUpgradeKind.WarehouseSlots, 4) == 80_000_000
            && ClanUpgrades.Cost(ClanUpgradeKind.WarehouseWeight, 1) == 2 * ClanUpgrades.Cost(ClanUpgradeKind.WarehouseWeight, 0)
            && ClanUpgrades.Cost(ClanUpgradeKind.WarehouseSlots, ClanUpgrades.MaxLevel) == 0, "the upgrade costs differ");
        Assert.True(ClanUpgrades.CheckUpgrade(ClanRank.Vice, ClanUpgradeKind.WarehouseSlots, 0, long.MaxValue) == ClanStatus.NotChief
            && ClanUpgrades.CheckUpgrade(ClanRank.Chief, ClanUpgradeKind.WarehouseSlots, 0, 4_999_999) == ClanStatus.NotEnoughTreasury
            && ClanUpgrades.CheckUpgrade(ClanRank.Chief, ClanUpgradeKind.WarehouseWeight, 5, long.MaxValue) == ClanStatus.MaxLevel
            && ClanUpgrades.CheckUpgrade(ClanRank.Chief, ClanUpgradeKind.WarehouseSlots, 0, 5_000_000) == ClanStatus.Ok, "the upgrade checks differ");
        Assert.True(ClanUpgrades.CheckDonation(999, 0) != ClanStatus.Ok && ClanUpgrades.CheckDonation(1_000, ClanUpgrades.MaxTreasury) == ClanStatus.TreasuryFull
            && ClanUpgrades.CheckDonation(5_000_000, 0) == ClanStatus.Ok, "the donation checks differ");

        await using var world = await ClanWorld.CreateAsync();
        var chief = await world.JoinAsync("Chief", Wolves, ClanPermission.All, Sword);
        await world.WarehouseAsync(chief, NpcServiceAction.Open);
        var swordId = chief.Item(Sword)!.InstanceId;
        Assert.True((await world.WarehouseAsync(chief, NpcServiceAction.Deposit, swordId, StoreGrid.Cells + 3)).Outcome.Ok, "the deposit");
        await world.RunAsync(world.Server.ResizeClanWarehouseAsync(Wolves, 2, 3));
        var resized = await world.WarehouseAsync(chief, NpcServiceAction.Refresh);
        Assert.True(resized.Warehouse is { Rows: 3 * StoreGrid.Rows, MaxWeight: 2_400 } view && view.Items.Single() is { Anchor: StoreGrid.Cells + 3 } kept
            && kept.InstanceId == swordId, "the resized warehouse lost its item or limits");
        Assert.True((await world.WarehouseAsync(chief, NpcServiceAction.Move, swordId, 2 * StoreGrid.Cells)).Outcome.Ok, "an item could not move to the new page");
    }

    private static async Task CoinRulesAsync()
    {
        var hour = WorldBuffs.Argument(WorldBuffKind.Experience, false);
        var day = WorldBuffs.Argument(WorldBuffKind.Experience, true);
        Assert.True(ClanBuffs.CoinPrice(false) == 200 && ClanBuffs.CoinPrice(true) == 4_000, "the coin prices are not the world buff items'");
        Assert.True(ClanBuffs.CheckCoinDonation(0, 0) == ClanStatus.DonationOutOfRange && ClanBuffs.CheckCoinDonation(ClanBuffs.MaxCoinDonation + 1, 0) == ClanStatus.DonationOutOfRange
            && ClanBuffs.CheckCoinDonation(5, ClanBuffs.MaxCoinTreasury) == ClanStatus.TreasuryFull && ClanBuffs.CheckCoinDonation(500, 0) == ClanStatus.Ok,
            "the coin donation checks");
        Assert.True(ClanBuffs.CheckCoinSpend(ClanPermission.Deposit, hour, 10_000) == ClanStatus.NotPermitted
            && ClanBuffs.CheckCoinSpend(ClanPermission.SpendCoins, day, 3_999) == ClanStatus.NotEnoughTreasury
            && ClanBuffs.CheckCoinSpend(ClanPermission.SpendCoins, day, 4_000) == ClanStatus.Ok
            && ClanBuffs.CheckCoinSpend(ClanPermission.All, 99, 10_000) == ClanStatus.Unavailable, "the coin spend checks");

        await using var world = await ClanWorld.CreateAsync();
        var member = await world.JoinAsync("Member", Wolves, ClanPermission.Deposit);
        await world.RunAsync(world.Server.StartClanBuffAsync(Wolves, "Chief", day));
        world.Pass();
        Assert.True(world.Server.ClanBuffState is [{ ClanId: Wolves, Kind: (int)WorldBuffKind.Experience } end] && end.EndSeconds >= world.Now + WorldBuffs.DayDurationSeconds - 2
            && member.Premium.ClanBuffMask == WorldBuffs.Get((int)WorldBuffKind.Experience)!.Bit && world.Server.WorldBuffState.Length == 0,
            "a buff paid with coins did not start for the clan only");
    }

    private static Task KeepersAsync()
    {
        var keeper = new NpcDefinition(1, "Keeper", "k.inx", [new NpcService(NpcServiceKind.Warehouse)]);
        var smith = new NpcDefinition(2, "Smith", "s.inx", [new NpcService(NpcServiceKind.Shop)]);
        var added = ClanWarehouse.AddTo(ClanWarehouse.AddTo([keeper, smith]));
        Assert.True(added.Count == 3 && added[2] is { Code: ClanWarehouse.KeeperCode, ModelInxPath: "k.inx", Label: ClanWarehouse.KeeperName }
            && added[2].Services is [{ Kind: NpcServiceKind.ClanWarehouse }] && added[0].Find(NpcServiceKind.ClanWarehouse) is null,
            "the Clan Warehouse keeper was not made once, as a warehouse keeper offering only the clan warehouse");
        Assert.True(NpcServiceAvailability.IsAvailable(new NpcService(NpcServiceKind.ClanWarehouse)), "the clan warehouse is offered as not running");
        Assert.True(ClanWarehouse.AddTo([smith]).Count == 1, "a keeper was made without a warehouse keeper's model");
        Assert.True(WorldBuffs.All.All(static buff => UsableItemCatalog.TryGet(ClanBuffs.ItemKey(buff, false), out var hour) && hour.Action == UsableItemAction.ClanBuff
            && UsableItemCatalog.TryGet(ClanBuffs.ItemKey(buff, true), out var day) && day.Argument == WorldBuffs.Argument(buff.Kind, true))
            && ClanBuffs.IsItem("CL101") && ClanBuffs.IsItem("cl210") && !ClanBuffs.IsItem("WB101") && ClanBuffs.Name(WorldBuffs.All[8]) == "Clan EXP Buff",
            "the clan buff items");
        return Task.CompletedTask;
    }

    private static Task WindowAsync()
    {
        var window = new Gameplay.UI.Npc.HudClanWarehouseWindow();
        var item = new OwnedItemView(Guid.NewGuid(), 1, "Sword", ItemKind.Misc, 1, 0, -1, 1, 1, Sword);
        var view = new WarehouseView(1, 0, 0, ClanWarehouse.MaxWeight, StoreGrid.Columns, StoreGrid.Rows * ClanWarehouse.Pages, [item]);
        var member = new Client.Net.Presence.ClanInfo(Wolves, "Wolves", ClanState.Registered, 0, ClanRank.Member, 20, "", 0, [], ClanRankPermissions.Default,
            ClanPermission.Deposit | ClanPermission.Withdraw);
        window.Show(view, member);
        for (var step = 0; step < 30; step++)
        {
            window.Advance(0.05);
        }

        var origin = window.Origin;
        var released = new Microsoft.Xna.Framework.Input.MouseState();
        var pressed = new Microsoft.Xna.Framework.Input.MouseState(0, 0, 0, Microsoft.Xna.Framework.Input.ButtonState.Pressed, 0, 0, 0, 0);
        Gameplay.UI.Inventory.HudPlacement None(OwnedItemView carried, Microsoft.Xna.Framework.Point at) => default;
        var grid = Gameplay.UI.Npc.HudClanWarehouseWindow.Grid(origin);
        var cell = new Microsoft.Xna.Framework.Point(grid.X + 5, grid.Y + 5);
        window.Update(released, released, cell, None, false, false);
        Assert.True(window.Update(pressed, released, cell, None, false, false).Kind == Gameplay.UI.Npc.HudWarehouseActionKind.Lifted && window.Dragging == item.InstanceId,
            "the stored item was not picked up");
        var next = Gameplay.UI.Npc.HudClanWarehouseWindow.Next(origin).Center;
        Assert.True(window.Update(pressed, released, next, None, false, false).Kind == Gameplay.UI.Npc.HudWarehouseActionKind.Consumed && window.Page == 1
            && window.Dragging == item.InstanceId, "Next did not turn the page with the item carried");
        var set = window.Update(pressed, released, cell, None, false, false);
        Assert.True(set is { Kind: Gameplay.UI.Npc.HudWarehouseActionKind.Move, Anchor: StoreGrid.Cells }, "the item was not set down on the second page");
        var logTab = Gameplay.UI.Npc.HudClanWarehouseWindow.TabButton(origin, 1).Center;
        Assert.True(window.Update(pressed, released, logTab, None, false, false).Kind != Gameplay.UI.Npc.HudWarehouseActionKind.ClanLog,
            "a rank without the right opened the log");
        window.ObserveClan(member with { Rights = ClanPermission.All });
        Assert.True(window.Update(pressed, released, logTab, None, false, false).Kind == Gameplay.UI.Npc.HudWarehouseActionKind.ClanLog, "the log tab did not ask for the log");
        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        var buffer = new byte[Protocol.Binary.FrameCodec.MaxFrameSize];
        var state = new ClanStatePacket(7, "Wolves", 1, 0, (byte)ClanRank.Vice, 20, "", 0, [], (ushort)ClanRankPermissions.Default.Packed, (byte)ClanPermission.All);
        var read = ClanStatePacket.Read(buffer.AsSpan(0, state.Write(buffer)));
        Assert.True(read.Permissions == 0x1F03 && read.Rights == (byte)ClanPermission.All && read.Name == "Wolves", "the clan's rights");

        var lines = Enumerable.Range(0, Contracts.Social.ClanLimits.WarehouseLogLines)
            .Select(static index => new ClanWarehouseLogWire(1000 + index, new string('n', 30), 1, "WA101", new string('x', 60), index)).ToArray();
        var log = ClanWarehouseLogPacket.Read(buffer.AsSpan(0, new ClanWarehouseLogPacket(lines).Write(buffer)));
        Assert.True(log.Lines.Count == lines.Length && log.Lines[3] is { At: 1003, Quantity: 3, ItemKey: "WA101" } && ClanWarehouseLogPacket.MaxSize <= buffer.Length,
            "the log does not fit one frame or round-trip");

        var clanBuffs = WorldBuffPacket.Read(buffer.AsSpan(0, new WorldBuffPacket([(9, 60)], Clan: true).Write(buffer)), clan: true);
        Assert.True(clanBuffs is { Clan: true } && clanBuffs.Buffs[0] == (9, 60)
            && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4)) == Protocol.Binary.EtervaleOpcodes.ClanBuffState, "the clan's buffs");

        var items = Enumerable.Range(0, ClanWarehouse.MaxItems)
            .Select(static index => new OwnedItemView(Guid.NewGuid(), 1, "Item", ItemKind.Misc, 1, index, -1, 1, 1, "WA101")).ToArray();
        var view = new WarehouseView(3, 0, 140, ClanWarehouse.MaxWeight, StoreGrid.Columns, StoreGrid.Rows * 2, items);
        WarehouseView joined = null!;
        foreach (var part in Server.Net.NpcWireMapping.WarehouseParts(Guid.Empty, 0, view, Protocol.Binary.EtervaleOpcodes.ClanWarehouseState))
        {
            var packet = WarehousePacket.Read(buffer.AsSpan(0, part.Write(buffer)));
            var partView = new WarehouseView(packet.Revision, packet.Money, packet.Weight, packet.MaxWeight, packet.Columns, packet.Rows,
                packet.Items.Select(static entry => new OwnedItemView(entry.InstanceId, entry.Code, entry.Name, (ItemKind)entry.Kind, entry.Count, entry.Anchor, -1,
                    entry.Width, entry.Height, "WA101")).ToArray());
            joined = WarehouseView.Join(joined, partView);
            joined = WarehouseView.Join(joined, partView);
        }

        Assert.True(joined.Items.Count == ClanWarehouse.MaxItems && joined.Items.Select(static item => item.InstanceId).SequenceEqual(items.Select(static item => item.InstanceId)),
            "a warehouse in parts did not join once each");
        Assert.True(WarehouseView.Join(joined, view with { Revision = 4, Items = [] }).Items.Count == 0, "a new revision did not replace the parts");
        return Task.CompletedTask;
    }

    // A clan warehouse and buff store in memory that checks revisions as the database does.
    internal sealed class MemoryClanStorage : IClanStorageRepository
    {
        private readonly Lock _gate = new();

        public long Revision { get; private set; }
        public JsonText Items { get; private set; } = new("[]");
        public List<ClanWarehouseLogRecord> Log { get; } = [];
        public List<ClanBuffRecord> Buffs { get; private set; } = [];

        public Task<ClanWarehouseRecord?> FindWarehouseAsync(long clanId, CancellationToken ct = default)
        {
            lock (_gate)
            {
                return Task.FromResult(clanId == Wolves && Revision > 0 ? new ClanWarehouseRecord(clanId, Revision, Items) : null);
            }
        }

        public async Task<bool> SaveWarehouseWithCharacterAsync(CharacterRecord character, ClanWarehouseRecord warehouse, long expectedRevision,
            IReadOnlyList<ClanWarehouseLogRecord> log, CancellationToken ct = default)
        {
            await Task.Yield();
            lock (_gate)
            {
                if (warehouse.ClanId != Wolves || Revision != expectedRevision)
                {
                    return false;
                }

                Revision = warehouse.Revision;
                Items = warehouse.Items;
                Log.AddRange(log);
                return true;
            }
        }

        public Task<IReadOnlyList<ClanWarehouseLogRecord>> WarehouseLogAsync(long clanId, int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ClanWarehouseLogRecord>>(Log.Where(line => line.ClanId == clanId).Reverse().Take(limit).ToArray());

        public Task<IReadOnlyList<ClanBuffRecord>> LoadBuffsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ClanBuffRecord>>(Buffs);

        public Task ReplaceBuffsAsync(IReadOnlyList<ClanBuffRecord> buffs, CancellationToken ct = default)
        {
            Buffs = [.. buffs];
            return Task.CompletedTask;
        }
    }

    // Players of several clans in a town of one world, tagged as the clan service tags them, with the NPC and item services
    // over a memory clan storage; snapshots are kept per player.
    private sealed class ClanWorld : IAsyncDisposable
    {
        private const int Field = BellatraArena.Village;
        private const int KeeperAt = 100;
        private uint _tick;
        private int _members;

        public required WorldServer Server { get; init; }
        public required SessionRegistry Sessions { get; init; }
        public required BellatraGameTests.GameClock Clock { get; init; }
        public required NpcAppService Npc { get; init; }
        public required ItemUseAppService Items { get; init; }
        public required MemoryClanStorage Storage { get; init; }
        public required Dictionary<string, ItemDefinition> Definitions { get; init; }
        public required CharacterClassRegistry Classes { get; init; }
        public ulong KeeperId { get; private set; }

        public long Now => Clock.Now.ToUnixTimeSeconds();

        public static Task<ClanWorld> CreateAsync(MemoryClanStorage? storage = null)
        {
            var definitions = new Dictionary<string, ItemDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                [Sword] = new ItemDefinition(ItemCodes.Parse(Sword), "Sword", ItemKind.Misc, MaxStack: 1) { CatalogKey = Sword },
                [Shield] = new ItemDefinition(ItemCodes.Parse(Shield), "Shield", ItemKind.Misc, GridWidth: 2, GridHeight: 2, MaxStack: 1) { CatalogKey = Shield }
            };
            foreach (var definition in WorldBuffs.ItemDefinitions().Concat(ClanBuffs.ItemDefinitions()))
            {
                definitions[definition.CatalogKey] = definition;
            }

            var empty = WorldData.Empty;
            // A warehouse keeper in the town, given the clan warehouse as the world data load gives it.
            var keeper = new NpcDefinition(1, "Keeper", "keeper.inx", [new NpcService(NpcServiceKind.Warehouse)]) { DisplayName = "Keeper" };
            var npcs = ClanWarehouse.AddTo([keeper]);
            var spawns = new Dictionary<int, Simulation.World.Fields.FieldSpawnData>
            {
                [Field] = Simulation.World.Fields.FieldSpawnData.Empty with { FixedNpcs = [new Simulation.World.Fields.FixedNpc(ClanWarehouse.KeeperCode, KeeperAt * 256, KeeperAt * 256, 0)] }
            };
            var holder = new WorldDataHolder { Current = new WorldData(new ItemCatalog(definitions.Values), empty.Monsters, new NpcCatalog(npcs), spawns, empty.Motions) };
            var clock = new BellatraGameTests.GameClock();
            var world = new WorldServer(holder, new WorldCatalog(), clock);
            var sessions = new SessionRegistry();
            var classes = new CharacterClassRegistry();
            var saves = new ImmediateSaves(new MemoryCharacters(CharacterRecordFactory.NewStarter(classes, 99, "Nobody", 0, PlayerClass.Fighter, Field) with { Id = 99 }));
            var memory = storage ?? new MemoryClanStorage();
            return Task.FromResult(new ClanWorld
            {
                Server = world,
                Sessions = sessions,
                Clock = clock,
                Npc = new NpcAppService(sessions, saves, new MemoryWarehouses(), world, holder, clanStorage: memory),
                Items = new ItemUseAppService(sessions, saves, world),
                Storage = memory,
                Definitions = definitions,
                Classes = classes
            });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async Task<Member> JoinAsync(string name, long clanId, ClanPermission rights, params string[] keys)
        {
            var id = ++_members;
            var session = Sessions.Create(id, name);
            session.Character = Classes.CreatePlayer(PlayerClass.Fighter, name, 60);
            foreach (var key in keys)
            {
                session.Character.Inventory.TryAdd(new ItemInstance(Definitions[key]));
            }

            session.Loaded = CharacterRecordFactory.NewStarter(Classes, id, name, 0, PlayerClass.Fighter, Field) with { Id = id };
            session.SpawnPosition = new System.Numerics.Vector3(KeeperAt, 0, KeeperAt);
            Assert.True(session.TryBeginEntry() && session.CompleteEntry(true) && session.TryBeginPlay(), "a member did not begin play");
            var player = WorldPlayerFactory.Create(session);
            var outbound = Channel.CreateUnbounded<WorldSnapshot>();
            await RunAsync(Server.JoinAsync(Field, player, outbound.Writer, session));
            var member = new Member(session, player, outbound.Reader);
            if (KeeperId == 0 && outbound.Reader.TryPeek(out var first))
            {
                KeeperId = first.Entities.Where(static entity => entity.Kind == Contracts.World.EntityKind.Npc).Select(static entity => entity.EntityId).FirstOrDefault();
            }

            await TagAsync(member, clanId, rights);
            return member;
        }

        public Task TagAsync(Member member, long clanId, ClanPermission rights) =>
            RunAsync(Server.SetClanTagAsync(member.Session.EntityId, clanId == 0 ? null : new ClanTag(clanId, clanId == Wolves ? "Wolves" : "Bears", 0, true, rights)));

        public Task<ItemUseReply> UseAsync(Member member, string key, int argument) =>
            RunAsync(Items.UseAsync(member.Session.SessionToken, new ItemUseCommand(Guid.NewGuid(), member.Session.Character!.InventoryRevision,
                member.Item(key)!.InstanceId, argument)));

        public Task<NpcServiceReply> WarehouseRequest(Member member, NpcServiceAction action, Guid itemId = default, int anchor = -1, ulong? npc = null)
        {
            var command = new NpcServiceCommand(Guid.NewGuid(), npc ?? KeeperId, NpcServiceKind.ClanWarehouse, action, member.Session.Character!.InventoryRevision,
                member.SeenRevision, ItemId: itemId, Anchor: anchor);
            return Npc.RequestAsync(member.Session.SessionToken, command).ContinueWith(task =>
            {
                if (task.Result.Warehouse is { } view)
                {
                    member.SeenRevision = view.Revision;
                }

                return task.Result;
            }, TaskScheduler.Default);
        }

        public Task<NpcServiceReply> WarehouseAsync(Member member, NpcServiceAction action, Guid itemId = default, int anchor = -1, ulong? npc = null) =>
            RunAsync(WarehouseRequest(member, action, itemId, anchor, npc));

        // A second of the world, so its once-a-second pass runs.
        public void Pass()
        {
            Clock.Now = Clock.Now.AddSeconds(1);
            Server.Tick(0.05f, ++_tick);
        }

        public WorldSnapshot? Latest(Member member)
        {
            while (member.Snapshots.TryRead(out var snapshot))
            {
                member.Last = snapshot;
                if (snapshot.ClanWarehouse is { } view)
                {
                    member.SeenRevision = view.Revision;
                }
            }

            return member.Last;
        }

        public async Task RunAsync(Task task) => await WorldPump.RunAsync(Server, task, () => ++_tick);

        public Task<T> RunAsync<T>(Task<T> task) => WorldPump.RunAsync(Server, task, () => ++_tick);
    }

    private sealed class Member(PlayerSession session, WorldPlayer player, ChannelReader<WorldSnapshot> snapshots)
    {
        public PlayerSession Session { get; } = session;
        public WorldPlayer Player { get; } = player;
        public ChannelReader<WorldSnapshot> Snapshots { get; } = snapshots;
        public WorldSnapshot? Last { get; set; }
        public long SeenRevision { get; set; }

        public PremiumTimers Premium => Session.Character!.Premium;

        public ItemInstance? Item(string key) => Session.Character!.OwnedItems().FirstOrDefault(item => item.Definition.CatalogKey == key);
    }
}
