using PristonTale.Application.Markets;
using PristonTale.Application.World;
using PristonTale.Contracts.Characters;
using PristonTale.Persistence.Abstractions.Markets;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Markets;
using PristonTale.Simulation.PersonalShops;

namespace PristonTale.Server.Net;

// The two markets' messages. Both are paged: no request sends more than one page of cards.
internal sealed partial class ClientSession
{
    private async Task CharacterMarketAsync(CharacterMarketRequestPacket request)
    {
        if (_player is null)
        {
            SendCharacterMarket(request, CharacterMarketStatus.Rejected, 0, 0, 0, []);
            return;
        }

        try
        {
            await RunCharacterMarketAsync(request);
        }
        catch (System.Data.Common.DbException)
        {
            SendCharacterMarket(request, CharacterMarketStatus.StorageFailed, 0, 0, 0, []);
        }
    }

    private async Task RunCharacterMarketAsync(CharacterMarketRequestPacket request)
    {
        var market = _services.CharacterMarket;
        var token = RequireToken();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        switch (request.Action)
        {
            case MarketWire.ActionBrowse:
            {
                var (status, page, _) = await market.BrowseAsync(token, request.NpcId, request.Page, request.JobCode, (CharacterMarketSort)request.Sort, request.Text);
                SendCharacterMarket(request, status, await BalanceAsync(), request.Page, page.Total, Rows(page, now));
                break;
            }
            case MarketWire.ActionMine:
            {
                var (status, page) = await market.MineAsync(token, request.NpcId, request.Page);
                SendCharacterMarket(request, status, await BalanceAsync(), request.Page, page.Total, Rows(page, now));
                break;
            }
            case MarketWire.ActionForSale:
            {
                var (status, characters) = await market.CharactersForSaleAsync(token, request.NpcId);
                var rows = characters.Take(MarketWire.MaxCharacterRows)
                    .Select(static row => new CharacterMarketRow(0, row.Name, "", row.JobCode, row.Level, row.Tier, row.ExpBasisPoints, 0, 0, 0, 0, (byte)row.Blocked)).ToArray();
                SendCharacterMarket(request, status, await BalanceAsync(), 0, rows.Length, rows);
                break;
            }
            case MarketWire.ActionDetail:
                await SendCharacterDetailAsync(request);
                break;
            case MarketWire.ActionList:
                SendStore(request, await market.ListAsync(token, request.NpcId, request.OperationId, request.Text, request.Price, request.HideSeller));
                break;
            case MarketWire.ActionCancel:
                SendStore(request, await market.CancelAsync(token, request.NpcId, request.Id));
                break;
            case MarketWire.ActionBuy:
                SendStore(request, await market.BuyAsync(token, request.NpcId, request.OperationId, request.Id, request.Price));
                break;
            default:
                SendCharacterMarket(request, CharacterMarketStatus.Rejected, 0, 0, 0, []);
                break;
        }
    }

    private Task<long> BalanceAsync() => _services.CoinShop.BalanceAsync(RequireToken());

    private async Task SendCharacterDetailAsync(CharacterMarketRequestPacket request)
    {
        var (status, detail) = await _services.CharacterMarket.DetailAsync(RequireToken(), request.NpcId, request.Id);
        var pieces = detail?.Equipment.Take(MarketWire.MaxEquipment).ToArray() ?? [];
        SendLarge(new CharacterMarketItemsPacket(request.OperationId, pieces.Select(static worn => ItemWireMapping.Detail(worn.Item)).ToArray()).Write, CharacterMarketItemsPacket.MaxSize);
        var gear = pieces.Select(static worn => new CharacterMarketGear((byte)worn.Group, (byte)worn.Slot, ItemWireMapping.StoreItem(worn.Item))).ToArray();
        var packet = new CharacterMarketDetailPacket(request.OperationId, (byte)status, detail?.Strength ?? 0, detail?.Spirit ?? 0, detail?.Talent ?? 0, detail?.Agility ?? 0, detail?.Health ?? 0, detail?.Mounts ?? 0, gear);
        SendLarge(packet.Write, CharacterMarketDetailPacket.MaxSize);
    }

