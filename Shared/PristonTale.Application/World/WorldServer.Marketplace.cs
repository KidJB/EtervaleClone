using PristonTale.Simulation.Characters;
using PristonTale.Application.Sessions;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Markets;
using PristonTale.Simulation.PersonalShops;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// One unit price of one offered stack, wherever it stands. Own marks the viewer's own offer, which it can see but not buy.
public sealed record MarketplaceOffer(ulong SellerId, string Seller, int FieldId, string Location, OwnedItemView Item, long UnitPrice, bool Own, long Sequence,
    PersonalShopCurrency Currency = PersonalShopCurrency.Gold);

public sealed record MarketplacePage(MarketplacePageStatus Status, int Page, int Total, IReadOnlyList<MarketplaceOffer> Offers)
{
    public static MarketplacePage Refused(MarketplacePageStatus status) => new(status, 0, 0, []);
}

public sealed partial class WorldServer
{
    // The offers open at this moment: every playing seller's shop (what left its bag is pruned first) and every stall.
    public Task<MarketplacePage> BrowseMarketplaceAsync(PlayerSession session, ulong npcId, MarketplaceQuery query) =>
        EnqueueAsync(session.EntityId, () =>
        {
            if (!TryGetPlaying(session, out var viewer))
            {
                return MarketplacePage.Refused(MarketplacePageStatus.Rejected);
            }

            if (!AtMarketplace(viewer, npcId))
            {
                return MarketplacePage.Refused(MarketplacePageStatus.OutOfReach);
            }

            var found = new List<MarketplaceOffer>();
            foreach (var seller in _connections.Values)
            {
                seller.Player.PruneShop();
                if (seller.Player.Shop is { } shop)
                {
                    Collect(found, query, shop, seller.Player.Character.Inventory, seller.Player.EntityId, seller.Player.Character.Name, seller.FieldId, viewer, seller.Player.EntityId == viewer.Player.EntityId);
                }
            }

            foreach (var field in _fields.Values)
            {
                foreach (var stall in field.Stalls)
                {
                    Collect(found, query, stall.Shop, stall.Stock, stall.EntityId, stall.OwnerName, stall.FieldId, viewer, stall.AccountId == session.AccountId);
                }
            }

            found.Sort(Comparison(query.Sort));
            var pages = MarketplaceRules.Pages(found.Count);
            var page = Math.Clamp(query.Page, 0, pages - 1);
            return new MarketplacePage(MarketplacePageStatus.Ok, page, found.Count,
                found.Skip(page * MarketplaceRules.PageSize).Take(MarketplaceRules.PageSize).ToArray());
        });

    private bool AtMarketplace(Connection viewer, ulong npcId) =>
        !viewer.Player.IsDead && _fields[viewer.FieldId].TryGetNpc(npcId, out var npc) && NpcServiceTransactions.CanReach(viewer.Player, npc)
        && npc.Definition.Find(NpcServiceKind.Marketplace) is not null;

    private void Collect(List<MarketplaceOffer> found, MarketplaceQuery query, PersonalShop shop, Inventory stock, ulong sellerId, string sellerName, int fieldId, Connection viewer, bool own)
    {
        foreach (var entry in shop.Entries)
        {
            var anchor = stock.Find(entry.ItemId);
            if (anchor < 0 || stock[anchor] is not { } item || !MarketplaceRules.Matches(query, item, entry.UnitPrice, sellerName))
            {
                continue;
            }

            var location = _catalog.TryGetFieldByCode(fieldId, out var field) ? field.Name : "";
            var view = OwnedItemView.Of(item, entry.Cell, -1) with { Details = ItemDetailView.Of(item, viewer.Player.Character) };
            found.Add(new MarketplaceOffer(sellerId, sellerName, fieldId, location, view, entry.UnitPrice, own, shop.Sequence, entry.Currency));
        }
    }

    private static Comparison<MarketplaceOffer> Comparison(MarketplaceSort sort) => sort switch
    {
        MarketplaceSort.PriceLow => static (a, b) => Tied(a.UnitPrice.CompareTo(b.UnitPrice), a, b),
        MarketplaceSort.PriceHigh => static (a, b) => Tied(b.UnitPrice.CompareTo(a.UnitPrice), a, b),
        MarketplaceSort.Name => static (a, b) => Tied(string.Compare(a.Item.Name, b.Item.Name, StringComparison.OrdinalIgnoreCase), a, b),
        _ => static (a, b) => Tied(b.Sequence.CompareTo(a.Sequence), a, b)
    };

    // A fixed order among equals, so a page does not reshuffle between requests.
    private static int Tied(int order, MarketplaceOffer a, MarketplaceOffer b) => order != 0 ? order : a.Item.InstanceId.CompareTo(b.Item.InstanceId);
}
