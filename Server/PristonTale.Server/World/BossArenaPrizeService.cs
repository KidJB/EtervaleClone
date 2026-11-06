using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.ItemBoxes;

namespace PristonTale.Server.World;

// Puts the boss arena's prizes (WorldServer.ArenaPrizes) into their accounts' Item Distributor, off the tick.
public sealed class BossArenaPrizeService(WorldServer world, IItemBoxRepository box, ILogger<BossArenaPrizeService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            while (world.ArenaPrizes.TryDequeue(out var prize))
            {
                try
                {
                    await box.AddAsync(prize.AccountId, "Boss Arena", prize.Item, stoppingToken);
                }
                catch (System.Data.Common.DbException exception)
                {
                    logger.LogError(exception, "A boss arena prize for account {Account} could not be stored", prize.AccountId);
                }
            }
        }
    }
}
