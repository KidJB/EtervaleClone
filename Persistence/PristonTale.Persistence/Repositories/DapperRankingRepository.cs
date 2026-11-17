using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Rankings;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperRankingRepository(IDbConnectionFactory connectionFactory) : IRankingRepository
{
    private const string Select = """
        SELECT c.name, c.job_code, c.level, c.exp, CAST(COALESCE(k.name, '') AS TEXT) AS clan_name
        FROM characters c
        JOIN accounts a ON a.id = c.account_id
        LEFT JOIN clan_members m ON m.character_id = c.id
        LEFT JOIN clans k ON k.id = m.clan_id
        WHERE a.gm_level = 0 AND a.is_blocked = @blocked
        """;

    private const string Order = " ORDER BY c.level DESC, c.exp DESC, c.id LIMIT @count";
    private const string AllClasses = Select + Order;
    private const string OneClass = Select + " AND c.job_code = @jobCode" + Order;

    public async Task<IReadOnlyList<LevelRankRow>> TopLevelsAsync(int count, int? jobCode, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<RankRow>(new CommandDefinition(
            jobCode is null ? AllClasses : OneClass, new { count, jobCode = jobCode ?? 0, blocked = false }, cancellationToken: ct));
        return [.. rows.Select(static row => row.ToRow())];
    }

    // A board line as either provider returns it (whole numbers may come back as 64-bit integers); Dapper converts each column.
    private sealed class RankRow
    {
        public string Name { get; set; } = "";
        public int JobCode { get; set; }
        public int Level { get; set; }
        public long Exp { get; set; }
        public string ClanName { get; set; } = "";

        public LevelRankRow ToRow() => new(Name, JobCode, Level, Exp, ClanName);
    }
}
