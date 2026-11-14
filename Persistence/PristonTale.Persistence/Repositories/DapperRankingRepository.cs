using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Rankings;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperRankingRepository(IDbConnectionFactory connectionFactory) : IRankingRepository
{
    private const string Select = """
        SELECT c.name, c.job_code, c.level, c.exp, COALESCE(k.name, '') AS clan_name
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
        var rows = await connection.QueryAsync<LevelRankRow>(new CommandDefinition(
            jobCode is null ? AllClasses : OneClass, new { count, jobCode = jobCode ?? 0, blocked = false }, cancellationToken: ct));
        return rows.AsList();
    }
}
