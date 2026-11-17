using PristonTale.Application.World;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Quests;

namespace PristonTale.Server.Net;

// Quest Courier requests: a Courier's board, and accept, abandon or turn in, answered by their status.
internal sealed partial class ClientSession
{
    private async Task CourierBoardAsync(CourierBoardRequestPacket request)
    {
        var board = _session is null || _player is null ? null : await _services.Couriers.BoardAsync(RequireToken(), request.NpcId);
        var packet = board is null
            ? new CourierBoardPacket(0, "", [])
            : new CourierBoardPacket(board.NpcId, board.Name,
                [.. board.Quests.Take(CourierWire.MaxQuests).Select(static shown => CourierWireMapping.Quest(shown.Quest, shown.Standing, shown.Kills, shown.CooldownSeconds, true))]);
        SendLarge(packet.Write, CourierBoardPacket.MaxSize);
    }

    private async Task CourierAsync(CourierRequestPacket request)
    {
        var status = CourierStatus.Rejected;
        long experience = 0;
        try
        {
            AdmitStoredOperation();
            if (_session is not null && _player is not null)
            {
                switch (request.Action)
                {
                    case CourierRequestPacket.Accept:
                        status = await _services.Couriers.AcceptAsync(RequireToken(), request.NpcId, request.QuestId);
                        break;
                    case CourierRequestPacket.Abandon:
                        status = await _services.Couriers.AbandonAsync(RequireToken(), request.QuestId);
                        break;
                    case CourierRequestPacket.TurnIn:
                        var turnIn = await _services.Couriers.TurnInAsync(RequireToken(), request.NpcId, request.QuestId);
                        (status, experience) = (turnIn.Status, (long)Math.Min(turnIn.Experience, long.MaxValue));
                        break;
                }
            }
        }
        catch (WorldBusyException)
        {
            status = CourierStatus.Rejected;
        }

        SendLarge(new CourierResultPacket(request.OperationId, (byte)status, experience).Write, CourierResultPacket.Size);
    }
}
