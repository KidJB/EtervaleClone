using System.Collections.Concurrent;
using PristonTale.Client.Net.Presence;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Items;

namespace PristonTale.Client.Net.Online;

// The markets' requests and answers. A Marketplace page's details arrive just before the page, under its operation id.
internal sealed class MarketRequests(Action<IFramePacket> send)
{
    private readonly PendingReplies<Guid, CharacterMarketPagePacket> _characterPages = new();
    private readonly PendingReplies<Guid, CharacterDetailView> _characterDetails = new();
    private readonly PendingReplies<Guid, MarketplaceView> _marketplace = new();
    private readonly ConcurrentDictionary<Guid, Dictionary<Guid, ItemDetailView>> _details = new();

    public Task<CharacterMarketPagePacket> CharacterMarketAsync(CharacterMarketRequestPacket request, CancellationToken ct) =>
        _characterPages.RequestAsync(request.OperationId, () => send(request), ct);

    public async Task<CharacterDetailView> CharacterMarketDetailAsync(CharacterMarketRequestPacket request, CancellationToken ct)
    {
        try
        {
            return await _characterDetails.RequestAsync(request.OperationId, () => send(request), ct);
        }
        finally
        {
            _details.TryRemove(request.OperationId, out _);
        }
    }

    public async Task<MarketplaceView> MarketplaceAsync(MarketplaceQueryPacket query, CancellationToken ct)
    {
        try
        {
            return await _marketplace.RequestAsync(query.OperationId, () => send(query), ct);
        }
        finally
        {
            _details.TryRemove(query.OperationId, out _);
        }
    }

    public void Handle(uint code, ReadOnlySpan<byte> packet)
    {
        switch (code)
        {
            case EtervaleOpcodes.CharacterMarketPage:
            {
                var page = CharacterMarketPagePacket.Read(packet);
                _characterPages.Complete(page.OperationId, page);
                break;
            }
            case EtervaleOpcodes.CharacterMarketDetail:
            {
                var detail = CharacterMarketDetailPacket.Read(packet);
                _details.TryRemove(detail.OperationId, out var values);
                var gear = detail.Gear.Select(piece =>
                {
                    var view = StoreItems.View(piece.Item);
                    return new CharacterGearInfo(piece.Group, piece.Slot,
                        values is not null && values.TryGetValue(view.InstanceId, out var value) ? view with { Details = value } : view);
                }).ToArray();
                _characterDetails.Complete(detail.OperationId,
                    new CharacterDetailView(detail.Status, detail.Strength, detail.Spirit, detail.Talent, detail.Agility, detail.Health, detail.Mounts, gear));
                break;
            }
            case EtervaleOpcodes.CharacterMarketItems:
            {
                var items = CharacterMarketItemsPacket.Read(packet);
                if (_characterDetails.IsWaiting(items.OperationId))
                {
                    _details[items.OperationId] = items.Items.ToDictionary(static entry => entry.InstanceId, OwnerItemDecoder.View);
                }

                break;
            }
            case EtervaleOpcodes.MarketplaceDetails:
            {
                var details = MarketplaceDetailsPacket.Read(packet);
                if (_marketplace.IsWaiting(details.OperationId))
                {
                    _details[details.OperationId] = details.Items.ToDictionary(static entry => entry.InstanceId, OwnerItemDecoder.View);
                }

                break;
            }
            case EtervaleOpcodes.MarketplacePage:
            {
                var page = MarketplacePagePacket.Read(packet);
                _details.TryRemove(page.OperationId, out var details);
                var offers = page.Offers.Select(offer =>
                {
                    var view = StoreItems.View(offer.Item);
                    return new MarketplaceOfferInfo(offer.SellerId, offer.Seller, offer.Location, offer.Own,
                        details is not null && details.TryGetValue(view.InstanceId, out var detail) ? view with { Details = detail } : view, offer.UnitPrice,
                        (PristonTale.Simulation.PersonalShops.PersonalShopCurrency)offer.Currency);
                }).ToArray();
                _marketplace.Complete(page.OperationId, new MarketplaceView(page.Status, page.Page, page.Total, offers));
                break;
            }
        }
    }

    public void FailAll(Exception error)
    {
        _details.Clear();
        _characterPages.FailAll(error);
        _characterDetails.FailAll(error);
        _marketplace.FailAll(error);
    }
}
