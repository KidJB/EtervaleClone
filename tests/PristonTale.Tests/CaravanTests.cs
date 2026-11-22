using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Caravans;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

internal static class CaravanTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("caravans: a crystal calls its caravan, adds its time to the same one out, and is refused while another is out", RulesAsync);
        yield return ("caravans: the caravan out saves with the character, and its storage is stored with it under its revision", StorageAsync);
        yield return ("caravans: the caravan travels in the pet collection and the looks", WireAsync);
        yield return ("caravans: the cursor picks a caravan by its body, scaled with its model", PickAsync);
        yield return ("caravans: a caravan leaves when its time runs out or when dismissed; only one out takes a name, stays or follows", WorldAsync);
    }

    private static Task RulesAsync()
    {
        var hopy = Caravans.Get(1)!;
        Assert.True(Caravans.IsItem(hopy.ItemKey) && UsableItemCatalog.TryGet(hopy.ItemKey, out var rule)
            && rule.Action == UsableItemAction.Caravan && rule.Argument == 1, "a caravan crystal is not usable");
        Assert.True(Caravans.All.Select(static kind => kind.ItemKey).Distinct().Count() == Caravans.All.Count, "two caravans share a crystal");

        var called = Caravans.Call(null, 1, 1000);
        Assert.True(called == new CaravanState(1, 1000 + hopy.Hours * 3600L), "a crystal did not call its caravan");
        var named = called with { Name = "Pack" };
        Assert.True(Caravans.Call(named, 1, 2000) == named with { ExpiresAt = named.ExpiresAt + hopy.Hours * 3600L }, "the same caravan's time was not extended");
        Assert.True(Caravans.Call(named, 2, 2000) is null, "a caravan of another kind replaced the one out");
        Assert.True(Caravans.Call(named, 2, named.ExpiresAt) is { Kind: 2, Name: "Pack" }, "a caravan whose time ran out blocked a new one");
        Assert.True(Caravans.Call(null, 99, 0) is null, "an unknown caravan was called");

        Assert.True(Caravans.TryParse("/caravan name  Mule ", out var command, out var raw) && command == CaravanCommand.Name && raw == "Mule"
            && Caravans.TryParse("/caravan name", out _, out var empty) && empty.Length == 0 && !Caravans.TryParse("/caravanx", out _, out _),
            "the caravan name command");
        Assert.True(Caravans.TryParse(Caravans.FollowCommand(true), out var follow, out _) && follow == CaravanCommand.Follow
            && Caravans.TryParse(Caravans.FollowCommand(false), out var stay, out _) && stay == CaravanCommand.Stay
            && Caravans.TryParse(Caravans.DismissCommand, out var dismiss, out _) && dismiss == CaravanCommand.Dismiss
            && !Caravans.TryParse("/caravan stay now", out _, out _) && !Caravans.TryParse("/caravan fly", out _, out _)
            && Caravans.TryParse(Caravans.NameCommand("Mule"), out _, out var named2) && named2 == "Mule", "the caravan's commands");
        Assert.True(Caravans.All.Count == 12 && Caravans.All.Select(static (kind, index) => kind.Id == index + 1).All(static ok => ok)
            && Caravans.Get(12)?.Name == "Caravan Sir Knight" && Caravans.Get(13) is null, "the caravans are not ConquestPT's twelve");
        Assert.True(Caravans.Shown(named) == "Pack" && Caravans.Shown(called) == hopy.Name && Caravans.Shown(null) == "", "the caravan's shown name");
        Assert.True(NpcServiceKind.Caravan == (NpcServiceKind)26, "the caravan service changed its number");
        return Task.CompletedTask;
    }

    private static async Task StorageAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "etervale-caravans-" + Guid.NewGuid().ToString("N"));
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
            var created = await characters.CreateAsync(CharacterRecordFactory.NewStarter(new CharacterClassRegistry(), 1, "Ann", 0, PlayerClass.Fighter, 3));
            var caravan = new CaravanState(3, 123456, "Mule");
            Assert.True(CaravanCodec.Restore(CaravanCodec.Capture(caravan)) == caravan && CaravanCodec.Capture(null).Length == 0
                && CaravanCodec.Restore("") is null, "the caravan codec");

            var storage = provider.GetRequiredService<ICaravanStorageRepository>();
            Assert.True(await storage.FindAsync(created.Id) is null, "a new character has a caravan storage");
            var saved = created with { Caravan = CaravanCodec.Capture(caravan), Money = 77 };
            Assert.True(await storage.SaveWithCharacterAsync(saved, new CaravanStorageRecord(created.Id, 1, new JsonText("[]")), 0), "the first storage write");
            Assert.True(!await storage.SaveWithCharacterAsync(saved, new CaravanStorageRecord(created.Id, 2, new JsonText("[]")), 0),
                "a stale storage write was stored");
            Assert.True(await storage.SaveWithCharacterAsync(saved, new CaravanStorageRecord(created.Id, 2, new JsonText("[]")), 1), "the next storage write");
            Assert.True(await storage.FindAsync(created.Id) is { Revision: 2 }, "the storage's revision");
            var reloaded = (await characters.ListByAccountAsync(1)).Single();
            Assert.True(CaravanCodec.Restore(reloaded.Caravan) == caravan && reloaded.Money == 77, "the caravan did not save with the character");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    private static Task PickAsync()
    {
        var feet = new Microsoft.Xna.Framework.Vector3(100f, 0f, 100f);
        var down = new Microsoft.Xna.Framework.Ray(feet + new Microsoft.Xna.Framework.Vector3(0f, 500f, 0f), -Microsoft.Xna.Framework.Vector3.UnitY);
        var beside = new Microsoft.Xna.Framework.Ray(feet + new Microsoft.Xna.Framework.Vector3(60f, 500f, 0f), -Microsoft.Xna.Framework.Vector3.UnitY);
        Assert.True(Gameplay.World.Remote.SkinPetPresentation.PickAt(down, feet, 1f) is { } distance && distance > 400f && distance < 500f,
            "a ray onto the caravan missed it");
        Assert.True(Gameplay.World.Remote.SkinPetPresentation.PickAt(beside, feet, 1f) is null, "a ray beside the caravan picked it");
        Assert.True(Gameplay.World.Remote.SkinPetPresentation.PickAt(beside, feet, 2f) is not null, "a larger caravan is not picked by its wider body");
        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        var buffer = new byte[PetCollectionPacket.MaxSize];
        var collection = new PetCollectionPacket(5, 1) { Name = "Rex", Caravan = 4, CaravanEnds = 99_000, CaravanName = "Mule", CaravanStays = true };
        var read = PetCollectionPacket.Read(buffer.AsSpan(0, collection.Write(buffer)));
        Assert.True(read.Caravan == 4 && read.CaravanEnds == 99_000 && read.CaravanName == "Mule" && read.CaravanStays && read.Name == "Rex",
            "the pet collection's caravan");
        return Task.CompletedTask;
    }

    private static async Task WorldAsync()
    {
        var game = await BellatraGameTests.Arena.CreateAsync(PristonTale.Simulation.World.Bellatra.BellatraMode.Always, 50);
        var character = game.Session.Character!;
        Assert.True(!await WorldPump.RunAsync(game.World, game.World.NameCaravanAsync(game.Player.EntityId, "Mule")), "a name was taken without a caravan");

        var now = game.Clock.Now.ToUnixTimeSeconds();
        character.Caravan = new CaravanState(2, now + 3600);
        Assert.True(await WorldPump.RunAsync(game.World, game.World.NameCaravanAsync(game.Player.EntityId, "Mule")) && character.Caravan.Name == "Mule",
            "the caravan out was not named");
        Assert.True(game.Player.Appearance.Caravan == 2 && game.Player.Appearance.CaravanName == "Mule", "the caravan is not in the looks");
        Assert.True(await WorldPump.RunAsync(game.World, game.World.SetCaravanStaysAsync(game.Player.EntityId, true)) && character.Caravan.Stays
            && game.Player.Appearance.CaravanStays, "the caravan did not stay");
        Assert.True(await WorldPump.RunAsync(game.World, game.World.SetCaravanStaysAsync(game.Player.EntityId, false)) && !character.Caravan.Stays,
            "the caravan did not follow again");
        Assert.True(CaravanCodec.Restore(CaravanCodec.Capture(character.Caravan with { Stays = true }))!.Stays, "staying was not saved");
        Assert.True(await WorldPump.RunAsync(game.World, game.World.DismissCaravanAsync(game.Player.EntityId)) && character.Caravan is null
            && !await WorldPump.RunAsync(game.World, game.World.DismissCaravanAsync(game.Player.EntityId)), "the caravan was not dismissed");

        character.Caravan = new CaravanState(2, now - 1, "Mule");
        game.World.Tick(0.05f, 4000);
        Assert.True(character.Caravan is null && game.Player.Appearance.Caravan == 0, "a caravan stayed after its time ran out");
    }
}
