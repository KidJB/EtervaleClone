using System.Data.Common;
using PristonTale.Application.Bellatra;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions.Rankings;
using PristonTale.Simulation.Progression;

namespace PristonTale.Application.Rankings;

// One character on the level board: its place (1 first), and its share of its level's experience in hundredths of a percent.
public sealed record RankedCharacter(int Rank, string Name, PlayerClass Class, int Level, int ExpBasisPoints, string Clan);

// The rankings window's boards, read off the tick: the level board of all classes or of one, kept for a minute before it is
// read again, and Bellatra's best clans of the day before (BellatraClanAppService).
public sealed class RankingAppService(IRankingRepository repository, BellatraClanAppService bellatra, TimeProvider? clock = null)
{
    public const int BoardSize = 100;
    public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<int, (DateTimeOffset At, IReadOnlyList<RankedCharacter> Board)> _levels = [];

    // The level board of a class, or of all with none; null while storage refuses.
    public async Task<IReadOnlyList<RankedCharacter>?> LevelsAsync(PlayerClass? job, CancellationToken ct = default)
    {
        var key = job is { } chosen ? (int)chosen : 0;
        await _gate.WaitAsync(ct);
        try
        {
            var now = _clock.GetUtcNow();
            if (_levels.TryGetValue(key, out var kept) && now - kept.At < Fresh)
            {
                return kept.Board;
            }

            var rows = await repository.TopLevelsAsync(BoardSize, job is null ? null : key, ct);
            var board = new List<RankedCharacter>(rows.Count);
            foreach (var row in rows)
            {
                if (!Enum.IsDefined((PlayerClass)row.JobCode))
                {
                    continue;
                }

                board.Add(new RankedCharacter(board.Count + 1, row.Name, (PlayerClass)row.JobCode, row.Level,
                    ExperienceTable.ShareBasisPoints(row.Level, unchecked((ulong)row.Exp)), row.ClanName));
            }

            _levels[key] = (now, board);
            return board;
        }
        catch (DbException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Bellatra's best clans of the day before, as its secretary shows them; null while storage refuses.
    public async Task<IReadOnlyList<BellatraClanRank>?> BellatraAsync(ulong entityId) => (await bellatra.ViewAsync(entityId))?.Top;
}
