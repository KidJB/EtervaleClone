using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Achievements;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Achievements;
using PristonTale.Simulation.Characters;

namespace PristonTale.Tests;

internal static class AchievementTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a top level tier is crossed once, a jump past several crosses each, and only staff badges can be granted", RulesAsync);
        yield return ("a badge's holder key is unique: the first of a class wins a top badge, a character holds each granted one once", StorageAsync);
        yield return ("the world claims a top level badge when a player reaches its level, not for a game master", ClaimAsync);
        yield return ("the badge list crosses the wire", WireAsync);
        yield return ("a player's title is its best badge with a top badge's class, and shows in its looks", TitleAsync);
    }

    private static Task RulesAsync()
    {
        var tier = AchievementRules.TopLevels[0];
        Assert.True(AchievementRules.Crosses(tier, tier.Level - 1, tier.Level) && AchievementRules.Crosses(tier, 1, 200), "a reached tier was not crossed");
        Assert.True(!AchievementRules.Crosses(tier, tier.Level, tier.Level + 1) && !AchievementRules.Crosses(tier, 1, tier.Level - 1), "an unreached or passed tier was crossed");
        Assert.True(AchievementRules.IsGranted((int)AchievementId.Streamer) && !AchievementRules.IsGranted((int)tier.Id) && !AchievementRules.IsGranted(999), "the granted badges differ");
        return Task.CompletedTask;
    }

    private static async Task StorageAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "etervale-achievements-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var repository = provider.GetRequiredService<IAchievementRepository>();
            Assert.True(await repository.TryAwardAsync(4, 1, 10, 100), "the first of a class did not win the top badge");
            Assert.True(!await repository.TryAwardAsync(4, 1, 11, 101), "a second player of the class won the top badge");
            Assert.True(await repository.TryAwardAsync(4, 2, 11, 102), "another class could not win the top badge");
            Assert.True(await repository.TryAwardAsync(3, 10, 10, 103) && !await repository.TryAwardAsync(3, 10, 10, 104), "a granted badge was held twice");
            Assert.True(await repository.LoadTitleAsync(10) is null, "a character that never chose has a title choice");
            await repository.SaveTitleAsync(10, 3);
            await repository.SaveTitleAsync(10, 0);
            Assert.True(await repository.LoadTitleAsync(10) == 0, "the title choice was not replaced");
            var listed = await repository.ListAsync(10, 64);
            Assert.True(listed.Count == 2 && listed[0] == new EarnedAchievement(4, 1, 100) && listed[1].AchievementId == 3, "the character's badges differ");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    private static async Task ClaimAsync()
    {
        var game = await BellatraGameTests.Arena.CreateAsync(PristonTale.Simulation.World.Bellatra.BellatraMode.Always, 114);
        var character = game.Session.Character!;
        var level = typeof(PlayerCharacter).GetProperty(nameof(PlayerCharacter.Level), BindingFlags.Public | BindingFlags.Instance)!;

        game.World.Tick(0.05f, 1000);
        level.SetValue(character, 115);
        game.World.Tick(0.05f, 1001);
        Assert.True(game.World.TopLevelClaims.TryRead(out var claim) && claim.Id == AchievementId.TopLevel115 && claim.Name == character.Name
            && claim.CharacterId == game.Session.Loaded!.Id, "reaching the level claimed no badge");

        game.World.Tick(0.05f, 1002);
        Assert.True(!game.World.TopLevelClaims.TryRead(out _), "a level kept claimed the badge again");

        game.Session.GmLevel = 1;
        level.SetValue(character, 125);
        game.World.Tick(0.05f, 1003);
        Assert.True(!game.World.TopLevelClaims.TryRead(out _), "a game master claimed a top badge");
    }

    private static async Task TitleAsync()
    {
        Assert.True(AchievementRules.TitleOf([]) == 0, "no badge gave a title");
        var title = AchievementRules.TitleOf([((int)AchievementId.LegacyMember, 7), ((int)AchievementId.TopLevel125, 1), ((int)AchievementId.TopLevel115, 1)]);
        Assert.True(AchievementRules.TitleId(title) == AchievementId.TopLevel125 && AchievementRules.TitleHolder(title) == 1, "the title is not the best badge");

        (int, long)[] earned = [((int)AchievementId.Streamer, 7), ((int)AchievementId.TopLevel125, 1)];
        Assert.True(AchievementRules.TitleId(AchievementRules.TitleOf(earned, null)) == AchievementId.TopLevel125, "with no choice the title is not the best badge");
        Assert.True(AchievementRules.TitleId(AchievementRules.TitleOf(earned, (int)AchievementId.Streamer)) == AchievementId.Streamer, "the chosen badge is not the title");
        Assert.True(AchievementRules.TitleOf(earned, AchievementRules.NoTitle) == 0, "a hidden title still shows");

        var game = await BellatraGameTests.Arena.CreateAsync(PristonTale.Simulation.World.Bellatra.BellatraMode.Always, 50);
        await WorldPump.RunAsync(game.World, game.World.SetAchievementTitleAsync(game.Player.EntityId, title));
        Assert.True(game.Player.AchievementTitle == title, "the world did not keep the title");
    }

    private static Task WireAsync()
    {
        var packet = new AchievementsPacket(3, [new AchievementWireEntry(4, 1), new AchievementWireEntry(3, 900)]);
        var buffer = new byte[AchievementsPacket.MaxSize];
        var read = AchievementsPacket.Read(buffer.AsSpan(0, packet.Write(buffer)));
        Assert.True(read.ActiveId == 3 && read.Entries.SequenceEqual(packet.Entries), "the badge list differs after the wire");
        var size = new AchievementTitlePacket(5).Write(buffer);
        Assert.True(AchievementTitlePacket.Read(buffer.AsSpan(0, size)).Id == 5, "the title choice differs after the wire");
        return Task.CompletedTask;
    }
}
