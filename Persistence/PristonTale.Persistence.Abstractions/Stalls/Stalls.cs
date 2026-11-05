using System.Text.Json;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.PersonalShops;

namespace PristonTale.Persistence.Abstractions.Stalls;

// How a stall looks: its owner as it stood when it set the stall up, and the stall's sign.
public sealed record StallLook(string Name, int Class, int Tier, int Face, int Hair, string Model, string Model2, string Weapon, string Armor,
    string OffHand, string Sign, int Level);

// A stall as stored: its account (one each), the owning character, where it stands, its look (StallLook JSON) and its stock
// (StallItem JSON array).
public sealed record StallRecord(long Id, long AccountId, long CharacterId, int FieldId, double X, double Y, double Z, double Facing, string Look, string Items);

// One listed item of a stall's stock: the item, its price per unit and its store grid cell.
public sealed record StallItem(SavedItem Item, long Price, int Cell);

public enum StallSaleStore
{
    Stored,
    Gone,
    GoldLimit,
    Failed
}

// A sale to store: the buyer's character, the stall's stock after it (empty closes the stall) and the price for the owner,
// written to the owner's character row (Owner when it plays, its snapshot) or added to its gold (offline, within
// OwnerMoneyLimit).
public sealed record StallSale(CharacterRecord Buyer, long StallId, string Items, bool Empty, long OwnerCharacterId, long Price, long OwnerMoneyLimit,
    CharacterRecord? Owner);

public interface IStallRepository
{
    Task<IReadOnlyList<StallRecord>> ListAsync(CancellationToken ct = default);

    // Writes the seller's character (without the listed items) and the stall in one transaction; the stall's id, or null when
    // the account already has one or the character row was not written.
    Task<long?> CreateWithCharacterAsync(CharacterRecord seller, StallRecord stall, CancellationToken ct = default);

    Task<StallSaleStore> SellAsync(StallSale sale, CancellationToken ct = default);

    // Removes the account's stall and puts each unsold item into the account's Item Distributor in one transaction; the
    // stall's id, or null when the account has none.
    Task<long?> CloseAsync(long accountId, string sender, CancellationToken ct = default);
}

public static class StallCodec
{
    public static string CaptureItems(PersonalShop shop, Inventory stock)
    {
        var items = new List<StallItem>(shop.Entries.Count);
        foreach (var entry in shop.Entries)
        {
            var anchor = stock.Find(entry.ItemId);
            if (anchor >= 0 && stock[anchor] is { } item)
            {
                items.Add(new StallItem(CharacterStateCodec.CaptureItem(item, entry.Cell, null), entry.UnitPrice, entry.Cell));
            }
        }

        return JsonSerializer.Serialize(items, CharacterStateCodec.Options);
    }

    public static (List<PersonalShopEntry> Entries, Inventory Stock) RestoreItems(string json, ItemCatalog catalog)
    {
        var items = JsonSerializer.Deserialize<StallItem[]>(json, CharacterStateCodec.Options) ?? [];
        var stock = StoreGrid.Create();
        var entries = new List<PersonalShopEntry>(items.Length);
        var identities = new HashSet<Guid>();
        foreach (var stored in items)
        {
            var item = CharacterStateCodec.RestoreItem(stored.Item with { Anchor = 0, Slot = null }, catalog, identities);
            if (!stock.TryPlace(item, stored.Cell))
            {
                throw new InvalidDataException("A stall item does not fit its cell.");
            }

            entries.Add(new PersonalShopEntry(item.InstanceId, stored.Price, stored.Cell));
        }

        return (entries, stock);
    }

    // Each stored item as the Item Distributor keeps it.
    public static IEnumerable<string> ItemBoxItems(string json)
    {
        foreach (var stored in JsonSerializer.Deserialize<StallItem[]>(json, CharacterStateCodec.Options) ?? [])
        {
            yield return JsonSerializer.Serialize(stored.Item with { Anchor = 0 }, CharacterStateCodec.Options);
        }
    }

    public static string CaptureLook(StallLook look) => JsonSerializer.Serialize(look, CharacterStateCodec.Options);

    public static StallLook RestoreLook(string json) =>
        JsonSerializer.Deserialize<StallLook>(json, CharacterStateCodec.Options) ?? throw new InvalidDataException("A stall's look is empty.");
}
