using PristonTale.Contracts.Characters;

namespace PristonTale.Application.World;

public sealed partial class WorldServer
{
    private IReadOnlyDictionary<string, RankAura> _rankAuras = new Dictionary<string, RankAura>();

    // The ranking auras by character name (RankAuras), refreshed off the tick: every player in game takes its own, and so
    // does each one entering; a changed aura changes its looks, so viewers are described it again.
    public Task SetRankAurasAsync(IReadOnlyDictionary<string, RankAura> auras) =>
        EnqueueAsync(NoOwner, () =>
        {
            _rankAuras = auras;
            foreach (var connection in _connections.Values)
            {
                ApplyRankAura(connection.Player);
            }

            return true;
        }, waitForCapacity: true);

    // Held on the premium timers: the player's looks show it and the experience rule reads its bonus (RankAuraBonus).
    private void ApplyRankAura(WorldPlayer player) =>
        player.Character.Premium.RankAura = _rankAuras.TryGetValue(player.Character.Name, out var aura) ? aura : RankAura.None;
}
