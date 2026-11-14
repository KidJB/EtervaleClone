using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Application.World;

public sealed partial class WorldServer
{
    // An item a player carries or wears, as a chat line links it: its values as its holder sees them. Null when the player
    // left or no longer holds it.
    public Task<OwnedItemView?> LinkedItemAsync(ulong entityId, Guid itemId) =>
        EnqueueAsync(entityId, () =>
        {
            if (!_connections.TryGetValue(entityId, out var connection))
            {
                return null;
            }

            var character = connection.Player.Character;
            var item = Find(character, itemId);
            return item is null ? null : OwnedItemView.Of(item, 0, -1) with { Details = ItemDetailView.Of(item, character) };
        });

    private static ItemInstance? Find(PlayerCharacter character, Guid itemId)
    {
        var anchor = character.Inventory.Find(itemId);
        if (anchor >= 0)
        {
            return character.Inventory[anchor];
        }

        foreach (var worn in character.Equipment.Values)
        {
            if (worn.InstanceId == itemId)
            {
                return worn;
            }
        }

        return null;
    }
}
