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

            // A class wears only looks its class may wear (an axe look never reaches a class without axes).
            var jobs = (uint)connection.Player.Character.Definition.JobBitCode;
            if (!Wearable(skins.Weapon, jobs) || !Wearable(skins.TwoHand, jobs) || !Wearable(skins.Armor, jobs) || !Wearable(skins.OffHand, jobs))
            {
                return false;
            }

            // Applying looks keeps the choice to hide them.
            connection.Player.Character.Skins = skins with { Hidden = connection.Player.Character.Skins.Hidden };
            return true;
        });

    // The costume button: the character shows its real gear to everyone (hidden) or its looks and costume again.
    public Task<bool> SetCosmeticsHiddenAsync(ulong entityId, bool hidden) =>
        EnqueueAsync(entityId, () =>
        {
            if (!_connections.TryGetValue(entityId, out var connection))
            {
                return false;
            }

            var character = connection.Player.Character;
            character.Skins = character.Skins with { Hidden = hidden };
            return true;
        });

    public Task<SkinSet> SkinsOfAsync(PlayerSession session) =>
        EnqueueAsync(session.EntityId, () => TryGetPlaying(session, out var connection) ? connection.Player.Character.Skins : SkinSet.None);

    private static bool Wearable(string key, uint jobs) => key.Length == 0 || SkinRules.Wearable(key, jobs);
}
