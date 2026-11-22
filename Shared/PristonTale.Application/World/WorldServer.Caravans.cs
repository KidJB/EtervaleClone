using PristonTale.Simulation.Characters;

namespace PristonTale.Application.World;

public sealed partial class WorldServer
{
    // A caravan whose time ran out leaves its owner (its storage stays); viewers are described the owner again.
    private void StepCaravans()
    {
        var now = _clock.GetUtcNow().ToUnixTimeSeconds();
        foreach (var connection in _connections.Values)
        {
            if (connection.Player.Character.Caravan is { } caravan && !caravan.ActiveAt(now))
            {
                connection.Player.Character.Caravan = null;
            }
        }
    }

    // Names the caravan out (empty clears the name); false without a caravan or for a name PetNames refuses.
    public Task<bool> NameCaravanAsync(ulong entityId, string raw) =>
        EnqueueAsync(entityId, () =>
        {
            if (!_connections.TryGetValue(entityId, out var connection) || connection.Player.Character.Caravan is not { } caravan
                || !caravan.ActiveAt(_clock.GetUtcNow().ToUnixTimeSeconds()))
            {
                return false;
            }

            var name = "";
            if (raw.Length > 0 && !PetNames.TryNormalize(raw, out name))
            {
                return false;
            }

            connection.Player.Character.Caravan = caravan with { Name = name };
            return true;
        });

    // Makes the caravan out stand where it is (stays) or follow its owner; false without a caravan.
    public Task<bool> SetCaravanStaysAsync(ulong entityId, bool stays) =>
        EnqueueAsync(entityId, () =>
        {
            if (!_connections.TryGetValue(entityId, out var connection) || connection.Player.Character.Caravan is not { } caravan
                || !caravan.ActiveAt(_clock.GetUtcNow().ToUnixTimeSeconds()))
            {
                return false;
            }

            connection.Player.Character.Caravan = caravan with { Stays = stays };
            return true;
        });

    // Sends the caravan out away before its time; its storage stays for the next one. False without a caravan.
    public Task<bool> DismissCaravanAsync(ulong entityId) =>
        EnqueueAsync(entityId, () =>
        {
            if (!_connections.TryGetValue(entityId, out var connection) || connection.Player.Character.Caravan is null)
            {
                return false;
            }

            connection.Player.Character.Caravan = null;
            return true;
        });
}
