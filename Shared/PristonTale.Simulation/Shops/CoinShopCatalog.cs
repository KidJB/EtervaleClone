namespace PristonTale.Simulation.Shops;

// The answer to a purchase; the values travel on the wire.
public enum CoinShopStatus : byte
{
    Bought = 0,
    NotEnoughCoins = 1,
    UnknownItem = 2,
    NoRoom = 3,
    Busy = 4,
    Rejected = 5
}

// One article of the coin shop. ItemKey names the catalog item delivered; Discount, when above zero, is the price that
// replaces Price. Bulk takes that percent off for every BulkStep bought, up to MaxBulk percent. Spec lets the buyer
// choose the copy's class specialization; Quantity lets the buyer choose how many (one stack).
public sealed record CoinShopItem(int Id, string Name, string Description, string ItemKey, int Price)
{
    public int Discount { get; init; }
    public int Bulk { get; init; }
    public int MaxBulk { get; init; }
    public bool Spec { get; init; }
    public bool Quantity { get; init; }
}

// A tab of the shop: its name, the category art it shows (Hud/CoinShop/categories/<Icon>.png) and its articles.
public sealed record CoinShopTab(int Id, string Name, string Icon, IReadOnlyList<CoinShopItem> Items);

public sealed record CoinShopCatalog(string Name, string Message, IReadOnlyList<CoinShopTab> Tabs)
{
    public static readonly CoinShopCatalog Empty = new("Item Shop", "", []);

    public CoinShopItem Find(int tabId, int itemId)
    {
        foreach (var tab in Tabs)
        {
            if (tab.Id != tabId)
            {
                continue;
            }

            foreach (var item in tab.Items)
            {
                if (item.Id == itemId)
                {
                    return item;
                }
            }
        }

        return null;
    }
}

public static class CoinShopPricing
{
    // The most one purchase may take of a quantity article, and the step its bulk discount counts in.
    public const int MaxQuantity = 100;
    public const int BulkStep = 5;

    public static int UnitPrice(CoinShopItem item) => item.Discount > 0 ? item.Discount : item.Price;

    // The quantity a purchase takes: one for a single article, 1..MaxQuantity (and the item's stack) for a quantity one.
    public static int Quantity(CoinShopItem item, int requested, int maxStack) =>
        item.Quantity ? Math.Clamp(requested, 1, Math.Min(MaxQuantity, Math.Max(1, maxStack))) : 1;

    public static int BulkPercent(CoinShopItem item, int quantity) =>
        item.Bulk > 0 && quantity >= BulkStep ? Math.Min(item.Bulk * (quantity / BulkStep), Math.Max(0, item.MaxBulk)) : 0;

    public static long Total(CoinShopItem item, int quantity)
    {
        var gross = (long)UnitPrice(item) * quantity;
        return gross - gross * BulkPercent(item, quantity) / 100;
    }
}
