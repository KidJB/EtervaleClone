using System.Text.Json;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Persistence.Abstractions.Warehouses;

// A warehouse's items as SavedItems JSON whose anchors span its pages (WarehouseStorage).
public static class WarehouseStateCodec
{
    public static WarehouseStorage Restore(WarehouseRecord? record, ItemCatalog catalog)
    {
        if (record is null)
        {
            return new WarehouseStorage();
        }

        if (record.Revision < 0 || record.Money < 0 || record.Money > WarehouseStorage.MaxMoney)
        {
            throw new InvalidDataException("Invalid saved warehouse totals.");
        }

        return RestoreItems(new WarehouseStorage(record.Revision, record.Money), record.Items, catalog);
    }

    // Fills the empty storage with the saved items.
    public static WarehouseStorage RestoreItems(WarehouseStorage storage, JsonText items, ItemCatalog catalog)
    {
        if (CharacterStateCodec.IsBlank(items, JsonValueKind.Array))
        {
            return storage;
        }

        var state = JsonSerializer.Deserialize<SavedItems>(items.Value, CharacterStateCodec.Options);
        if (state is null || state.Version != 1 || state.Items is null || state.Items.Length > storage.ItemLimit)
        {
            throw new InvalidDataException("Unsupported warehouse state version or size.");
        }

        var identities = new HashSet<Guid>();
        foreach (var saved in state.Items)
        {
            var item = CharacterStateCodec.RestoreItem(saved, catalog, identities);
            if (saved.Slot.HasValue || !storage.Restore(item, saved.Anchor))
            {
                throw new InvalidDataException("Invalid saved warehouse footprint.");
            }
        }

        return storage;
    }

    public static WarehouseRecord Capture(long accountId, WarehouseStorage storage) =>
        new(accountId, storage.Money, storage.Revision, CaptureItems(storage));

    public static JsonText CaptureItems(WarehouseStorage storage)
    {
        var items = storage.Occupied().Select(static cell => CharacterStateCodec.CaptureItem(cell.Item, cell.Anchor, null)).ToArray();
        return CharacterJson.Write(new SavedItems(1, items));
    }
}
