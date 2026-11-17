using PristonTale.Application.Rankings;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;

namespace PristonTale.Server.World;

// Reads the level boards (RankingAppService) once a minute, off the tick, and gives the world who wears which ranking aura
// (RankAuras). A board storage refuses keeps the auras already worn.
public sealed class RankAuraService(RankingAppService rankings, WorldServer world, ILogger<RankAuraService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RankingAppService.Fresh);
        do
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Ranking auras could not be refreshed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        if (await rankings.LevelsAsync(null, ct) is not { } overall)
        {
            return;
        }

        var classes = new List<IReadOnlyList<RankedCharacter>>();
        foreach (var playerClass in Enum.GetValues<PlayerClass>())
        {
            if (await rankings.LevelsAsync(playerClass, ct) is { } board)
            {
                classes.Add(board);
            }
        }

        await world.SetRankAurasAsync(RankAuras.Of(overall, classes));
    }
}
