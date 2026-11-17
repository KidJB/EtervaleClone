using Microsoft.Xna.Framework;
using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Gameplay.Effects;

namespace PristonTale.Gameplay.World;

// The ranking auras (RankAuraPresentation) of the local player, from its own description, and of every other drawn player.
public sealed partial class GameScene
{
    private RankAuraPresentation _rankAuras;
    private RankAura _selfRankAura;

    // The ranking aura the local player wears, from its own description.
    public RankAura SelfRankAura => _selfRankAura;
    private Action<ulong, PlayerActorHandles, PristonTale.Client.Net.Presence.PlayerAppearanceInfo, Vector3> _rankAuraVisit;

    private void ShowRankAuras()
    {
        if (_skillFx is null || _remote is null)
        {
            return;
        }

        _rankAuras ??= new RankAuraPresentation(_skillFx);
        if (!PlayerDead)
        {
            _rankAuras.Show(_remote.SelfEntityId, _selfRankAura, PlayerPosition, _skillClockMs);
        }

        _remote.ForEachPlayer(_rankAuraVisit ??= (entityId, _, appearance, feet) => _rankAuras.Show(entityId, appearance.RankAura, feet, _skillClockMs));
        _rankAuras.Finish();
    }
}
