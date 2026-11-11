using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Simulation.Markets;
using PristonTale.Simulation.PersonalShops;

namespace PristonTale.Application.Markets;

// The Marketplace NPC: a search over the personal shops and stalls open right now, and a purchase that runs the personal shop's own
// purchase with the distance waived for a buyer at the NPC. It holds no stock of its own.
public sealed class MarketplaceAppService(ISessionRegistry sessions, WorldServer world, PersonalShopAppService shops, MarketOptionsHolder options)
{
    public Task<MarketplacePage> BrowseAsync(string token, ulong npcId, MarketplaceQuery query)
    {
        if (!options.MarketplaceEnabled)
        {
            return Task.FromResult(MarketplacePage.Refused(MarketplacePageStatus.Disabled));
        }

        return sessions.TryGet(token, out var session)
            ? world.BrowseMarketplaceAsync(session, npcId, query)
            : Task.FromResult(MarketplacePage.Refused(MarketplacePageStatus.Rejected));
    }

    public Task<PersonalShopReply> BuyAsync(string token, ulong npcId, PersonalShopPurchase purchase, CancellationToken ct = default)
    {
        if (!options.MarketplaceEnabled)
        {
            return Task.FromResult(new PersonalShopReply(PersonalShopOutcome.Failed(PersonalShopStatus.Rejected, 0)));
        }

        return shops.BuyAsync(token, purchase, ct, npcId);
    }
}
