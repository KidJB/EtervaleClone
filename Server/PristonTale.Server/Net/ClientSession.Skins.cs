using PristonTale.Contracts.Characters;
using PristonTale.Application.World;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;

namespace PristonTale.Server.Net;

// The Cabinet's messages: an open answers the catalog in parts; a purchase or a change of looks answers its status.
internal sealed partial class ClientSession
{
    private async Task OpenSkinsAsync(SkinOpenPacket request)
    {
        if (_session is null || _player is null)
        {
            return;
        }

        var view = await _services.Skins.CatalogAsync(RequireToken());
        var offset = 0;
        do
        {
            var count = Math.Min(view.Articles.Count - offset, SkinWire.MaxArticles);
            var part = new SkinArticle[count];
            for (var index = 0; index < count; index++)
            {
                part[index] = view.Articles[offset + index];
            }

            offset += count;
            var packet = new SkinCatalogPacket(request.OperationId, view.Balance, offset >= view.Articles.Count, view.Skins.Weapon, view.Skins.Armor,
                view.Skins.OffHand, view.Skins.Hair, part) { TwoHand = view.Skins.TwoHand };
            SendLarge(packet.Write, SkinCatalogPacket.MaxSize);
        }
        while (offset < view.Articles.Count);
    }

    private async Task BuySkinAsync(SkinBuyPacket request)
    {
        var reply = _session is null || _player is null ? (SkinStatus.Rejected, 0L) : await _services.Skins.BuyAsync(RequireToken(), request.Key);
        SkinReply(request.OperationId, reply);
    }

    private async Task ApplySkinsAsync(SkinApplyPacket request)
    {
        (SkinStatus Status, long Balance) reply;
        try
        {
            reply = _session is null || _player is null
                ? (SkinStatus.Rejected, 0L)
                : await _services.Skins.ApplyAsync(RequireToken(), new SkinSet(request.Weapon, request.Armor, request.OffHand, request.Hair, request.TwoHand));
        }
        catch (WorldBusyException)
        {
            reply = (SkinStatus.Rejected, 0L);
        }

        SkinReply(request.OperationId, reply);
    }

    private async Task UnlockSkinAsync(SkinUnlockPacket request)
    {
        (SkinStatus Status, string Key, long Balance) reply;
        try
        {
            reply = _session is null || _player is null ? (SkinStatus.Rejected, "", 0L) : await _services.Skins.UnlockFromItemAsync(RequireToken(), request.ItemId);
        }
        catch (WorldBusyException)
        {
            reply = (SkinStatus.Busy, "", 0L);
        }

        var result = new SkinUnlockResultPacket(request.OperationId, (byte)reply.Status, reply.Balance, reply.Key);
        SendLarge(result.Write, SkinUnlockResultPacket.MaxSize);
    }

    private async Task SendDealsAsync(CabinetDealsRequestPacket request)
    {
        if (_session is null || _player is null)
        {
            return;
        }

        var (deals, balance) = await _services.Deals.ListAsync(RequireToken());
        var wire = new List<CabinetWireDeal>(Math.Min(deals.Count, CabinetDealsPacket.MaxDeals));
        foreach (var view in deals.Take(CabinetDealsPacket.MaxDeals))
        {
            var deal = view.Deal;
            wire.Add(new CabinetWireDeal(deal.Id, deal.ItemKey, view.Look?.Name ?? deal.ItemKey, (byte)(view.Look?.Rarity ?? 0), deal.EndsAt, deal.NextBid,
                deal.TopBid, deal.TopName, view.Leading, deal.MinLevel, deal.Bids,
                [.. view.History.Select(static bid => new CabinetWireBid(bid.Name, bid.Amount, bid.PlacedAt))]));
        }

        var packet = new CabinetDealsPacket(request.OperationId, balance, _services.World.UtcNow.ToUnixTimeSeconds(), wire);
        SendLarge(packet.Write, CabinetDealsPacket.MaxSize);
    }

    private async Task BidAsync(CabinetBidPacket request)
    {
        var reply = _session is null || _player is null
            ? (CabinetBidStatus.Rejected, 0L)
            : await _services.Deals.BidAsync(RequireToken(), request.DealId, request.Amount);
        var result = new CabinetBidResultPacket(request.OperationId, (byte)reply.Item1, reply.Item2);
        SendLarge(result.Write, CabinetBidResultPacket.Size);
    }

    private void SkinReply(Guid operation, (SkinStatus Status, long Balance) reply)
    {
        var result = new SkinResultPacket(operation, (byte)reply.Status, reply.Balance);
        SendLarge(result.Write, SkinResultPacket.Size);
    }
}
