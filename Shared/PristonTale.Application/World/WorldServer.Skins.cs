using PristonTale.Application.Sessions;
using PristonTale.Simulation.Characters;

namespace PristonTale.Application.World;

public sealed partial class WorldServer
{
    // The looks the character shows; viewers get them through its appearance.
    public Task<bool> ApplySkinsAsync(PlayerSession session, SkinSet skins) =>
        EnqueueAsync(session.EntityId, () =>
        {
            if (!TryGetPlaying(session, out var connection))
            {
                return false;
            }

            connection.Player.Character.Skins = skins;
            return true;
        });

    public Task<SkinSet> SkinsOfAsync(PlayerSession session) =>
        EnqueueAsync(session.EntityId, () => TryGetPlaying(session, out var connection) ? connection.Player.Character.Skins : SkinSet.None);
}
