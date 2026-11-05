using PristonTale.Application.Services;

namespace PristonTale.Server.World;

// Stands the stored offline stalls in their fields once the world ticks.
public sealed class StallRestoreService(StallAppService stalls, ILogger<StallRestoreService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await stalls.RestoreAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException or ArgumentException or InvalidOperationException
            or System.Data.Common.DbException)
        {
            logger.LogError(exception, "Stored stalls could not be restored");
        }
    }
}
