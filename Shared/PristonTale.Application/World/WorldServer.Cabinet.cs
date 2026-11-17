using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;

namespace PristonTale.Application.World;

// A look unlocked from a bag item, prepared on a copy of the character: the status, the look and how it was unlocked.
public sealed record CabinetUnlock(SkinStatus Status, string Key = "", CosmeticSource Source = CosmeticSource.None);

// The Cabinet on the tick. A brush turns a bag item of its kind (armor and robes, or weapons and shields) into its look: the
// item and one brush leave the bag. A look's token leaves the bag for its look. Nothing is taken when the look is owned
// already, the item holds no brushable look, or no brush of its kind is carried. The account's unlock is stored with the
// character in one transaction (SkinAppService), so the items never leave without the look nor the look come without them.
public sealed partial class WorldServer
{
    public CabinetConfigStore? CabinetStore { get; set; }

    public CabinetConfig Cabinet => CabinetStore?.Current ?? CabinetConfig.Empty;

    public DateTimeOffset UtcNow => _clock.GetUtcNow();

    public Task<PreparedCharacterOperation<CabinetUnlock>?> PrepareCabinetUnlockAsync(PlayerSession session, Guid itemId, IReadOnlyCollection<string> owned)
    {
        return EnqueueAsync<PreparedCharacterOperation<CabinetUnlock>?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection))
            {
                return null;
            }

            var player = connection.Player;
            var original = player.Character;
            var refused = (SkinStatus status) => new PreparedCharacterOperation<CabinetUnlock>(original, null, null, new CabinetUnlock(status));
            var anchor = original.Inventory.Find(itemId);
            if (anchor < 0 || original.Inventory[anchor] is not { } item)
            {
                return refused(SkinStatus.NotEligible);
            }

            var config = Cabinet;
            var items = _worldData.Current.Items;
            var key = item.Definition.CatalogKey;
            CabinetLook? look;
            CosmeticSource source;
            var brushAnchor = -1;
            if (CabinetRules.IsToken(key))
            {
                look = config.RuleOfToken(key) is { } rule ? CabinetLooks.Find(rule.Key, items, config) : null;
                source = look is null ? CosmeticSource.None : TokenSource(look.Sources);
                if (look is not { Enabled: true })
                {
                    return refused(SkinStatus.NotEligible);
                }
            }
            else
            {
                look = CabinetLooks.OfItem(item.Definition, config);
                source = CosmeticSource.Brush;
                if (look is not { Brushable: true })
                {
                    return refused(SkinStatus.NotEligible);
                }

                brushAnchor = FindItem(original, CabinetRules.BrushKey(CabinetRules.BrushFor(look.Slot)));
                if (brushAnchor < 0)
                {
                    return refused(SkinStatus.NoBrush);
                }
            }

            if (owned.Contains(look.Key, StringComparer.OrdinalIgnoreCase))
            {
                return refused(SkinStatus.AlreadyOwned);
            }

            var candidate = original.CloneForInventoryOperation();
            candidate.Inventory.ConsumeAt(anchor);
            if (brushAnchor >= 0)
            {
                candidate.Inventory.ConsumeAt(brushAnchor);
            }

            return new PreparedCharacterOperation<CabinetUnlock>(original, candidate,
                CharacterRecordFactory.Capture(session.Loaded, candidate, connection.FieldId, player.Position),
                new CabinetUnlock(SkinStatus.Ok, look.Key, source));
        });
    }

    // Exempt from the quota: the unlock is stored.
    public Task<bool> CommitCabinetUnlockAsync(PlayerSession session, PreparedCharacterOperation<CabinetUnlock> prepared)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            var candidate = CheckReceived(session, prepared, "a Cabinet unlock");
            prepared.Original.CommitInventoryOperation(candidate);
            return true;
        }, waitForCapacity: true);
    }

    // How a token's look is recorded: the way its tokens are found (a drop, a quest, an event), or a monster drop.
    private static CosmeticSource TokenSource(CosmeticSource sources)
    {
        var found = sources & (CosmeticSource.MonsterDrop | CosmeticSource.BossDrop | CosmeticSource.Quest | CosmeticSource.Event);
        return found == CosmeticSource.None ? CosmeticSource.MonsterDrop : found;
    }

    private static int FindItem(PlayerCharacter character, string key)
    {
        foreach (var (anchor, item) in character.Inventory.Occupied())
        {
            if (string.Equals(item.Definition.CatalogKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return anchor;
            }
        }

        return -1;
    }
}
