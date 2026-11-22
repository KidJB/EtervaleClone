using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Server.Net;

// Encodes a service reply: the state messages it carries first, then the result, all with the request's operation id.
internal static class NpcWireMapping
{
    // A warehouse in messages of at most WarehousePacket.MaxItems items each, all of its revision; an empty one in one.
    public static IEnumerable<WarehousePacket> WarehouseParts(Guid operation, ulong npc, WarehouseView warehouse, uint opcode)
    {
        var start = 0;
        do
        {
            var part = warehouse.Items.Skip(start).Take(WarehousePacket.MaxItems).ToArray();
            yield return new WarehousePacket(operation, npc, warehouse.Revision, warehouse.Money, warehouse.Weight, warehouse.MaxWeight,
                (short)warehouse.Columns, (short)warehouse.Rows, part.Select(ItemWireMapping.StoreItem).ToArray())
            {
                Opcode = opcode,
                Details = part.Select(ItemWireMapping.Detail).ToArray()
            };
            start += WarehousePacket.MaxItems;
        }
        while (start < warehouse.Items.Count);
    }

    public static void SendState(NpcServiceRequestPacket request, NpcServiceReply reply, Action<ClientSession.PacketWrite> send)
    {
        var operation = request.OperationId;
        var npc = request.NpcId;
        if (reply.Catalog is { } catalog)
        {
            var entries = catalog.Take(ShopCatalogPacket.MaxItems)
                .Select(offer => new ShopCatalogEntry(offer.Code, (byte)offer.Tab, (byte)offer.Kind, (ushort)offer.Ordinal, offer.Price,
                    (short)offer.Width, (short)offer.Height, offer.MaxStack, offer.Name))
                .ToArray();
            send(new ShopCatalogPacket(operation, npc, entries).Write);
        }

        if (reply.Quotes is { } quotes)
        {
            var entries = quotes.Items.Take(ShopQuotesPacket.MaxItems)
                .Select(quote => new ShopQuoteEntry(quote.ItemId, quote.SellPrice, quote.RepairCost, quote.Durability, quote.MaxDurability))
                .ToArray();
            send(new ShopQuotesPacket(operation, npc, quotes.InventoryRevision, entries).Write);
        }

        if (reply.Warehouse is { } warehouse)
        {
            foreach (var part in WarehouseParts(operation, npc, warehouse, EtervaleOpcodes.Warehouse))
            {
                send(part.Write);
            }
        }

        if (reply.Destinations is { } destinations)
        {
            var entries = destinations.Take(TeleportListPacket.MaxDestinations)
                .Select(offer => new TeleportEntry(offer.Index, offer.FieldCode, offer.Price, offer.MinimumLevel, offer.Name))
                .ToArray();
            send(new TeleportListPacket(operation, npc, entries).Write);
        }

        if (reply.Work is { } work)
        {
            send(new ItemWorkPacket(operation, npc, request.Service, work.Result, work.ItemId, work.Code).Write);
        }

        var outcome = reply.Outcome;
        send(new NpcServiceResultPacket(operation, npc, request.Service, request.Action, (ushort)outcome.Status, outcome.Ok, outcome.Revision,
            outcome.StorageRevision, outcome.Message).Write);
    }
}
