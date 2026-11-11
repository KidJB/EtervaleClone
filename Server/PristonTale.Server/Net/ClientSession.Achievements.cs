using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Achievements;

namespace PristonTale.Server.Net;

internal sealed partial class ClientSession
{
    private async Task AchievementTitleAsync(AchievementTitlePacket request)
    {
        if (_session is { Loaded: { } loaded } session)
        {
            await _services.Achievements.ChooseTitleAsync(session.EntityId, loaded.Id, (int)Math.Min(request.Id, int.MaxValue));
            await AchievementsAsync();
        }
    }

    private async Task AchievementsAsync()
    {
        if (_session?.Loaded is not { } loaded)
        {
            return;
        }

        var view = await _services.Achievements.ViewAsync(loaded.Id);
        var packet = new AchievementsPacket((uint)AchievementRules.TitleId(view.Title), [.. view.Earned.Select(static badge => new AchievementWireEntry((uint)badge.AchievementId, (ulong)Math.Max(0, badge.HolderKey)))]);
        SendLarge(packet.Write, AchievementsPacket.MaxSize);
    }
}
