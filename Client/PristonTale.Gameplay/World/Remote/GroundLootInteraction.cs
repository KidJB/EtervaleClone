using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Gameplay.World.Remote;

// pickedUp hears every drop the server handed over, for its sound. toCursor, asked as an item's pickup leaves, says whether
// it comes to the inventory's cursor (InventoryCommand.PickupToBag); toCursorAnswered then hears that pickup's reply.
public sealed class GroundLootInteraction(Func<InventoryCommand, Task<InventoryOutcome>> submit, IClientLog log, Action<GroundDropView> pickedUp = null,
    Func<GroundDropView, bool> toCursor = null, Action<bool, long> toCursorAnswered = null)
{
    // The client picks up within this of a drop, inside the server's reach (FieldGroundLoot.PickupReach, 32) so a
    // request it sends is not refused for distance.
    private const float PickupReach = 28f;
    // A pet tries again at most this often, and leaves a drop the server refused for this long (another's drop, a full
    // bag, a key that only looked like a potion).
    private const double FetchIntervalMs = 300d;
    private const double RefusedForMs = 5000d;
    private readonly Dictionary<Guid, double> _refused = new();
    private readonly List<Guid> _expired = new();
    private readonly List<GroundDropView> _drops = new();
    private double _nextFetchMs;
    private Guid _selected;
    private Task<InventoryOutcome> _pending;
    private GroundDropView _pendingDrop;
    private bool _pendingToCursor;
    public bool Busy => _pending is not null;

    public void Select(Guid id)
    {
        if (!Busy)
        {
            _selected = id;
        }
    }

    public void Cancel() => _selected = Guid.Empty;

    private double _nowMs;

    // pet: a helper pet is beside the player and fetches gold, and potions too with potions; nowMs: the scene's clock.
    public void Update(GroundLootPresentation ground, Vector3 position, Vector2 serverPosition, OwnerState owner, bool pet = false, bool potions = false,
        double nowMs = 0d)
    {
        _nowMs = nowMs;
        if (_pending is not null)
        {
            if (_pending.IsCompleted)
            {
                if (_pending.IsCompletedSuccessfully && !_pending.Result.Ok)
                {
                    log.Info($"[Loot] Pickup rejected: {_pending.Result.Message}");
                    if (_pendingDrop is not null)
                    {
                        _refused[_pendingDrop.Id] = _nowMs + RefusedForMs;
                    }
                }
                else if (_pending.IsCompletedSuccessfully && _pendingDrop is not null)
                {
                    pickedUp?.Invoke(_pendingDrop);
                }

                if (_pending.IsFaulted)
                {
                    log.Error($"[Loot] Pickup failed: {_pending.Exception?.GetBaseException().Message}");
                }

                if (_pendingToCursor)
                {
                    var ok = _pending.IsCompletedSuccessfully && _pending.Result.Ok;
                    toCursorAnswered?.Invoke(ok, ok ? _pending.Result.Revision : 0);
                }

                _pending = null;
            }

            return;
        }

        if (_selected == Guid.Empty && pet && owner is { Life: > 0 } && nowMs >= _nextFetchMs)
        {
            _nextFetchMs = nowMs + FetchIntervalMs;
            Fetch(ground, serverPosition, owner, potions);
            return;
        }

        if (_selected == Guid.Empty || owner is null)
        {
            return;
        }

        if (owner.Life <= 0 || !ground.TryGet(_selected, out var target))
        {
            Cancel();
            return;
        }

        var planar = new Vector2(target.X, target.Z);
        if (Vector2.DistanceSquared(new Vector2(position.X, position.Z), planar) > PickupReach * PickupReach
            || Vector2.DistanceSquared(serverPosition, planar) > PickupReach * PickupReach)
        {
            return;
        }

        var id = _selected;
        Cancel();
        _pendingDrop = ground.TryGetDrop(id, out var drop) ? drop : null;
        _pendingToCursor = _pendingDrop is { Gold: false } && toCursor?.Invoke(_pendingDrop) == true;
        var destination = _pendingToCursor ? InventoryCommand.PickupToBag : 0;
        _pending = submit(new InventoryCommand(Guid.NewGuid(), owner.InventoryRevision, InventoryAction.Pickup, id, destination, 1, default));
        _ = ObserveAsync(_pending);
    }

    // The nearest gold (or potion) within the pet's reach of where the server has the player, as a pickup to the bag.
    private void Fetch(GroundLootPresentation ground, Vector2 serverPosition, OwnerState owner, bool potions)
    {
        _expired.Clear();
        foreach (var (id, until) in _refused)
        {
            if (until <= _nowMs)
            {
                _expired.Add(id);
            }
        }

        foreach (var id in _expired)
        {
            _refused.Remove(id);
        }

        GroundDropView nearest = null;
        var best = PetFetch.Reach * PetFetch.Reach;
        ground.CollectDrops(_drops);
        foreach (var drop in _drops)
        {
            // Where the drop is drawn (render space, as serverPosition), not the wire position it arrived with.
            if (!drop.Gold && !(potions && PetFetch.LooksLikePotion(drop.CatalogKey)) || _refused.ContainsKey(drop.Id)
                || !ground.TryGet(drop.Id, out var at))
            {
                continue;
            }

            var distance = Vector2.DistanceSquared(serverPosition, new Vector2(at.X, at.Z));
            if (distance <= best)
            {
                best = distance;
                nearest = drop;
            }
        }

        if (nearest is null)
        {
            return;
        }

        _pendingDrop = nearest;
        _pendingToCursor = false;
        _pending = submit(new InventoryCommand(Guid.NewGuid(), owner.InventoryRevision, InventoryAction.Pickup, nearest.Id, 0, 1, default));
        _ = ObserveAsync(_pending);
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Update reports the failure; observing also covers leaving the screen while I/O is pending.
        }
    }
}