    private void SendStore(CharacterMarketRequestPacket request, CharacterMarketStore store) => SendCharacterMarket(request, store.Status, store.Balance, 0, 0, []);

    private void SendCharacterMarket(CharacterMarketRequestPacket request, CharacterMarketStatus status, long balance, int page, int total, IReadOnlyList<CharacterMarketRow> rows)
    {
        var rules = _services.CharacterMarket.Options;
        var packet = new CharacterMarketPagePacket(request.OperationId, (byte)status, request.Action, balance, page, total, rules.ListingFee, rules.TaxPercent, rules.MinLevel, rules.MinPrice, rules.MaxPrice, rows);
        SendLarge(packet.Write, CharacterMarketPagePacket.MaxSize);
    }

    private static CharacterMarketRow[] Rows(CharacterListingPage page, long now) => page.Listings.Take(MarketWire.MaxCharacterRows).Select(listing =>
        new CharacterMarketRow(listing.Id, listing.Name, listing.SellerHidden ? "" : listing.SellerName, listing.JobCode, listing.Level, listing.Tier, listing.ExpBasisPoints, listing.Price,
            (int)((now - listing.CreatedAt) / CharacterMarketRules.SecondsPerDay), (int)Math.Max(0, (listing.ExpiresAt - now) / CharacterMarketRules.SecondsPerDay), (byte)listing.State, 0)
        {
            Look = new CharacterMarketLook(listing.FaceIndex, listing.HairIndex, listing.ArmorKey, listing.WeaponKey, listing.OffHandKey)
        }).ToArray();

    private async Task MarketplaceQueryAsync(MarketplaceQueryPacket request)
    {
        var page = _player is null
            ? MarketplacePage.Refused(MarketplacePageStatus.Rejected)
            : await _services.Marketplace.BrowseAsync(RequireToken(), request.NpcId, Query(request));
        var details = page.Offers.Select(static offer => ItemWireMapping.Detail(offer.Item)).ToArray();
        SendLarge(new MarketplaceDetailsPacket(request.OperationId, details).Write, MarketplaceDetailsPacket.MaxSize);
        var offers = page.Offers.Select(static offer =>
            new MarketplaceOfferWire(offer.SellerId, offer.Seller, offer.Location, offer.Own, ItemWireMapping.StoreItem(offer.Item), offer.UnitPrice, (byte)offer.Currency)).ToArray();
        SendLarge(new MarketplacePagePacket(request.OperationId, (byte)page.Status, page.Page, page.Total, offers).Write, MarketplacePagePacket.MaxSize);
    }

    private static MarketplaceQuery Query(MarketplaceQueryPacket request) => new(request.Page, (MarketplaceCategory)request.Category, request.Subcategory,
        request.Class == 0 ? null : (PlayerClass)request.Class, (ItemRarity)request.Rarity, request.MinPrice, request.MaxPrice, (MarketplaceSort)request.Sort, Limited(request.Item), Limited(request.Seller));

    private static string Limited(string search) => search.Length <= MarketplaceRules.MaxSearchLength ? search : search[..MarketplaceRules.MaxSearchLength];

    private async Task MarketplaceBuyAsync(MarketplaceBuyPacket request)
    {
        var purchase = new PersonalShopPurchase(request.OperationId, request.SellerId, request.ItemId, request.UnitPrice, request.Count, request.ExpectedRevision);
        PersonalShopReply reply;
        try
        {
            reply = _player is null
                ? new PersonalShopReply(PersonalShopOutcome.Failed(PersonalShopStatus.Rejected, 0))
                : await _services.Marketplace.BuyAsync(RequireToken(), request.NpcId, purchase);
        }
        catch (System.Data.Common.DbException)
        {
            reply = new PersonalShopReply(PersonalShopOutcome.Failed(PersonalShopStatus.StorageFailed, 0));
        }

        SendPersonalShop(request.OperationId, PersonalShopAction.Buy, reply with { Listing = null });
    }
}
