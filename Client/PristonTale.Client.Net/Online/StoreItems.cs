using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Client.Net.Online;

internal static class StoreItems
{
    // An item on the store grid (the warehouse, a personal shop) at its cell, as the HUD draws owned items.
    public static OwnedItemView View(StoreItemEntry entry) => new(entry.InstanceId, entry.Code, entry.Name, (ItemKind)entry.Kind, entry.Count,
        entry.Anchor, -1, entry.Width, entry.Height, ItemCodes.KeyOf(entry.Code));

    // One part of a warehouse (WarehousePacket), its items as the HUD draws them.
    public static Simulation.World.Npcs.WarehouseView View(Protocol.Packets.WarehousePacket packet) =>
        new(packet.Revision, packet.Money, packet.Weight, packet.MaxWeight, packet.Columns, packet.Rows, packet.Items
            .Select((entry, index) => index < packet.Details.Count && packet.Details[index].InstanceId == entry.InstanceId
                ? View(entry) with { Details = OwnerItemDecoder.View(packet.Details[index]) }
                : View(entry)).ToArray());
}
