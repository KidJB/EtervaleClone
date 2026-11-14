namespace PristonTale.Persistence.Abstractions.Rankings;

// A character on the level board: its clan's name is empty out of a clan.
public sealed record LevelRankRow(string Name, int JobCode, int Level, long Exp, string ClanName);

public interface IRankingRepository
{
    // The highest characters by level then experience (the older first on a tie), of one class's job code or of all
    // (null); game masters' and blocked accounts' characters are left out.
    Task<IReadOnlyList<LevelRankRow>> TopLevelsAsync(int count, int? jobCode, CancellationToken ct = default);
}
