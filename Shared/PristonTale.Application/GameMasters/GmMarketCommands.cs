using System.Globalization;

namespace PristonTale.Application.GameMasters;

// The Character Market's game-master commands. Fees, limits and switching a market off are server settings (/setting market.*).
internal static class GmMarketCommands
{
    private const int ReportLines = 8;

    // /market_listings: the latest listings, in any state.
    public static async Task Listings(GmCommandContext context)
    {
        if (context.Commands.CharacterMarket is not { } market)
        {
            context.Reply("no character market on this server");
            return;
        }

        foreach (var listing in await market.RecentListingsAsync(ReportLines))
        {
            context.Reply(string.Create(CultureInfo.InvariantCulture, $"#{listing.Id} {listing.Name} Lv{listing.Level} {listing.Price} coins, {listing.State}, seller account {listing.SellerAccountId}"));
        }
    }

    // /market_sales: the latest sales.
    public static async Task Sales(GmCommandContext context)
    {
        if (context.Commands.CharacterMarket is not { } market)
        {
            context.Reply("no character market on this server");
            return;
        }

        foreach (var sale in await market.RecentSalesAsync(ReportLines))
        {
            context.Reply(string.Create(CultureInfo.InvariantCulture,
                $"#{sale.ListingId} {sale.CharacterName}: account {sale.SellerAccountId} to {sale.BuyerAccountId}, {sale.Price} coins, tax {sale.Tax}"));
        }
    }

    // /market_remove id [reason]: takes a listing down; the character can be played again.
    public static async Task Remove(GmCommandContext context)
    {
        if (context.Commands.CharacterMarket is not { } market || !long.TryParse(context.Word(), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            context.Reply("Usage: /market_remove <listing id> [reason]");
            return;
        }

        var removed = await market.RemoveAsync(id, context.Issuer.AccountId, context.Rest, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        context.Reply(removed ? string.Create(CultureInfo.InvariantCulture, $"listing {id} removed") : string.Create(CultureInfo.InvariantCulture, $"listing {id} is not active"));
    }

    // /market_expire: closes the listings past their date now.
    public static async Task Expire(GmCommandContext context)
    {
        if (context.Commands.CharacterMarket is not { } market)
        {
            context.Reply("no character market on this server");
            return;
        }

        var closed = await market.ExpireAsync(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        context.Reply(string.Create(CultureInfo.InvariantCulture, $"{closed} listings closed"));
    }
}
