using PristonTale.Client.Net.Presence;
using PristonTale.Protocol.Packets;

namespace PristonTale.Gameplay.World;

// The Quest Couriers: the local player's accepted hunts and the last board a Courier sent, as the server sent them, and the
// requests the HUD makes of a Courier.
public sealed partial class GameScene
{
    public CourierStatePacket Couriers { get; private set; } = new([]);

    public CourierBoardPacket CourierBoard { get; private set; }

    private void ObserveCouriers(PresenceUpdate update)
    {
        if (update.Couriers is { } couriers)
        {
            Couriers = couriers;
        }

        if (update.CourierBoard is { } board)
        {
            CourierBoard = board;
        }
    }

    public void SendCourierBoardRequest(ulong npcId) => _ = _playChannel?.SendCourierBoardRequestAsync(npcId, _presenceCts.Token);

    public Task<(byte Status, long Experience)> CourierAsync(ulong npcId, int questId, byte action) =>
        _playChannel is { } channel
            ? channel.CourierAsync(npcId, questId, action, _presenceCts.Token)
            : Task.FromResult(((byte)Simulation.Quests.CourierStatus.Rejected, 0L));
}
