using PristonTale.Application.Achievements;
using PristonTale.Application.World;

namespace PristonTale.Server.World;

// Stores the top level badges the world tick claims, one after another, off the tick.
public sealed class AchievementService(WorldServer world, AchievementAppService achievements, ILogger<AchievementService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var claim in world.TopLevelClaims.ReadAllAsync(stoppingToken))
        {
            try
            {
                await achievements.ClaimAsync(claim, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "The achievement {Achievement} of {Character} could not be stored", claim.Id, claim.Name);
            }
        }
    }
}
