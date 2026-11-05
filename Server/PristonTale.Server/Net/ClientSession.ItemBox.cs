using PristonTale.Application.Services;
using PristonTale.Application.World;
using PristonTale.Protocol.Packets;

namespace PristonTale.Server.Net;

// The Item Distributor's messages: an open answers each waiting item then the list's end; an action answers its result.
internal sealed partial class ClientSession
{
    private async Task OpenItemBoxAsync(ItemBoxOpenPacket request)
    {
        var (status, entries) = _session is null || _player is null
            ? (ItemBoxStatus.Rejected, [])
            : await _services.ItemBox.ListAsync(RequireToken(), request.NpcId);
        foreach (var entry in entries)
        {
            var packet = new ItemBoxEntryPacket(request.OperationId, entry.Id, new DateTimeOffset(DateTime.SpecifyKind(entry.CreatedAt, DateTimeKind.Utc))
                .ToUnixTimeMilliseconds(), entry.Sender, ItemWireMapping.StoreItem(entry.Item), ItemWireMapping.Detail(entry.Item));
            SendLarge(packet.Write, ItemBoxEntryPacket.MaxSize);
        }

        var end = new ItemBoxListPacket(request.OperationId, (byte)status, entries.Count);
        SendLarge(end.Write, ItemBoxListPacket.Size);
    }

    private async Task ActItemBoxAsync(ItemBoxActionPacket request)
    {
        ItemBoxStatus status;
        try
        {
            AdmitStoredOperation();
            status = _session is null || _player is null ? ItemBoxStatus.Rejected : request.Action switch
            {
                ItemBoxWire.ActionTake => await _services.ItemBox.TakeAsync(RequireToken(), request.NpcId, request.Id),
                ItemBoxWire.ActionDiscard => await _services.ItemBox.DiscardAsync(RequireToken(), request.NpcId, request.Id),
                ItemBoxWire.ActionSend => await _services.ItemBox.SendAsync(RequireToken(), request.NpcId, request.ItemId, request.Recipient),
                _ => ItemBoxStatus.Rejected
            };
        }
        catch (WorldBusyException)
        {
            status = ItemBoxStatus.Busy;
        }

        var result = new ItemBoxResultPacket(request.OperationId, (byte)status);
        SendLarge(result.Write, ItemBoxResultPacket.Size);
    }
}
