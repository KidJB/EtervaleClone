using System.Threading.Channels;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Achievements;

namespace PristonTale.Application.World;

// A player who reached a top level tier; whether it is the first of its class is decided off the tick.
public sealed record TopLevelClaim(long CharacterId, string Name, PlayerClass Class, AchievementId Id, ulong EntityId);

public sealed partial class WorldServer
{
    private readonly Channel<TopLevelClaim> _topLevelClaims = Channel.CreateUnbounded<TopLevelClaim>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<TopLevelClaim> TopLevelClaims => _topLevelClaims.Reader;

    // The title the player shows; its looks change, so viewers are described it again.
    public Task SetAchievementTitleAsync(ulong entityId, int title) =>
        EnqueueAsync(NoOwner, () =>
        {
            if (_connections.TryGetValue(entityId, out var connection))
            {
                connection.Player.AchievementTitle = title;
            }

            return true;
        }, waitForCapacity: true);

    private void StepAchievements()
    {
        foreach (var connection in _connections.Values)
        {
            if (connection.Session is not { Character: { } character } session)
            {
                continue;
            }

            var from = connection.LastLevel;
            connection.LastLevel = character.Level;
            if (from == 0 || character.Level <= from || session.GmLevel != 0 || session.Loaded is not { } loaded)
            {
                continue;
            }

            foreach (var tier in AchievementRules.TopLevels)
            {
                if (AchievementRules.Crosses(tier, from, character.Level))
                {
                    _topLevelClaims.Writer.TryWrite(new TopLevelClaim(loaded.Id, character.Name, character.Class.Definition.Class, tier.Id, session.EntityId));
                }
            }
        }
    }
}
