using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Rankings;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Rankings;
using PristonTale.Persistence.Abstractions.Security;
using PristonTale.Persistence.Sqlite;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;

namespace PristonTale.Tests;

// The rankings window's boards: the level board read from storage, its class filter and experience shares, and its
// messages on the wire.
internal static class RankingTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("rankings: the level board orders by level then experience, filters a class, shows clans and leaves game masters and blocked accounts out", LevelsAsync);
        yield return ("rankings: a board request and its answer round-trip on the wire in parts a frame holds, and an unknown board is refused", WireAsync);
    }

    private static async Task LevelsAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-rankings-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var accounts = provider.GetRequiredService<IAccountRepository>();
            var characters = provider.GetRequiredService<ICharacterRepository>();
            var classes = new CharacterClassRegistry();
            async Task<long> Add(string account, string name, PlayerClass job, int level, long exp)
            {
                var owner = await accounts.FindByUserIdAsync(account) ?? await accounts.CreateAsync(account, PasswordHasher.Hash("x"));
                var created = await characters.CreateAsync(CharacterRecordFactory.NewStarter(classes, owner.Id, name, 0, job, 3));
                await characters.UpdateAsync(created with { Level = level, Exp = exp });
                return created.Id;
            }

            var middle = (long)(ExperienceTable.ForLevel(100) + (ExperienceTable.ForLevel(101) - ExperienceTable.ForLevel(100)) / 2);
            var ann = await Add("ann", "Ann", PlayerClass.Fighter, 100, middle);
            await Add("bob", "Bob", PlayerClass.Magician, 100, (long)ExperienceTable.ForLevel(100));
            await Add("cid", "Cid", PlayerClass.Fighter, 120, (long)ExperienceTable.ForLevel(120));
            await Add("gm", "Keeper", PlayerClass.Fighter, 150, (long)ExperienceTable.ForLevel(150));
            await Add("ban", "Banned", PlayerClass.Fighter, 140, (long)ExperienceTable.ForLevel(140));
            await using (var connection = await provider.GetRequiredService<IDbConnectionFactory>().OpenAsync())
            {
                await connection.ExecuteAsync("UPDATE accounts SET gm_level = 3 WHERE user_id = 'gm'");
                await connection.ExecuteAsync("UPDATE accounts SET is_blocked = @yes WHERE user_id = 'ban'", new { yes = true });
                var clanId = await connection.ExecuteScalarAsync<long>(
                    "INSERT INTO clans (name, name_key, state, created_at, deadline_at, intro, mark_version) VALUES ('Wolves', 'wolves', 1, 0, 0, '', 0) RETURNING id");
                await connection.ExecuteAsync("INSERT INTO clan_members (character_id, clan_id, rank, joined_at) VALUES (@ann, @clanId, 1, 0)", new { ann, clanId });
            }

            var rankings = new RankingAppService(provider.GetRequiredService<IRankingRepository>(), null!);
            var all = await rankings.LevelsAsync(null);
            Assert.True(all is [{ Name: "Cid", Rank: 1 }, { Name: "Ann", Rank: 2, Clan: "Wolves" }, { Name: "Bob", Rank: 3, Clan: "" }],
                $"the level board order or contents differ: {string.Join(", ", all?.Select(static row => row.Name) ?? [])}");
            Assert.True(all![1].ExpBasisPoints is >= 4_999 and <= 5_001 && all[2].ExpBasisPoints == 0, $"the experience shares differ: {all[1].ExpBasisPoints} {all[2].ExpBasisPoints}");
            var fighters = await rankings.LevelsAsync(PlayerClass.Fighter);
            Assert.True(fighters is [{ Name: "Cid" }, { Name: "Ann", Rank: 2 }], "the class board kept another class");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task WireAsync()
    {
        Assert.True(RankingsPacket.MaxSize <= Protocol.Binary.FrameCodec.MaxFrameSize, $"a full ranking part ({RankingsPacket.MaxSize} bytes) exceeds a frame");
        var buffer = new byte[RankingsPacket.MaxSize];
        var request = new RankingsRequestPacket(RankingBoard.Level, (byte)PlayerClass.Knight);
        Assert.True(RankingsRequestPacket.Read(buffer.AsSpan(0, request.Write(buffer))) == request, "the request changed on the wire");
        var length = new RankingsRequestPacket(RankingBoard.Bellatra, 0).Write(buffer);
        buffer[length - 2] = 7;
        Assert.Throws<InvalidDataException>(() => RankingsRequestPacket.Read(buffer.AsSpan(0, length)), "an unknown board was accepted");

        var entries = Enumerable.Range(0, RankingWire.MaxEntries)
            .Select(static index => new RankingWireEntry("Name" + index, (byte)PlayerClass.Shaman, 150, 9999, "Clan" + index, index)).ToArray();
        var packet = new RankingsPacket(RankingBoard.Level, (byte)PlayerClass.Shaman, true, entries, RankingWire.MaxEntries);
        var read = RankingsPacket.Read(buffer.AsSpan(0, packet.Write(buffer)));
        Assert.True(read is { Board: RankingBoard.Level, Available: true, First: RankingWire.MaxEntries } && read.Entries.SequenceEqual(entries),
            "a full board part changed on the wire");
        var refused = RankingsPacket.Read(buffer.AsSpan(0, new RankingsPacket(RankingBoard.Bellatra, 0, false, []).Write(buffer)));
        Assert.True(refused is { Available: false, Entries.Count: 0 }, "an unavailable board changed on the wire");
        return Task.CompletedTask;
    }
}
