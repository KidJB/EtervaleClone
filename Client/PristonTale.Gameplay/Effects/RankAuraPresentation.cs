using Microsoft.Xna.Framework;
using PristonTale.Contracts.Characters;
using PristonTale.Effects.Vfx;

namespace PristonTale.Gameplay.Effects;

// The ranking aura around each player who wears one (RankAura): the converted particle script of its place streams around
// the player, moved to where the player is drawn each frame and started again once an effect's longest run is over
// (VfxDefinition.MaxDuration, timed here so a restart never cuts a running aura short). Called once
// a frame: Show for each player wearing one, then Finish, which stops the auras of players not shown.
public sealed class RankAuraPresentation(SkillPresentation effects)
{
    private static readonly string[] Names = ["", "particle.PlayerRank1", "particle.PlayerRank2", "particle.PlayerRank3", "particle.PlayerRankOverall"];

    private const double RunMs = VfxDefinition.MaxDuration * 1000;
    // The aura stands this far above the feet: its ground ring lies 2 above its origin, which at the feet themselves sinks
    // under the drawn floor of stage fields whose surface rises over the collision height.
    private const float Lift = 8f;

    private readonly Dictionary<ulong, (RankAura Aura, VfxHandle Effect, double StartedMs)> _shown = new();
    private readonly HashSet<ulong> _seen = new();
    private readonly List<ulong> _gone = new();

    public static string NameOf(RankAura aura) => (int)aura < Names.Length ? Names[(int)aura] : "";

    public void Show(ulong entityId, RankAura aura, Vector3 feet, double nowMs)
    {
        if (aura == RankAura.None)
        {
            return;
        }

        _seen.Add(entityId);
        // An effect not spawned (the effect library still loading at world entry) is asked for again the next frame.
        if (!_shown.TryGetValue(entityId, out var shown) || shown.Aura != aura || !shown.Effect.Spawned || nowMs - shown.StartedMs >= RunMs)
        {
            shown.Effect.Stop();
            shown = (aura, effects.Spawn(NameOf(aura), entityId, entityId, feet + Vector3.Up * Lift), nowMs);
            _shown[entityId] = shown;
        }

        shown.Effect.MoveTo(feet + Vector3.Up * Lift);
    }

    public void Finish()
    {
        foreach (var entityId in _shown.Keys)
        {
            if (!_seen.Contains(entityId))
            {
                _gone.Add(entityId);
            }
        }

        foreach (var entityId in _gone)
        {
            _shown[entityId].Effect.Stop();
            _shown.Remove(entityId);
        }

        _gone.Clear();
        _seen.Clear();
    }
}
