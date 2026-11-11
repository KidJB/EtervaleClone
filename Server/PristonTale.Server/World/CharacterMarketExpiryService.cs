using PristonTale.Persistence.Abstractions.Markets;

namespace PristonTale.Server.World;

// Closes the Character Market's listings past their date, once a minute.
public sealed class CharacterMarketExpiryService(ICharacterMarketRepository market, ILogger<CharacterMarketExpiryService> logger) : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await market.ExpireAsync(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), stoppingToken);
            }
            catch (System.Data.Common.DbException failure)
            {
                logger.LogWarning(failure, "The character market's expired listings were not closed.");
            }
        }
    }
}
