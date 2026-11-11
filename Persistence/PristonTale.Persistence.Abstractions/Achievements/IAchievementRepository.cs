namespace PristonTale.Persistence.Abstractions.Achievements;

public sealed record EarnedAchievement(int AchievementId, long HolderKey, long EarnedAt);

public interface IAchievementRepository
{
    // False when the badge already has this holder key.
    Task<bool> TryAwardAsync(int achievementId, long holderKey, long characterId, long atSeconds, CancellationToken ct = default);

    // The character's title choice: a badge id, 0 for none, null when it never chose.
    Task<int?> LoadTitleAsync(long characterId, CancellationToken ct = default);

    Task SaveTitleAsync(long characterId, int achievementId, CancellationToken ct = default);

    Task<IReadOnlyList<EarnedAchievement>> ListAsync(long characterId, int count, CancellationToken ct = default);
}
