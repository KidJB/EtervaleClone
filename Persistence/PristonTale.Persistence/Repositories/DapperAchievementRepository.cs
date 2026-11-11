using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Achievements;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperAchievementRepository(IDbConnectionFactory connectionFactory) : IAchievementRepository
{
    public async Task<bool> TryAwardAsync(int achievementId, long holderKey, long characterId, long atSeconds, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var inserted = await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO character_achievements (achievement_id, holder_key, character_id, earned_at)
            VALUES (@AchievementId, @HolderKey, @CharacterId, @AtSeconds)
            ON CONFLICT (achievement_id, holder_key) DO NOTHING
            """, new { AchievementId = achievementId, HolderKey = holderKey, CharacterId = characterId, AtSeconds = atSeconds }, cancellationToken: ct));
        return inserted == 1;
    }

    public async Task<int?> LoadTitleAsync(long characterId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<long>(new CommandDefinition(
            "SELECT achievement_id FROM character_titles WHERE character_id = @CharacterId", new { CharacterId = characterId }, cancellationToken: ct));
        foreach (var chosen in rows)
        {
            return (int)chosen;
        }

        return null;
    }

    public async Task SaveTitleAsync(long characterId, int achievementId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO character_titles (character_id, achievement_id) VALUES (@CharacterId, @AchievementId)
            ON CONFLICT (character_id) DO UPDATE SET achievement_id = excluded.achievement_id
            """, new { CharacterId = characterId, AchievementId = achievementId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<EarnedAchievement>> ListAsync(long characterId, int count, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<(long AchievementId, long HolderKey, long EarnedAt)>(new CommandDefinition(
            """
            SELECT achievement_id, holder_key, earned_at FROM character_achievements
            WHERE character_id = @CharacterId ORDER BY earned_at, achievement_id LIMIT @Count
            """, new { CharacterId = characterId, Count = count }, cancellationToken: ct));
        return [.. rows.Select(static row => new EarnedAchievement((int)row.AchievementId, row.HolderKey, row.EarnedAt))];
    }
}
