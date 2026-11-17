using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Rankings;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Rankings;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;

namespace PristonTale.Tests;

// Ranking auras: who wears which, how a change reaches viewers, and the aura in a player's looks on the wire.
internal static class RankAuraTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("rank auras: the first three of each class wear First to Third and the first of all classes wears Overall", RuleAsync);
        yield return ("rank auras: the aura worn adds its experience bonus by place, and none without one", BonusAsync);
        yield return ("rank auras: a changed aura raises the player's description revision, so viewers see it", RevisionAsync);
        yield return ("rank auras: the aura travels in a player's looks; older looks read without one", WireAsync);
        yield return ("rankings: the stored level board reads characters without a clan and leaves game masters out", BoardAsync);
    }

    private static RankedCharacter Ranked(int rank, string name, PlayerClass playerClass) => new(rank, name, playerClass, 100, 0, "");

    private static Task RuleAsync()
    {
        var overall = new[] { Ranked(1, "Ace", PlayerClass.Fighter), Ranked(2, "Bea", PlayerClass.Archer) };
        var fighters = new[] { Ranked(1, "Ace", PlayerClass.Fighter), Ranked(2, "Cid", PlayerClass.Fighter), Ranked(3, "Dan", PlayerClass.Fighter),
            Ranked(4, "Eve", PlayerClass.Fighter) };
        var archers = new[] { Ranked(1, "Bea", PlayerClass.Archer) };
        var auras = RankAuras.Of(overall, [fighters, archers, []]);
        Assert.True(auras["ace"] == RankAura.Overall, "the first of all classes must wear Overall over its class place");
        Assert.True(auras["Cid"] == RankAura.Second && auras["Dan"] == RankAura.Third && auras["Bea"] == RankAura.First, "class places");
        Assert.True(!auras.ContainsKey("Eve") && RankAuras.Of([], []).Count == 0, "a fourth place or empty boards wear none");
        return Task.CompletedTask;
    }

    private static Task BonusAsync()
    {
        var timers = new PristonTale.Simulation.Items.Usable.PremiumTimers();
        Assert.True(PristonTale.Simulation.Items.Usable.PremiumRules.BoostExperience(1000, timers) == 1000, "no aura gave a bonus");
        foreach (var (aura, expected) in new[] { (RankAura.First, 1100UL), (RankAura.Second, 1070UL), (RankAura.Third, 1050UL), (RankAura.Overall, 1100UL) })
        {
            timers.RankAura = aura;
            Assert.True(PristonTale.Simulation.Items.Usable.PremiumRules.BoostExperience(1000, timers) == expected, $"{aura} gave the wrong experience");
        }

        return Task.CompletedTask;
    }

    private static Task RevisionAsync()
    {
        var descriptors = new PristonTale.Application.World.FieldDescriptors();
        var looks = new PristonTale.Application.World.PlayerAppearanceView("Ann", PlayerClass.Fighter, 0, 0, 0, "", "");
        static PristonTale.Application.World.EntityView View(PristonTale.Application.World.PlayerAppearanceView appearance) =>
            new(1, PristonTale.Contracts.World.EntityKind.Player, false, default, 0f, default, 1, 100, 100, appearance);

        var none = descriptors.RevisionOf(View(looks));
        var first = descriptors.RevisionOf(View(looks with { RankAura = RankAura.First }));
        var overall = descriptors.RevisionOf(View(looks with { RankAura = RankAura.Overall }));
        Assert.True(first != none && overall != first, "an aura change did not raise the description revision");
        return Task.CompletedTask;
    }

    private static Task WireAsync()
    {
        var buffer = new byte[EntityDescriptionPacket.MaximumSize + 64];
        var looks = new DescribedPlayer(PlayerClass.Fighter, 0, 0, 0, "", 0, 0, 0, false, false, 0, 0) { RankAura = (byte)RankAura.Overall };
        var packet = new EntityDescriptionPacket(9, 1, Contracts.World.EntityKind.Player, 10, 0, 1f, "Ace", "", looks);
        var length = packet.Write(buffer);
        Assert.True(EntityDescriptionPacket.Read(buffer.AsSpan(0, length)).Player is { RankAura: (byte)RankAura.Overall }, "the aura did not survive the wire");

        // A version 8 description: the same bytes without the aura's, its header saying 8.
        var older = buffer.AsSpan(0, length - 1).ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(older, older.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(older.AsSpan(8), 8);
        Assert.True(EntityDescriptionPacket.Read(older).Player is { RankAura: 0 }, "a version 8 description did not read without an aura");
        return Task.CompletedTask;
    }

    private static async Task BoardAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-ranks-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            await using (var connection = await provider.GetRequiredService<IDbConnectionFactory>().OpenAsync())
            {
                await connection.ExecuteAsync("INSERT INTO accounts (user_id, password_hash, gm_level) VALUES ('ann', 'x', 0), ('gm', 'x', 4)");
            }

            var characters = provider.GetRequiredService<ICharacterRepository>();
            var registry = new CharacterClassRegistry();
            await characters.CreateAsync(CharacterRecordFactory.NewStarter(registry, 1, "Ann", 0, PlayerClass.Fighter, 3));
            await characters.CreateAsync(CharacterRecordFactory.NewStarter(registry, 2, "Boss", 0, PlayerClass.Fighter, 3));
            var board = await provider.GetRequiredService<IRankingRepository>().TopLevelsAsync(10, (int)PlayerClass.Fighter);
            Assert.True(board.Count == 1 && board[0] is { Name: "Ann", JobCode: (int)PlayerClass.Fighter, ClanName: "" }, "the board did not read Ann alone without a clan");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
