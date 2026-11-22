using System.Data.Common;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Clans;
using PristonTale.Simulation.Social.Clans;

namespace PristonTale.Server.World;

// Keeps the clans' running world buffs in storage, so they run on through a restart: read at start (tried again until it
// succeeds, and nothing is written before, so a failed read never replaces what is stored), then written again whenever
// they change; a failed write is logged and tried again on the next check.
public sealed class ClanBuffService(WorldServer world, IClanStorageRepository storage, ILogger<ClanBuffService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var restored = await TryRestoreAsync(stoppingToken);
        var saved = world.ClanBuffState;
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!restored)
            {
                restored = await TryRestoreAsync(stoppingToken);
                saved = world.ClanBuffState;
                continue;
            }

            var state = world.ClanBuffState;
            if (ReferenceEquals(state, saved))
            {
                continue;
            }

            try
            {
                await storage.ReplaceBuffsAsync(state.Select(static end => new ClanBuffRecord(end.ClanId, (short)end.Kind, end.EndSeconds)).ToArray(), stoppingToken);
                saved = state;
            }
            catch (DbException exception)
            {
                logger.LogError(exception, "The clan buffs could not be stored");
            }
        }
    }

    private async Task<bool> TryRestoreAsync(CancellationToken stoppingToken)
    {
        try
        {
            var stored = await storage.LoadBuffsAsync(stoppingToken);
            await world.RestoreClanBuffsAsync(stored.Select(static row => new ClanBuffEnd(row.ClanId, row.Kind, row.EndsAt)).ToArray());
            return true;
        }
        catch (DbException exception)
        {
            logger.LogError(exception, "The clan buffs could not be read; they are read again before any is stored");
            return false;
        }
    }
}
