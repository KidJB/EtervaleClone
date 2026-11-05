using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.ItemBoxes;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Tests;

// The Item Distributor: an item round-trips through its saved form; taking, discarding and sending write the box and the
// character together; its messages travel on the wire.
internal static class ItemBoxTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("item box: an item keeps its values through the box's saved form", CodecAsync);
        yield return ("item box: taking, discarding and sending write the box and the character in one transaction", StorageAsync);
        yield return ("item box: its open, entries, actions and results travel on the wire", WireAsync);
    }

    private static Task CodecAsync()
    {
        var definition = new ItemDefinition(ItemCodes.Parse("WA101"), "Stone Axe", ItemKind.Axe) { CatalogKey = "WA101" };
        var catalog = new ItemCatalog([definition]);
        var item = new ItemInstance(definition, 1);
        var back = ItemBoxCodec.Restore(ItemBoxCodec.Capture(item), catalog);
        Assert.True(back.InstanceId == item.InstanceId && back.Definition.Code == definition.Code && back.Count == 1, "the item did not survive the box");
        return Task.CompletedTask;
    }

    private static async Task StorageAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-itembox-{Guid.NewGuid():N}");
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
            var ann = await characters.CreateAsync(CharacterRecordFactory.NewStarter(new CharacterClassRegistry(), 1, "Ann", 0, PlayerClass.Fighter, 3));
            var box = provider.GetRequiredService<IItemBoxRepository>();
            Assert.True(await box.AccountOfCharacterAsync("ann") == 1 && await box.AccountOfCharacterAsync("Nobody") is null, "the recipient lookup failed");

            var first = await box.AddAsync(2, "Server", "{\"a\":1}");
            Assert.True(await box.FindAsync(2, first) is { Sender: "Server" } && await box.FindAsync(1, first) is null && (await box.ListAsync(2)).Count == 1,
                "an added item is not the account's alone");

            Assert.True(await box.SendWithCharacterAsync(ann with { Money = 7 }, 2, "Ann", "{\"b\":2}")
                && (await characters.FindByNameForAccountAsync(1, "Ann"))!.Money == 7 && (await box.ListAsync(2)).Count == 2,
                "a send did not write the sender and the box together");

            Assert.True(!await box.TakeWithCharacterAsync(first, ann with { Money = 9 }) && (await characters.FindByNameForAccountAsync(1, "Ann"))!.Money == 7,
                "another account's item was taken");
            var bob = await characters.CreateAsync(CharacterRecordFactory.NewStarter(new CharacterClassRegistry(), 2, "Bob", 0, PlayerClass.Fighter, 3));
            Assert.True(await box.TakeWithCharacterAsync(first, bob with { Money = 5 }) && !await box.TakeWithCharacterAsync(first, bob with { Money = 6 })
                && (await characters.FindByNameForAccountAsync(2, "Bob"))!.Money == 5, "an item was not taken once with the character");

            var left = (await box.ListAsync(2)).Single();
            Assert.True(await box.DiscardAsync(2, left.Id) && (await box.ListAsync(2)).Count == 0, "a discard kept the item");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task WireAsync()
    {
        var buffer = new byte[FrameCodec.MaxFrameSize];
        var operation = Guid.NewGuid();
        var open = new ItemBoxOpenPacket(operation, 77);
        Assert.True(ItemBoxOpenPacket.Read(buffer.AsSpan(0, open.Write(buffer))) == open, "the open did not survive the wire");
        var item = new StoreItemEntry(Guid.NewGuid(), 0x0101_0100, 1, 1, 0, 2, 3, new string('N', 40));
        var entry = new ItemBoxEntryPacket(operation, 5, 1_700_000_000_000, new string('S', 64), item, new ItemDetailEntry { InstanceId = item.InstanceId, DamageMin = 4 });
        var length = entry.Write(buffer);
        var read = ItemBoxEntryPacket.Read(buffer.AsSpan(0, length));
        Assert.True(length <= ItemBoxEntryPacket.MaxSize && read is { Id: 5, CreatedAtUnixMs: 1_700_000_000_000 } && read.Item == item && read.Details.DamageMin == 4,
            "a full entry did not fit or survive the wire");
        var list = new ItemBoxListPacket(operation, 0, 3);
        Assert.True(ItemBoxListPacket.Read(buffer.AsSpan(0, list.Write(buffer))) == list, "the list end did not survive the wire");
        var action = new ItemBoxActionPacket(operation, 77, ItemBoxWire.ActionSend, 0, Guid.NewGuid(), "Bob");
        Assert.True(ItemBoxActionPacket.Read(buffer.AsSpan(0, action.Write(buffer))) == action, "the action did not survive the wire");
        var result = new ItemBoxResultPacket(operation, 4);
        Assert.True(ItemBoxResultPacket.Read(buffer.AsSpan(0, result.Write(buffer))) == result, "the result did not survive the wire");
        return Task.CompletedTask;
    }
}
