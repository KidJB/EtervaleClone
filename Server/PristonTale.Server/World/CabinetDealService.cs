using PristonTale.Application.Services;

namespace PristonTale.Server.World;

// Closes The Cabinet's bidding deals whose time is over, every half minute: the leader receives the look.
public sealed class CabinetDealService(CabinetDealAppService deals, ILogger<CabinetDealService> logger) : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                foreach (var closed in await deals.CloseDueAsync(stoppingToken))
                {
                    logger.LogInformation("Cabinet deal {Id} for {Look} closed: {Outcome}", closed.Deal.Id, closed.Deal.ItemKey,
                        closed.Awarded ? $"won by {closed.Deal.TopName} at {closed.Deal.TopBid}" : closed.Refunded ? "winner refunded" : "no bids");
                }
            }
            catch (System.Data.Common.DbException failure)
            {
                logger.LogWarning(failure, "The Cabinet's ended deals were not closed.");
            }
        }
    }
}
