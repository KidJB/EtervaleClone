using PristonTale.Application.Sessions;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// The clan warehouses on the tick: one copy per clan, read from storage when a member first opens it (NpcAppService) and
// replaced by each committed change. Every member with it open is sent the new view (WorldSnapshot.ClanWarehouse).
public sealed partial class WorldServer
{
    private readonly Dictionary<long, ClanWarehouseCopy> _clanWarehouses = new();

    // The player's clan (0 without one) and whether its warehouse still has to be read from storage.
    public Task<(long ClanId, bool Load)> ClanWarehouseToLoadAsync(PlayerSession session) =>
        EnqueueAsync(session.EntityId, () =>
        {
            if (!TryGetPlaying(session, out var connection) || connection.Player.Clan is not { } clan)
            {
                return (0L, false);
            }

            return (clan.ClanId, !_clanWarehouses.ContainsKey(clan.ClanId));
        });

    // The warehouse as storage holds it: kept unless the world's copy is as new already.
    public Task ProvideClanWarehouseAsync(long clanId, WarehouseStorage storage) =>
        EnqueueAsync(NoOwner, () =>
        {
            if (!_clanWarehouses.TryGetValue(clanId, out var copy) || copy.Storage.Revision < storage.Revision)
            {
                _clanWarehouses[clanId] = new ClanWarehouseCopy(storage);
            }

            return true;
        }, waitForCapacity: true);

    private WarehouseStorage? ClanWarehouseOf(WorldPlayer player) =>
        player.Clan is { } clan && _clanWarehouses.TryGetValue(clan.ClanId, out var copy) ? copy.Storage : null;

    // The view a player with the warehouse open is sent, made again only when the copy changed.
    private WarehouseView? ClanWarehouseViewOf(WorldPlayer player)
    {
        if (player.OpenClanWarehouse == 0 || player.Clan?.ClanId != player.OpenClanWarehouse || !_clanWarehouses.TryGetValue(player.OpenClanWarehouse, out var copy))
        {
            return null;
        }

        if (!ReferenceEquals(player.ClanWarehouseSeen, copy.Storage))
        {
            player.ClanWarehouseSeen = copy.Storage;
            player.ClanWarehouseShown = copy.Storage.View(player.Character);
        }

        return player.ClanWarehouseShown;
    }

    // An upgrade's new limits: the world's copy keeps its items and revision under them (Clan warehouse views are made again).
    public Task ResizeClanWarehouseAsync(long clanId, int slotLevel, int weightLevel) =>
        EnqueueAsync(NoOwner, () =>
        {
            if (_clanWarehouses.TryGetValue(clanId, out var copy))
            {
                _clanWarehouses[clanId] = new ClanWarehouseCopy(Simulation.Social.Clans.ClanWarehouse.Resized(copy.Storage, slotLevel, weightLevel));
            }

            return true;
        }, waitForCapacity: true);

    // Tick only, at commit: a stored change replaces the copy it was made from.
    private void CommitClanWarehouse(long clanId, WarehouseStorage changed)
    {
        if (!_clanWarehouses.TryGetValue(clanId, out var copy) || copy.Storage.Revision < changed.Revision)
        {
            _clanWarehouses[clanId] = new ClanWarehouseCopy(changed);
        }
    }

    private sealed class ClanWarehouseCopy(WarehouseStorage storage)
    {
        public WarehouseStorage Storage { get; } = storage;
    }
}
