using System.Text.Json;
using PristonTale.Application.World;
using PristonTale.Simulation.World;

namespace PristonTale.Server.World;

// Keeps the world buffs' end times in a file beside the server, so they run on through a restart.
public sealed class WorldBuffService(WorldServer world, string path, ILogger<WorldBuffService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (File.Exists(path))
            {
                await world.RestoreWorldBuffsAsync(JsonSerializer.Deserialize<WorldBuffEnd[]>(await File.ReadAllTextAsync(path, stoppingToken)) ?? []);
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            logger.LogError(exception, "The world buffs in {Path} could not be read", path);
        }

        var saved = world.WorldBuffState;
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var state = world.WorldBuffState;
            if (ReferenceEquals(state, saved))
            {
                continue;
            }

            try
            {
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state), stoppingToken);
                saved = state;
            }
            catch (IOException exception)
            {
                logger.LogError(exception, "The world buffs could not be written to {Path}", path);
            }
        }
    }
}
