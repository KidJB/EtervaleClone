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
                view.Skins.OffHand, view.Skins.Hair, part);
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
                : await _services.Skins.ApplyAsync(RequireToken(), new SkinSet(request.Weapon, request.Armor, request.OffHand, request.Hair));
        }
        catch (WorldBusyException)
        {
            reply = (SkinStatus.Rejected, 0L);
        }

        SkinReply(request.OperationId, reply);
    }

    private void SkinReply(Guid operation, (SkinStatus Status, long Balance) reply)
    {
        var result = new SkinResultPacket(operation, (byte)reply.Status, reply.Balance);
        SendLarge(result.Write, SkinResultPacket.Size);
    }
}
