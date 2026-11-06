using PristonTale.Application.World;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Quests;

namespace PristonTale.Server.Net;

// Daily quest requests: accept or claim at the giver, answered by their status.
internal sealed partial class ClientSession
{
    private async Task DailyQuestAsync(DailyQuestRequestPacket request)
    {
        DailyQuestStatus status;
        try
        {
            AdmitStoredOperation();
            status = _session is null || _player is null ? DailyQuestStatus.Rejected : request.Action switch
            {
                DailyQuestRequestPacket.Accept => await _services.DailyQuests.AcceptAsync(RequireToken(), request.NpcId, request.QuestId),
                DailyQuestRequestPacket.Claim => await _services.DailyQuests.ClaimAsync(RequireToken(), request.NpcId, request.QuestId),
                _ => DailyQuestStatus.Rejected
            };
        }
        catch (WorldBusyException)
        {
            status = DailyQuestStatus.Rejected;
        }

        var result = new DailyQuestResultPacket(request.OperationId, (byte)status);
        SendLarge(result.Write, DailyQuestResultPacket.Size);
    }
}
